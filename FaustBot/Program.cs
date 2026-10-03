using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using FaustBot;
using FaustBot.Options;
using FaustBot.Services;
using FaustBot.State;
using FaustBot.Vpn;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

const string ConfigFileName = "config.json";

var configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"{ConfigFileName} not found at {configPath}. Copy config.example.json to {ConfigFileName} and fill it in.");
    return ExitCodes.ConfigError;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Configuration
    .AddJsonFile(ConfigFileName, optional: false, reloadOnChange: false)
    .AddEnvironmentVariables("FAUSTBOT_");

// Uses journald log formatting and sd_notify readiness when running under systemd; no-op otherwise.
builder.Services.AddSystemd();

builder.Services.AddOptions<BotOptions>().Bind(builder.Configuration);
builder.Services.AddSingleton<IPostConfigureOptions<BotOptions>, BotOptionsSetup>();
builder.Services.AddSingleton<IValidateOptions<BotOptions>, BotOptionsValidator>();

builder.Services.AddSingleton(new DiscordSocketConfig
{
    // Slash commands only need the Guilds intent.
    GatewayIntents = GatewayIntents.Guilds,
});
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>()));
builder.Services.AddSingleton<CommandHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<VpnServerClient>();
builder.Services.AddSingleton<StatusEmbedBuilder>();
builder.Services.AddSingleton<VpnMonitorService>();

// Hosted services stop in reverse order, so the monitor stops before the Discord client disconnects.
builder.Services.AddHostedService<BotHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<VpnMonitorService>());

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

BotOptions options;
try
{
    options = host.Services.GetRequiredService<IOptions<BotOptions>>().Value;
}
catch (OptionsValidationException ex)
{
    foreach (var failure in ex.Failures)
    {
        logger.LogCritical("Invalid {ConfigFile}: {Failure}", ConfigFileName, failure);
    }
    return ExitCodes.ConfigError;
}
catch (InvalidOperationException ex)
{
    // Thrown by the configuration binder when a value can't be converted, e.g. a non-numeric GuildId.
    logger.LogCritical("Invalid {ConfigFile}: {Error}", ConfigFileName, ex.Message);
    return ExitCodes.ConfigError;
}

if (options.UsesLegacyHubFormat)
{
    logger.LogWarning("VpnHubList/VpnHubPasswords are deprecated. Move your hubs to the Hubs list (see config.example.json).");
}

await host.RunAsync();
return Environment.ExitCode;
