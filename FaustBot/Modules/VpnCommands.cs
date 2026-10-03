using Discord;
using Discord.Interactions;
using FaustBot.Services;
using FaustBot.Vpn;

namespace FaustBot.Modules;

[Group("vpn", "VPN hub status and monitoring.")]
public sealed class VpnCommands(VpnMonitorService monitor, VpnServerClient vpn) : InteractionModuleBase<SocketInteractionContext>
{
    private const string UnreachableMessage = "Can't reach the VPN server right now.";

    [RequireBotAdmin]
    [SlashCommand("start", "Start VPN monitoring.")]
    public async Task Start()
    {
        var started = await monitor.StartMonitoringAsync();
        await RespondAsync(started ? "VPN monitoring started." : "VPN monitoring is already running.", ephemeral: true);
    }

    [RequireBotAdmin]
    [SlashCommand("stop", "Pause VPN monitoring until it's started again.")]
    public async Task Stop()
    {
        // Stopping waits for an in-progress update and edits the embed, which can take longer than
        // the 3 seconds Discord allows for a response.
        await DeferAsync(ephemeral: true);
        var stopped = await monitor.StopMonitoringAsync();
        await FollowupAsync(stopped ? "VPN monitoring paused." : "VPN monitoring isn't running.", ephemeral: true);
    }

    [SlashCommand("status", "Show whether hubs are online.")]
    public async Task Status(
        [Summary("hub", "Hub to check. Leave empty for all hubs."), Autocomplete(typeof(HubAutocompleteHandler))]
        string? hubName = null)
    {
        if (hubName is null)
        {
            await DeferAsync();
            var hubs = await vpn.QueryAllHubsAsync(CancellationToken.None);
            await FollowupAsync(hubs.All(h => h.Status == HubStatus.Unreachable)
                ? UnreachableMessage
                : string.Join('\n', hubs.Select(FormatStatusLine)));
            return;
        }

        var hub = vpn.FindHub(hubName);
        if (hub is null)
        {
            await RespondAsync(UnknownHubMessage(hubName), ephemeral: true);
            return;
        }

        await DeferAsync();
        var snapshot = await vpn.QueryHubAsync(hub, CancellationToken.None);
        await FollowupAsync(snapshot.Status switch
        {
            HubStatus.Online => $"The {snapshot.Name} hub is currently online.",
            HubStatus.Offline => $"The {snapshot.Name} hub is currently offline.",
            _ => UnreachableMessage,
        });
    }

    [SlashCommand("list", "List the users connected to a hub.")]
    public async Task List(
        [Summary("hub", "Hub to list users for."), Autocomplete(typeof(HubAutocompleteHandler))] string hubName)
    {
        var hub = vpn.FindHub(hubName);
        if (hub is null)
        {
            await RespondAsync(UnknownHubMessage(hubName), ephemeral: true);
            return;
        }

        await DeferAsync();
        var snapshot = await vpn.QueryHubAsync(hub, CancellationToken.None);
        if (snapshot.Status == HubStatus.Unreachable)
        {
            await FollowupAsync(UnreachableMessage);
            return;
        }

        var output = snapshot.Sessions.Count == 0
            ? $"No users are currently connected to {snapshot.Name}."
            : string.Join('\n', snapshot.Sessions.Select(s =>
                $"Username: {s.Username}, Session Created: {monitor.FormatLocalTime(s.CreatedUtc)}"));

        if (output.Length > DiscordConfig.MaxMessageSize)
        {
            output = output[..(DiscordConfig.MaxMessageSize - 1)] + "…";
        }

        await FollowupAsync(output);
    }

    private static string FormatStatusLine(HubSnapshot hub) => hub.Status switch
    {
        HubStatus.Online => $"**{hub.Name}**: online, {hub.Sessions.Count} {(hub.Sessions.Count == 1 ? "user" : "users")}",
        HubStatus.Offline => $"**{hub.Name}**: offline",
        _ => $"**{hub.Name}**: unreachable",
    };

    private string UnknownHubMessage(string hubName) =>
        $"There's no hub called \"{hubName}\". Hubs: {string.Join(", ", vpn.Hubs.Select(h => h.Name))}";
}
