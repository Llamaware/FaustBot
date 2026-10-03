using FaustBot.Options;
using Microsoft.Extensions.Options;
using SoftEther.VPNServerRpc;

namespace FaustBot.Vpn;

/// <summary>Queries the SoftEther server. RPC clients are created once and reused until the config is reloaded.</summary>
public sealed class VpnServerClient : IDisposable
{
    private readonly IDisposable? _changeSubscription;

    // Replaced as a whole on reload, so a query always sees one consistent set of settings and clients.
    private volatile Connection _connection;

    public VpnServerClient(IOptionsMonitor<BotOptions> options)
    {
        _connection = new Connection(options.CurrentValue);
        _changeSubscription = options.OnChange(updated => _connection = new Connection(updated));
    }

    public IReadOnlyList<HubOptions> Hubs => _connection.Options.Hubs;

    public HubOptions? FindHub(string name) =>
        Hubs.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<HubSnapshot>> QueryAllHubsAsync(CancellationToken cancellationToken)
    {
        var connection = _connection;
        return await Task.WhenAll(connection.Options.Hubs.Select(hub => QueryHubAsync(connection, hub, cancellationToken)));
    }

    /// <summary>Queries one hub. Connection and RPC errors are returned as <see cref="HubStatus.Unreachable"/>.</summary>
    public Task<HubSnapshot> QueryHubAsync(HubOptions hub, CancellationToken cancellationToken) =>
        QueryHubAsync(_connection, hub, cancellationToken);

    public void Dispose() => _changeSubscription?.Dispose();

    private static async Task<HubSnapshot> QueryHubAsync(Connection connection, HubOptions hub, CancellationToken cancellationToken)
    {
        try
        {
            return await QueryHubCoreAsync(connection, hub, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The generated RPC client throws plain Exceptions for HTTP errors, so there's no narrower type to catch.
            return HubSnapshot.Unreachable(hub.Name, ex.Message);
        }
    }

    private static async Task<HubSnapshot> QueryHubCoreAsync(Connection connection, HubOptions hub, CancellationToken cancellationToken)
    {
        var rpc = connection.GetRpc(hub.Name);

        // The generated RPC stubs don't take a CancellationToken, so stop waiting instead.
        var status = await rpc.GetHubStatusAsync(new VpnRpcHubStatus { HubName_str = hub.Name })
            .WaitAsync(cancellationToken);
        if (!status.Online_bool)
        {
            return new HubSnapshot(hub.Name, HubStatus.Offline, [], HasTerminal: false);
        }

        var enumSession = await rpc.EnumSessionAsync(new VpnRpcEnumSession { HubName_str = hub.Name })
            .WaitAsync(cancellationToken);
        var allSessions = enumSession.SessionList ?? [];

        // A user can hold several sessions; keep their oldest one.
        var sessions = allSessions
            .Where(s => !string.IsNullOrEmpty(s.Username_str) && !connection.IgnoreList.Contains(s.Username_str))
            .GroupBy(s => s.Username_str, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MinBy(s => s.CreatedTime_dt)!)
            .Select(s => new VpnSession(s.Username_str, AsUtc(s.CreatedTime_dt), AsUtc(s.LastCommTime_dt)))
            .ToList();

        var terminalName = connection.Options.TerminalName;
        var hasTerminal = !string.IsNullOrEmpty(terminalName)
            && allSessions.Any(s => string.Equals(s.Username_str, terminalName, StringComparison.OrdinalIgnoreCase));

        return new HubSnapshot(hub.Name, HubStatus.Online, sessions, hasTerminal);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private sealed class Connection
    {
        // Server admin mode shares one client; virtual hub mode needs one per hub, since each has its own password.
        private readonly VpnServerRpc? _serverRpc;
        private readonly Dictionary<string, VpnServerRpc> _hubRpcs = new(StringComparer.OrdinalIgnoreCase);

        public Connection(BotOptions options)
        {
            Options = options;
            IgnoreList = new HashSet<string>(options.IgnoreList, StringComparer.OrdinalIgnoreCase);

            if (options.VirtualHubMode)
            {
                foreach (var hub in options.Hubs)
                {
                    _hubRpcs[hub.Name] = new VpnServerRpc(options.VpnServerIp, options.VpnServerPort, hub.Password, hub.Name);
                }
            }
            else
            {
                _serverRpc = new VpnServerRpc(options.VpnServerIp, options.VpnServerPort, options.VpnServerPassword, "");
            }
        }

        public BotOptions Options { get; }
        public HashSet<string> IgnoreList { get; }

        public VpnServerRpc GetRpc(string hubName) => _serverRpc ?? _hubRpcs[hubName];
    }
}
