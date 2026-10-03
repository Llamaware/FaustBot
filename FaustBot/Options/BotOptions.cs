namespace FaustBot.Options;

/// <summary>Settings bound from config.json (and FAUSTBOT_* environment variables).</summary>
public sealed class BotOptions
{
    public string Token { get; set; } = "";
    public ulong GuildId { get; set; }
    public ulong LogChannelId { get; set; }
    public ulong EmbedChannelId { get; set; }
    public bool EnableLogs { get; set; }

    /// <summary>Seconds between embed updates.</summary>
    public int UpdateDelay { get; set; } = 60;

    public string VpnServerIp { get; set; } = "";
    public int VpnServerPort { get; set; } = 443;

    /// <summary>Server admin password. Only used when <see cref="VirtualHubMode"/> is off.</summary>
    public string VpnServerPassword { get; set; } = "";

    /// <summary>Connect with per-hub admin passwords instead of the server admin password.</summary>
    public bool VirtualHubMode { get; set; }

    public List<HubOptions> Hubs { get; set; } = [];

    /// <summary>Legacy hub names, superseded by <see cref="Hubs"/>.</summary>
    public List<string> VpnHubList { get; set; } = [];

    /// <summary>Legacy hub passwords, index-matched to <see cref="VpnHubList"/>.</summary>
    public List<string> VpnHubPasswords { get; set; } = [];

    public List<string> IgnoreList { get; set; } = [];
    public string TerminalName { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";
    public bool DisplaySessionTime { get; set; } = true;

    /// <summary>Shown as "n/MaxPlayersPerHub Players" on each hub. 0 shows just the count.</summary>
    public int MaxPlayersPerHub { get; set; } = 4;

    public string TitleText { get; set; } = "VPN Status";
    public string FooterText { get; set; } = "";
    public bool MentionUserIds { get; set; }
    public bool CustomEmojis { get; set; }
    public string HubOnlineEmoji { get; set; } = "";
    public string HubOfflineEmoji { get; set; } = "";

    /// <summary>Set when <see cref="Hubs"/> was built from <see cref="VpnHubList"/>.</summary>
    public bool UsesLegacyHubFormat { get; set; }
}

public sealed class HubOptions
{
    public string Name { get; set; } = "";

    /// <summary>Hub admin password. Only required when <see cref="BotOptions.VirtualHubMode"/> is on.</summary>
    public string? Password { get; set; }
}
