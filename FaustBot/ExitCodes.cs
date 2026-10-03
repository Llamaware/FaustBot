namespace FaustBot;

/// <summary>Process exit codes, matched by the systemd unit's restart rules.</summary>
public static class ExitCodes
{
    /// <summary>EX_CONFIG from sysexits.h. systemd does not restart on this, since retrying can't fix a bad config.</summary>
    public const int ConfigError = 78;
}
