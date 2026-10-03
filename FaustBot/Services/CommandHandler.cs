using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using FaustBot.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FaustBot.Services;

/// <summary>Routes incoming interactions to the command modules and reports failed commands to the user.</summary>
public sealed class CommandHandler(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    IOptions<BotOptions> options,
    ILogger<CommandHandler> logger)
{
    public async Task InitializeAsync()
    {
        await interactions.AddModulesAsync(typeof(CommandHandler).Assembly, services);

        client.InteractionCreated += HandleInteractionAsync;
        interactions.InteractionExecuted += OnInteractionExecutedAsync;
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        // The token is shared with programs that serve other guilds. Their interactions reach this
        // gateway connection too, and must be left alone so the program that owns them can reply.
        if (interaction.GuildId != options.Value.GuildId)
        {
            return;
        }

        var context = new SocketInteractionContext(client, interaction);
        await interactions.ExecuteCommandAsync(context, services);
    }

    private async Task OnInteractionExecutedAsync(ICommandInfo command, IInteractionContext context, IResult result)
    {
        // UnknownCommand: not one of ours, so stay silent. Autocomplete requests can't be answered with a message.
        if (result.IsSuccess
            || result.Error == InteractionCommandError.UnknownCommand
            || context.Interaction is IAutocompleteInteraction)
        {
            return;
        }

        // Exceptions are already logged by InteractionService through its Log event.
        if (result.Error != InteractionCommandError.Exception)
        {
            logger.LogWarning("Command {Command} failed for {User}: {Error} {Reason}",
                command?.Name, context.User.Username, result.Error, result.ErrorReason);
        }

        var message = result.Error switch
        {
            InteractionCommandError.UnmetPrecondition => result.ErrorReason,
            InteractionCommandError.BadArgs or InteractionCommandError.ConvertFailed or InteractionCommandError.ParseFailed
                => "Invalid command arguments.",
            _ => "Something went wrong running that command. Check the bot logs for details.",
        };

        try
        {
            if (context.Interaction.HasResponded)
            {
                await context.Interaction.FollowupAsync(message, ephemeral: true);
            }
            else
            {
                await context.Interaction.RespondAsync(message, ephemeral: true);
            }
        }
        catch (Exception ex) when (ex is Discord.Net.HttpException or TimeoutException)
        {
            // The interaction may have expired (3s to respond, 15 min for follow-ups).
            logger.LogWarning(ex, "Could not send the error message for {Command}.", command?.Name);
        }
    }
}
