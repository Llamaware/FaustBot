using Microsoft.Extensions.Options;

namespace FaustBot.Options;

public sealed class BotOptionsValidator : IValidateOptions<BotOptions>
{
    private const int MinUpdateDelay = 10;

    public ValidateOptionsResult Validate(string? name, BotOptions options)
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            errors.Add("Token is required.");
        }

        if (options.GuildId == 0)
        {
            errors.Add("GuildId is required.");
        }

        if (options.EmbedChannelId == 0)
        {
            errors.Add("EmbedChannelId is required.");
        }

        if (options.EnableLogs && options.LogChannelId == 0)
        {
            errors.Add("LogChannelId is required when EnableLogs is true.");
        }

        if (options.UpdateDelay < MinUpdateDelay)
        {
            errors.Add($"UpdateDelay must be at least {MinUpdateDelay} seconds.");
        }

        if (string.IsNullOrWhiteSpace(options.VpnServerIp))
        {
            errors.Add("VpnServerIp is required.");
        }

        if (options.VpnServerPort is < 1 or > 65535)
        {
            errors.Add("VpnServerPort must be between 1 and 65535.");
        }

        ValidateHubs(options, errors);

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out _))
        {
            errors.Add($"TimeZone '{options.TimeZone}' was not found on this system.");
        }

        if (options.CustomEmojis
            && (string.IsNullOrWhiteSpace(options.HubOnlineEmoji) || string.IsNullOrWhiteSpace(options.HubOfflineEmoji)))
        {
            errors.Add("HubOnlineEmoji and HubOfflineEmoji are required when CustomEmojis is true.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateHubs(BotOptions options, List<string> errors)
    {
        if (options.Hubs.Count == 0)
        {
            errors.Add("At least one hub must be listed in Hubs.");
            return;
        }

        foreach (var hub in options.Hubs)
        {
            if (string.IsNullOrWhiteSpace(hub.Name))
            {
                errors.Add("Every entry in Hubs needs a Name.");
            }
            else if (options.VirtualHubMode && string.IsNullOrEmpty(hub.Password))
            {
                errors.Add($"Hub '{hub.Name}' needs a Password because VirtualHubMode is true.");
            }
        }

        var duplicates = options.Hubs
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var duplicate in duplicates)
        {
            errors.Add($"Hub '{duplicate}' is listed more than once.");
        }
    }
}
