using System.Globalization;
using System.Net;
using Discord;
using Discord.WebSocket;
using FaustBot.Options;
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
    IOptions<BotOptions> options,
    TimeProvider timeProvider,
    ILogger<VpnMonitorService> logger) : IHostedService
{
    private const string TimeFormat = "dddd, MMMM dd, h:mm:ss tt";

    private readonly BotOptions _options = options.Value;
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private IReadOnlyList<HubSnapshot>? _previousHubs;
    private ulong? _embedMessageId;

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
            _previousHubs = null;
            _loopCts = new CancellationTokenSource();
            _loopTask = RunLoopAsync(_loopCts.Token);
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>Stops monitoring and removes the status embed. Returns false if it wasn't running.</summary>
    public async Task<bool> StopMonitoringAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (!await StopLoopAsync())
            {
                return false;
            }

            await DeleteEmbedAsync();
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>Formats a UTC time in the configured time zone.</summary>
    public string FormatLocalTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZone).ToString(TimeFormat, CultureInfo.InvariantCulture);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // On shutdown the embed is left in place.
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            await StopLoopAsync();
        }
        finally
        {
            _stateLock.Release();
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.UpdateDelay), timeProvider);
        try
        {
            do
            {
                await TickAsync(cancellationToken);
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

            if (_options.EnableLogs && _previousHubs is not null)
            {
                await PostUserChangesAsync(_previousHubs, hubs);
            }
            _previousHubs = hubs;

            await ReplaceEmbedAsync(hubs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // One failed update (VPN server or Discord unreachable) must not stop the monitor; the next tick retries.
            logger.LogError(ex, "VPN status update failed.");
        }
    }

    private async Task ReplaceEmbedAsync(IReadOnlyList<HubSnapshot> hubs)
    {
        var channel = await GetTextChannelAsync(_options.EmbedChannelId);
        var embed = embedBuilder.Build(hubs, timeProvider.GetUtcNow());

        await DeleteEmbedAsync();
        var message = await channel.SendMessageAsync(embed: embed);
        _embedMessageId = message.Id;
    }

    private async Task DeleteEmbedAsync()
    {
        if (_embedMessageId is not { } messageId)
        {
            return;
        }

        try
        {
            var channel = await GetTextChannelAsync(_options.EmbedChannelId);
            await channel.DeleteMessageAsync(messageId);
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            // Already deleted by someone else.
        }

        _embedMessageId = null;
    }

    private async Task PostUserChangesAsync(IReadOnlyList<HubSnapshot> previousHubs, IReadOnlyList<HubSnapshot> currentHubs)
    {
        List<string> messages = [];
        foreach (var current in currentHubs)
        {
            var previous = previousHubs.FirstOrDefault(h => h.Name == current.Name);
            if (previous is null)
            {
                continue;
            }

            var previousUsers = previous.Sessions.ToDictionary(s => s.Username, StringComparer.OrdinalIgnoreCase);
            var currentUsers = current.Sessions.ToDictionary(s => s.Username, StringComparer.OrdinalIgnoreCase);

            foreach (var joined in current.Sessions.Where(s => !previousUsers.ContainsKey(s.Username)))
            {
                messages.Add($"User {FormatUser(joined.Username)} has joined the {current.Name} hub at {FormatLocalTime(joined.CreatedUtc)}.");
            }

            foreach (var left in previous.Sessions.Where(s => !currentUsers.ContainsKey(s.Username)))
            {
                messages.Add($"User {FormatUser(left.Username)} has left the {current.Name} hub. Last seen at {FormatLocalTime(left.LastCommUtc)}.");
            }
        }

        if (messages.Count == 0)
        {
            return;
        }

        var channel = await GetTextChannelAsync(_options.LogChannelId);
        foreach (var message in messages)
        {
            logger.LogInformation("{Message}", message);
            await channel.SendMessageAsync(message);
        }
    }

    private string FormatUser(string username) => _options.MentionUserIds ? $"<@{username}>" : username;

    private async Task<IMessageChannel> GetTextChannelAsync(ulong channelId)
    {
        // Served from the gateway cache when possible, otherwise fetched over REST.
        return await client.GetChannelAsync(channelId) as IMessageChannel
            ?? throw new InvalidOperationException($"Channel {channelId} was not found or is not a text channel.");
    }
}
