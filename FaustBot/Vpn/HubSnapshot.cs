namespace FaustBot.Vpn;

public enum HubStatus
{
    Online,
    Offline,

    /// <summary>The VPN server didn't answer, so the hub's real status is unknown.</summary>
    Unreachable,
}

/// <summary>A user connected to a hub. Times are UTC.</summary>
public sealed record VpnSession(string Username, DateTime CreatedUtc, DateTime LastCommUtc);

/// <summary>The state of one hub at the time it was queried.</summary>
/// <param name="Sessions">Connected users, one entry per username, excluding the configured IgnoreList.</param>
/// <param name="HasTerminal">Whether the configured TerminalName is connected (it may be in the IgnoreList).</param>
/// <param name="Error">Why the hub is <see cref="HubStatus.Unreachable"/>.</param>
public sealed record HubSnapshot(
    string Name,
    HubStatus Status,
    IReadOnlyList<VpnSession> Sessions,
    bool HasTerminal,
    string? Error = null)
{
    public static HubSnapshot Unreachable(string name, string error) => new(name, HubStatus.Unreachable, [], false, error);
}
