using Discord;
using Discord.Interactions;
using FaustBot.Services;
using FaustBot.Vpn;

namespace FaustBot.Modules;

public sealed class VpnMonitor(VpnMonitorService monitor, VpnServerClient vpn) : InteractionModuleBase<SocketInteractionContext>
{
    [RequireOwner]
    [SlashCommand("start", "Start VPN monitoring service.")]
    public async Task Start()
    {
        var started = await monitor.StartMonitoringAsync();
        await RespondAsync(started ? "VPN monitoring service started." : "VPN monitoring service is already running.");
    }

    [RequireOwner]
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
        await FollowupAsync($"The {snapshot.Name} hub is currently {(snapshot.Online ? "online" : "offline")}.");
    }
}
