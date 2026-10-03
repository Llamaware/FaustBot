using Discord;
using Discord.Interactions;
using FaustBot.Services;
using FaustBot.Vpn;

namespace FaustBot.Modules;

public sealed class VpnMonitor(VpnMonitorService monitor, VpnServerClient vpn) : InteractionModuleBase<SocketInteractionContext>
{
    private const string UnreachableMessage = "Can't reach the VPN server right now.";

    [RequireBotAdmin]
    [SlashCommand("start", "Start VPN monitoring service.")]
    public async Task Start()
    {
        var started = await monitor.StartMonitoringAsync();
        await RespondAsync(started ? "VPN monitoring service started." : "VPN monitoring service is already running.");
    }

    [RequireBotAdmin]
    [SlashCommand("stop", "Stop VPN monitoring service.")]
    public async Task Stop()
    {
        // Stopping waits for an in-progress update and deletes the embed, which can take longer than
        // the 3 seconds Discord allows for a response.
        await DeferAsync();
        var stopped = await monitor.StopMonitoringAsync();
        await FollowupAsync(stopped ? "VPN monitoring service stopped." : "VPN monitoring service is not running.");
    }

    [SlashCommand("list", "List current VPN sessions on one hub.")]
    public async Task List(string hubName)
    {
        var hub = vpn.FindHub(hubName);
        if (hub is null)
        {
            await RespondAsync("Hub not found.");
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

    [SlashCommand("status", "Print VPN hub status.")]
    public async Task Status(string hubName)
    {
        var hub = vpn.FindHub(hubName);
        if (hub is null)
        {
            await RespondAsync("Hub not found.");
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
}
