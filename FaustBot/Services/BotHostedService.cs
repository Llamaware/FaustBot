using System.Net;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using FaustBot.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FaustBot.Services;

/// <summary>Owns the Discord connection: login, command registration and shutdown.</summary>
public sealed class BotHostedService(
    DiscordSocketClient client,
    InteractionService interactions,
    CommandHandler commandHandler,
    IOptions<BotOptions> options,
    IHostApplicationLifetime lifetime,
    ILoggerFactory loggerFactory,
    ILogger<BotHostedService> logger) : IHostedService
{
    private readonly ILogger _discordLogger = loggerFactory.CreateLogger("Discord");
    private bool _commandsRegistered;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += LogDiscordAsync;
        interactions.Log += LogDiscordAsync;
        client.Ready += OnReadyAsync;
        client.Disconnected += OnDisconnectedAsync;

        // Modules must be loaded before Ready fires, or there is nothing to register.
        await commandHandler.InitializeAsync();

        await client.LoginAsync(TokenType.Bot, options.Value.Token);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Bot is shutting down.");
        await client.StopAsync();
        await client.LogoutAsync();
    }

    private async Task OnReadyAsync()
    {
        logger.LogInformation("Connected as {Username}.", client.CurrentUser.Username);

        // Ready fires again after every reconnect; commands only need registering once per run.
        if (_commandsRegistered)
        {
            return;
        }

        // Commands are registered to the configured guild only, never globally, because the same token
        // is shared with other programs that each own their own guild.
        var guildId = options.Value.GuildId;
        await interactions.RegisterCommandsToGuildAsync(guildId, deleteMissing: true);
        _commandsRegistered = true;
        logger.LogInformation("Registered slash commands to guild {GuildId}.", guildId);
    }

    private Task OnDisconnectedAsync(Exception exception)
    {
        // Discord.Net retries a rejected token forever; stop instead so the bad config is visible.
        if (exception is Discord.Net.HttpException { HttpCode: HttpStatusCode.Unauthorized })
        {
            logger.LogCritical("Discord rejected the bot token. Check Token in config.json.");
            Environment.ExitCode = ExitCodes.ConfigError;
            lifetime.StopApplication();
        }
        return Task.CompletedTask;
    }

    private Task LogDiscordAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
        _discordLogger.Log(level, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
