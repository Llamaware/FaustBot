using System.Globalization;
using System.Text;
using System.Net;
using Discord;
using Discord.WebSocket;
using FaustBot.Options;
using FaustBot.State;
using FaustBot.Vpn;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FaustBot.Services;

/// <summary>Polls the VPN server on an interval, keeps the status embed current and posts join/leave logs.</summary>
public sealed class VpnMonitorService(
    DiscordSocketClient client,
    VpnServerClient vpn,
    StatusEmbedBuilder embedBuilder,
    StateStore state,
    IOptionsMonitor<BotOptions> options,
    TimeProvider timeProvider,
    ILogger<VpnMonitorService> logger) : IHostedService
{
    private const string TimeFormat = "dddd, MMMM dd, h:mm:ss tt";
    private const string PausedNotice = "⏸️ VPN monitoring is paused. Use `/vpn start` to resume.";
    private const string OfflineNotice = "🔴 The bot is offline. This message will update when it's back.";

    // How far back to look for an embed to reuse when state.json doesn't have one.
    private const int AdoptSearchLimit = 20;

    private BotOptions Settings => options.CurrentValue;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _autoStartHandled;

    // Last snapshot of each hub that answered, for join/leave logs.
    private readonly Dictionary<string, HubSnapshot> _lastKnownHubs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unreachableHubs = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _serverUnreachableSince;

    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>Starts monitoring. Returns false if it was already running.</summary>
    public async Task<bool> StartMonitoringAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (IsRunning)
            {
                return false;
            }

            logger.LogInformation("Starting VPN monitoring service.");
            await SetPausedAsync(false);
            _lastKnownHubs.Clear();
            _loopCts = new CancellationTokenSource();
            _loopTask = RunLoopAsync(_loopCts.Token);
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>Stops monitoring until started again, including across restarts, and marks the embed as paused.
    /// Returns false if it wasn't running.</summary>
    public async Task<bool> StopMonitoringAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!await StopLoopAsync())
            {
                return false;
            }

            await SetPausedAsync(true);
            await TryPublishNoticeAsync(PausedNotice);
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>Formats a UTC time in the configured time zone.</summary>
    public string FormatLocalTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(Settings.TimeZone))
            .ToString(TimeFormat, CultureInfo.InvariantCulture);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += OnReadyAsync;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!await StopLoopAsync())
            {
                return;
            }

            // Don't leave live-looking data up while the bot is down. Shutdown mustn't hang on Discord.
            await TryPublishNoticeAsync(OfflineNotice, new RequestOptions { Timeout = 5000, CancelToken = cancellationToken });
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task OnReadyAsync()
    {
        // Ready fires again after every reconnect; only auto-start on the first one.
        if (_autoStartHandled || !Settings.AutoStartMonitoring)
        {
            return;
        }

        _autoStartHandled = true;
        if (await state.ReadAsync(s => s.MonitoringPaused))
        {
            logger.LogInformation("VPN monitoring was paused with /vpn stop, so it won't auto-start. Use /vpn start to resume.");
            return;
        }

        await StartMonitoringAsync();
    }

    /// <summary>Replaces the embed with a notice. Failures are logged, since monitoring has already stopped.</summary>
    private async Task TryPublishNoticeAsync(string notice, RequestOptions? requestOptions = null)
    {
        try
        {
            await PublishEmbedAsync(embedBuilder.BuildNotice(notice, timeProvider.GetUtcNow()), requestOptions);
        }
        catch (Exception ex) when (ex is Discord.Net.HttpException or TimeoutException
            or OperationCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not update the status embed.");
        }
    }

    private async Task SetPausedAsync(bool paused)
    {
        if (await state.ReadAsync(s => s.MonitoringPaused) != paused)
        {
            await state.UpdateAsync(s => s.MonitoringPaused = paused);
        }
    }

    private async Task<bool> StopLoopAsync()
    {
        if (_loopCts is null || _loopTask is null)
        {
            return false;
        }

        logger.LogInformation("Stopping VPN monitoring service.");
        await _loopCts.CancelAsync();
        await _loopTask;
        _loopCts.Dispose();
        _loopCts = null;
        _loopTask = null;
        return true;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        // Let StartMonitoringAsync return before the first tick runs.
        await Task.Yield();

        // PeriodicTimer never overlaps ticks: a slow tick delays the next one instead of running alongside it.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Settings.UpdateDelay), timeProvider);
        try
        {
            do
            {
                await TickAsync(cancellationToken);

                // Pick up an UpdateDelay changed by /bot reload.
                timer.Period = TimeSpan.FromSeconds(Settings.UpdateDelay);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped.
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            var hubs = await vpn.QueryAllHubsAsync(cancellationToken);
            TrackReachability(hubs);

            var changes = CollectUserChanges(hubs);
            if (Settings.EnableLogs && changes.Count > 0)
            {
                await PostUserChangesAsync(changes);
            }

            await PublishEmbedAsync(embedBuilder.Build(hubs, timeProvider.GetUtcNow(), _serverUnreachableSince));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // One failed update (e.g. Discord unreachable) must not stop the monitor; the next tick retries.
            logger.LogError(ex, "VPN status update failed.");
        }
    }

    /// <summary>Logs when hubs stop or start answering, rather than on every tick.</summary>
    private void TrackReachability(IReadOnlyList<HubSnapshot> hubs)
    {
        foreach (var hub in hubs)
        {
            if (hub.Status == HubStatus.Unreachable)
            {
                if (_unreachableHubs.Add(hub.Name))
                {
                    logger.LogWarning("Can't reach hub {Hub}: {Error}", hub.Name, hub.Error);
                }
            }
            else if (_unreachableHubs.Remove(hub.Name))
            {
                logger.LogInformation("Hub {Hub} is reachable again.", hub.Name);
            }
        }

        var serverUnreachable = hubs.Count > 0 && hubs.All(h => h.Status == HubStatus.Unreachable);
        if (!serverUnreachable)
        {
            _serverUnreachableSince = null;
        }
        else
        {
            _serverUnreachableSince ??= timeProvider.GetUtcNow();
        }
    }

    /// <summary>Compares each reachable hub to its last known state. Unreachable hubs are skipped, so an
    /// outage doesn't report everyone as having left.</summary>
    private List<string> CollectUserChanges(IReadOnlyList<HubSnapshot> hubs)
    {
        List<string> messages = [];
        foreach (var current in hubs.Where(h => h.Status != HubStatus.Unreachable))
        {
            if (_lastKnownHubs.TryGetValue(current.Name, out var previous))
            {
                var previousUsers = previous.Sessions.Select(s => s.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var currentUsers = current.Sessions.Select(s => s.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var joined in current.Sessions.Where(s => !previousUsers.Contains(s.Username)))
                {
                    messages.Add($"User {FormatUser(joined.Username)} has joined the {current.Name} hub at {FormatLocalTime(joined.CreatedUtc)}.");
                }

                foreach (var left in previous.Sessions.Where(s => !currentUsers.Contains(s.Username)))
                {
                    messages.Add($"User {FormatUser(left.Username)} has left the {current.Name} hub. Last seen at {FormatLocalTime(left.LastCommUtc)}.");
                }
            }

            _lastKnownHubs[current.Name] = current;
        }

        return messages;
    }

    /// <summary>Posts one message per update, split only if it exceeds Discord's message length limit.</summary>
    private async Task PostUserChangesAsync(List<string> lines)
    {
        foreach (var line in lines)
        {
            logger.LogInformation("{Message}", line);
        }

        var channel = await GetTextChannelAsync(Settings.LogChannelId);
        var message = new StringBuilder();
        foreach (var line in lines)
        {
            if (message.Length > 0 && message.Length + 1 + line.Length > DiscordConfig.MaxMessageSize)
            {
                await channel.SendMessageAsync(message.ToString(), allowedMentions: AllowedMentions.None);
                message.Clear();
            }

            if (message.Length > 0)
            {
                message.Append('\n');
            }
            message.Append(line);
        }

        await channel.SendMessageAsync(message.ToString(), allowedMentions: AllowedMentions.None);
    }

    /// <summary>Edits the status message in place, or posts a new one if it doesn't exist yet or was deleted.</summary>
    private async Task PublishEmbedAsync(Embed embed, RequestOptions? requestOptions = null)
    {
        var channelId = Settings.EmbedChannelId;
        var channel = await GetTextChannelAsync(channelId);
        var (savedChannelId, savedMessageId) = await state.ReadAsync(s => (s.EmbedChannelId, s.EmbedMessageId));

        var messageId = savedChannelId == channelId ? savedMessageId : null;
        if (savedChannelId != channelId && savedChannelId is { } oldChannelId && savedMessageId is { } oldMessageId)
        {
            await DeleteOldEmbedAsync(oldChannelId, oldMessageId);
        }

        messageId ??= await FindExistingEmbedAsync(channel);
        if (messageId is { } id)
        {
            try
            {
                await channel.ModifyMessageAsync(id, m => m.Embed = embed, requestOptions);
                await SaveEmbedLocationAsync(channelId, id);
                return;
            }
            catch (Discord.Net.HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation("The status message was deleted; posting a new one.");
            }
        }

        var message = await channel.SendMessageAsync(embed: embed, options: requestOptions);
        await SaveEmbedLocationAsync(channelId, message.Id);
    }

    private async Task SaveEmbedLocationAsync(ulong channelId, ulong messageId)
    {
        var unchanged = await state.ReadAsync(s => s.EmbedChannelId == channelId && s.EmbedMessageId == messageId);
        if (!unchanged)
        {
            await state.UpdateAsync(s =>
            {
                s.EmbedChannelId = channelId;
                s.EmbedMessageId = messageId;
            });
        }
    }

    /// <summary>Finds the bot's newest embed in the channel, e.g. one left by an older version without state.json.</summary>
    private async Task<ulong?> FindExistingEmbedAsync(IMessageChannel channel)
    {
        try
        {
            var messages = await channel.GetMessagesAsync(AdoptSearchLimit).FlattenAsync();
            var existing = messages
                .Where(m => m.Author.Id == client.CurrentUser.Id && m.Embeds.Count > 0)
                .MaxBy(m => m.Timestamp);
            if (existing is not null)
            {
                logger.LogInformation("Reusing existing status message {MessageId}.", existing.Id);
            }
            return existing?.Id;
        }
        catch (Discord.Net.HttpException ex)
        {
            // Most likely missing the Read Message History permission; a new message will be posted instead.
            logger.LogWarning("Could not search the embed channel for an existing status message: {Reason}", ex.Reason);
            return null;
        }
    }

    /// <summary>Removes the status message from a previously configured embed channel.</summary>
    private async Task DeleteOldEmbedAsync(ulong channelId, ulong messageId)
    {
        try
        {
            if (await client.GetChannelAsync(channelId) is IMessageChannel oldChannel)
            {
                await oldChannel.DeleteMessageAsync(messageId);
            }
        }
        catch (Discord.Net.HttpException ex)
        {
            logger.LogWarning("Could not delete the old status message in channel {ChannelId}: {Reason}", channelId, ex.Reason);
        }
    }

    private string FormatUser(string username) => Settings.MentionUserIds ? $"<@{username}>" : username;

    private async Task<IMessageChannel> GetTextChannelAsync(ulong channelId)
    {
        // Served from the gateway cache when possible, otherwise fetched over REST.
        return await client.GetChannelAsync(channelId) as IMessageChannel
            ?? throw new InvalidOperationException($"Channel {channelId} was not found or is not a text channel.");
    }
}
