using Discord;
using Discord.Interactions;
using FaustBot.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FaustBot.Modules;

/// <summary>Limits a command to bot admins (see <see cref="AdminService"/>), or to the bot owner only.</summary>
public sealed class RequireBotAdminAttribute(bool ownerOnly = false) : PreconditionAttribute
{
    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
    {
        var admins = services.GetRequiredService<AdminService>();

        if (ownerOnly)
        {
            return await admins.IsOwnerAsync(context.User)
                ? PreconditionResult.FromSuccess()
                : PreconditionResult.FromError("Only the bot owner can use this command.");
        }

        return await admins.IsAdminAsync(context.User)
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError("You need to be a bot admin to use this command.");
    }
}
