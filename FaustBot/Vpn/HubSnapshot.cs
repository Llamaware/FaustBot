namespace FaustBot.Vpn;

/// <summary>A user connected to a hub. Times are UTC.</summary>
public sealed record VpnSession(string Username, DateTime CreatedUtc, DateTime LastCommUtc);

/// <summary>The state of one hub at the time it was queried.</summary>
/// <param name="Sessions">Connected users, one entry per username, excluding the configured IgnoreList.</param>
/// <param name="HasTerminal">Whether the configured TerminalName is connected (it may be in the IgnoreList).</param>
public sealed record HubSnapshot(string Name, bool Online, IReadOnlyList<VpnSession> Sessions, bool HasTerminal);
