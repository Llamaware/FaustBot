using System.Globalization;
using System.Text;
using Discord;
using FaustBot.Options;
using FaustBot.Vpn;
using Microsoft.Extensions.Options;

namespace FaustBot.Services;

/// <summary>Builds the persistent status embed, keeping within Discord's embed limits.</summary>
public sealed class StatusEmbedBuilder(IOptions<BotOptions> options)
{
    private const string TerminalBadge = " :regional_indicator_d::regional_indicator_t:";

    private readonly BotOptions _options = options.Value;

    public Embed Build(IReadOnlyList<HubSnapshot> hubs, DateTimeOffset now)
    {
        var embed = new EmbedBuilder()
            .WithTitle(Truncate(_options.TitleText, EmbedBuilder.MaxTitleLength))
            .WithColor(Color.Green)
            .WithTimestamp(now);

        if (!string.IsNullOrWhiteSpace(_options.FooterText))
        {
            embed.WithFooter(Truncate(_options.FooterText, EmbedFooterBuilder.MaxFooterTextLength));
        }

        var fields = hubs.Take(EmbedBuilder.MaxFieldCount)
            .Select(hub => (Hub: hub, Name: Truncate(BuildFieldName(hub), EmbedFieldBuilder.MaxFieldNameLength)))
            .ToList();

        // Discord also caps the whole embed at 6000 characters, so share what's left between the field values.
        var fixedLength = embed.Length + fields.Sum(f => f.Name.Length);
        var valueBudget = fields.Count == 0
            ? 0
            : Math.Min(EmbedFieldBuilder.MaxFieldValueLength, (EmbedBuilder.MaxEmbedLength - fixedLength) / fields.Count);

        foreach (var (hub, name) in fields)
        {
            embed.AddField(name, BuildFieldValue(hub, now, valueBudget));
        }

        return embed.Build();
    }

    private string BuildFieldName(HubSnapshot hub)
    {
        var status = _options.CustomEmojis
            ? (hub.Online ? _options.HubOnlineEmoji : _options.HubOfflineEmoji)
            : (hub.Online ? "[Online]" : "[Offline]");

        var capacity = _options.MaxPlayersPerHub > 0 ? $"/{_options.MaxPlayersPerHub}" : "";
        var name = $"{status} {hub.Name}: {hub.Sessions.Count}{capacity} Players";
        return hub.HasTerminal ? name + TerminalBadge : name;
    }

    private string BuildFieldValue(HubSnapshot hub, DateTimeOffset now, int maxLength)
    {
        if (!hub.Online)
        {
            return "Hub Offline";
        }

        if (hub.Sessions.Count == 0)
        {
            return "No Players";
        }

        var value = new StringBuilder();
        for (var i = 0; i < hub.Sessions.Count; i++)
        {
            var line = FormatSession(hub.Sessions[i], now);

            // Leave room for the "...and N more" line if the rest won't fit.
            var remaining = hub.Sessions.Count - i - 1;
            var reserve = remaining > 0 ? $"\n...and {remaining} more".Length : 0;
            if (value.Length + line.Length + 1 + reserve > maxLength)
            {
                value.Append($"...and {hub.Sessions.Count - i} more");
                break;
            }

            value.Append(line).Append('\n');
        }

        return value.ToString().TrimEnd('\n');
    }

    private string FormatSession(VpnSession session, DateTimeOffset now)
    {
        var name = _options.MentionUserIds ? $"<@{session.Username}>" : session.Username;
        if (!_options.DisplaySessionTime)
        {
            return name;
        }

        var duration = now.UtcDateTime - session.CreatedUtc;
        return string.Create(CultureInfo.InvariantCulture,
            $"{name} - {(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
