using Microsoft.Extensions.Options;

namespace FaustBot.Options;

/// <summary>Converts the legacy VpnHubList/VpnHubPasswords arrays into <see cref="BotOptions.Hubs"/>.</summary>
public sealed class BotOptionsSetup : IPostConfigureOptions<BotOptions>
{
    public void PostConfigure(string? name, BotOptions options)
    {
        if (options.Hubs.Count > 0 || options.VpnHubList.Count == 0)
        {
            return;
        }

        options.Hubs = options.VpnHubList
            .Select((hubName, i) => new HubOptions
            {
                Name = hubName,
                Password = i < options.VpnHubPasswords.Count ? options.VpnHubPasswords[i] : null,
            })
            .ToList();
        options.UsesLegacyHubFormat = true;
    }
}
