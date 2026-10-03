using Discord.Interactions;
using Microsoft.Extensions.Hosting;

namespace FaustBot.Modules;

public sealed class Admin(IHostApplicationLifetime lifetime) : InteractionModuleBase<SocketInteractionContext>
{
    [RequireOwner]
    [SlashCommand("shutdown", "Shut down the bot.")]
    public async Task Shutdown()
    {
        await RespondAsync("The system will shut down now.");
        lifetime.StopApplication();
    }

    [SlashCommand("ping", "Ping the bot.")]
    public async Task Ping()
    {
        await RespondAsync("Pong!");
    }
}
