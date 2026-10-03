using System.Text;
using Discord;
using Discord.Interactions;
using FaustBot.Options;
using FaustBot.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Logging;

namespace FaustBot.Modules;

[Group("bot", "Bot controls.")]
public sealed class BotCommands(IHostApplicationLifetime lifetime, ConfigReloader reloader, ILogger<BotCommands> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("ping", "Check that the bot is responding.")]
    public async Task Ping()
    {
        await RespondAsync($"Pong! Gateway latency: {Context.Client.Latency} ms.", ephemeral: true);
    }

    [RequireBotAdmin]
    [SlashCommand("reload", "Re-read config.json without restarting.")]
    public async Task Reload()
    {
        var result = reloader.Reload();
        var reply = new StringBuilder();

        if (result.Errors.Count > 0)
        {
            reply.Append("config.json was not reloaded:\n");
            foreach (var error in result.Errors)
            {
                reply.Append("- ").Append(error).Append('\n');
            }
        }
        else if (result.Changed.Count == 0)
        {
            reply.Append($"No changes found in {Path.Combine(AppContext.BaseDirectory, BotOptions.FileName)}.");
        }
        else
        {
            reply.Append($"config.json reloaded. Changed: {string.Join(", ", result.Changed)}.");
            if (result.RestartRequired.Count > 0)
            {
                reply.Append($" {string.Join(" and ", result.RestartRequired)} changed, which needs `/bot restart` to take effect.");
            }
        }

        var text = reply.ToString();
        if (text.Length > DiscordConfig.MaxMessageSize)
        {
            text = text[..(DiscordConfig.MaxMessageSize - 1)] + "…";
        }

        await RespondAsync(text, ephemeral: true);
    }

    [RequireBotAdmin]
    [SlashCommand("restart", "Restart the bot.")]
    public async Task Restart()
    {
        // Only systemd brings the process back: the unit restarts it when it exits with this code.
        if (!SystemdHelpers.IsSystemdService())
        {
            await RespondAsync("The bot isn't running as a systemd service, so it can't restart itself.", ephemeral: true);
            return;
        }

        logger.LogInformation("Restart requested by {User} ({UserId}).", Context.User.Username, Context.User.Id);
        await RespondAsync("Restarting...");
        Environment.ExitCode = ExitCodes.Restart;
        lifetime.StopApplication();
    }

    [RequireBotAdmin]
    [SlashCommand("shutdown", "Shut down the bot. It stays off until started again on the server.")]
    public async Task Shutdown()
    {
        logger.LogInformation("Shutdown requested by {User} ({UserId}).", Context.User.Username, Context.User.Id);
        await RespondAsync("Shutting down.");
        lifetime.StopApplication();
    }
}
