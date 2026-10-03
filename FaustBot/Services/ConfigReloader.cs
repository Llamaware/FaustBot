using System.Text.Json;
using System.Text.Json.Nodes;
using FaustBot.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FaustBot.Services;

/// <param name="Errors">Why the new config was rejected. Empty if it was applied.</param>
/// <param name="Changed">Names of the settings that differ from the running config.</param>
/// <param name="RestartRequired">Settings that changed but only take effect after a restart.</param>
public sealed record ReloadResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Changed, IReadOnlyList<string> RestartRequired)
{
    public static ReloadResult Failed(IReadOnlyList<string> errors) => new(errors, [], []);
}

/// <summary>Re-reads config.json at runtime. The new file is validated first, so a bad edit is reported
/// instead of being applied.</summary>
public sealed class ConfigReloader(
    IConfiguration configuration,
    IOptions<BotOptions> startupOptions,
    IOptionsMonitor<BotOptions> currentOptions,
    IEnumerable<IPostConfigureOptions<BotOptions>> postConfigures,
    IEnumerable<IValidateOptions<BotOptions>> validators)
{
    public ReloadResult Reload()
    {
        BotOptions candidate;
        try
        {
            candidate = LoadCandidate();
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or FormatException or InvalidOperationException)
        {
            // Missing file, malformed JSON, or a value that can't be converted (e.g. a non-numeric GuildId).
            return ReloadResult.Failed([ex.Message]);
        }

        var errors = validators
            .Select(v => v.Validate(Microsoft.Extensions.Options.Options.DefaultName, candidate))
            .Where(r => r.Failed)
            .SelectMany(r => r.Failures ?? [])
            .ToList();
        if (errors.Count > 0)
        {
            return ReloadResult.Failed(errors);
        }

        var changed = GetChangedSettings(currentOptions.CurrentValue, candidate);
        if (changed.Count == 0)
        {
            return new ReloadResult([], [], []);
        }

        // Token and GuildId are only read at startup (login and command registration).
        var startup = startupOptions.Value;
        List<string> restartRequired = [];
        if (candidate.Token != startup.Token)
        {
            restartRequired.Add(nameof(BotOptions.Token));
        }
        if (candidate.GuildId != startup.GuildId)
        {
            restartRequired.Add(nameof(BotOptions.GuildId));
        }

        // Reloading the host's configuration makes IOptionsMonitor<BotOptions> pick up the new values.
        ((IConfigurationRoot)configuration).Reload();
        return new ReloadResult([], changed, restartRequired);
    }

    /// <summary>Compares settings by their JSON form, which also covers lists like Hubs.</summary>
    private static List<string> GetChangedSettings(BotOptions current, BotOptions candidate)
    {
        var before = JsonSerializer.SerializeToNode(current)!.AsObject();
        var after = JsonSerializer.SerializeToNode(candidate)!.AsObject();

        return after
            .Where(p => p.Key != nameof(BotOptions.UsesLegacyHubFormat)
                && !JsonNode.DeepEquals(p.Value, before[p.Key]))
            .Select(p => p.Key)
            .ToList();
    }

    private BotOptions LoadCandidate()
    {
        // Same sources as Program.cs: config.json overridden by FAUSTBOT_* environment variables.
        var candidateConfig = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(BotOptions.FileName, optional: false, reloadOnChange: false)
            .AddEnvironmentVariables("FAUSTBOT_")
            .Build();

        var candidate = new BotOptions();
        candidateConfig.Bind(candidate);
        foreach (var postConfigure in postConfigures)
        {
            postConfigure.PostConfigure(Microsoft.Extensions.Options.Options.DefaultName, candidate);
        }
        return candidate;
    }
}
