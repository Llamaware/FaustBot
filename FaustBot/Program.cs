using System.Reflection;
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

var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

// Used by update.sh to compare the installed version with the latest release.
if (args is ["--version"])
{
    Console.WriteLine(version);
    return 0;
}

var configPath = Path.Combine(AppContext.BaseDirectory, BotOptions.FileName);
if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"{BotOptions.FileName} not found at {configPath}. Copy config.example.json to {BotOptions.FileName} and fill it in.");
    return ExitCodes.ConfigError;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Configuration
    .AddJsonFile(BotOptions.FileName, optional: false, reloadOnChange: false)
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
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<ConfigReloader>();
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
        logger.LogCritical("Invalid {ConfigFile}: {Failure}", BotOptions.FileName, failure);
    }
    return ExitCodes.ConfigError;
}
catch (InvalidOperationException ex)
{
    // Thrown by the configuration binder when a value can't be converted, e.g. a non-numeric GuildId.
    logger.LogCritical("Invalid {ConfigFile}: {Error}", BotOptions.FileName, ex.Message);
    return ExitCodes.ConfigError;
}

logger.LogInformation("FaustBot {Version}", version);

if (options.UsesLegacyHubFormat)
{
    logger.LogWarning("VpnHubList/VpnHubPasswords are deprecated. Move your hubs to the Hubs list (see config.example.json).");
}

await host.RunAsync();
return Environment.ExitCode;
