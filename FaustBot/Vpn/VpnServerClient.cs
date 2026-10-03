using FaustBot.Options;
using Microsoft.Extensions.Options;
using SoftEther.VPNServerRpc;

namespace FaustBot.Vpn;

/// <summary>Queries the SoftEther server. RPC clients are created once and reused for every request.</summary>
public sealed class VpnServerClient
{
    private readonly BotOptions _options;
    private readonly HashSet<string> _ignoreList;

    // Server admin mode shares one client; virtual hub mode needs one per hub, since each has its own password.
    private readonly VpnServerRpc? _serverRpc;
    private readonly Dictionary<string, VpnServerRpc> _hubRpcs = new(StringComparer.OrdinalIgnoreCase);

    public VpnServerClient(IOptions<BotOptions> options)
    {
        _options = options.Value;
        _ignoreList = new HashSet<string>(_options.IgnoreList, StringComparer.OrdinalIgnoreCase);

        if (_options.VirtualHubMode)
        {
            foreach (var hub in _options.Hubs)
            {
                _hubRpcs[hub.Name] = new VpnServerRpc(_options.VpnServerIp, _options.VpnServerPort, hub.Password, hub.Name);
            }
        }
        else
        {
            _serverRpc = new VpnServerRpc(_options.VpnServerIp, _options.VpnServerPort, _options.VpnServerPassword, "");
        }
    }

    public IReadOnlyList<HubOptions> Hubs => _options.Hubs;

    public HubOptions? FindHub(string name) =>
        _options.Hubs.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<HubSnapshot>> QueryAllHubsAsync(CancellationToken cancellationToken)
    {
        return await Task.WhenAll(_options.Hubs.Select(hub => QueryHubAsync(hub, cancellationToken)));
    }

    /// <summary>Queries one hub. Connection and RPC errors are returned as <see cref="HubStatus.Unreachable"/>.</summary>
    public async Task<HubSnapshot> QueryHubAsync(HubOptions hub, CancellationToken cancellationToken)
    {
        try
        {
            return await QueryHubCoreAsync(hub, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The generated RPC client throws plain Exceptions for HTTP errors, so there's no narrower type to catch.
            return HubSnapshot.Unreachable(hub.Name, ex.Message);
        }
    }

    private async Task<HubSnapshot> QueryHubCoreAsync(HubOptions hub, CancellationToken cancellationToken)
    {
        var rpc = _serverRpc ?? _hubRpcs[hub.Name];

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
            .Where(s => !string.IsNullOrEmpty(s.Username_str) && !_ignoreList.Contains(s.Username_str))
            .GroupBy(s => s.Username_str, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MinBy(s => s.CreatedTime_dt)!)
            .Select(s => new VpnSession(s.Username_str, AsUtc(s.CreatedTime_dt), AsUtc(s.LastCommTime_dt)))
            .ToList();

        var hasTerminal = !string.IsNullOrEmpty(_options.TerminalName)
            && allSessions.Any(s => string.Equals(s.Username_str, _options.TerminalName, StringComparison.OrdinalIgnoreCase));

        return new HubSnapshot(hub.Name, HubStatus.Online, sessions, hasTerminal);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
