using System.Text;
using Discord;
using Discord.Interactions;
using FaustBot.Options;
using FaustBot.Services;
using Microsoft.Extensions.Options;

namespace FaustBot.Modules;

[Group("admin", "Manage who can control the bot.")]
public sealed class AdminCommands(AdminService admins, IOptionsMonitor<BotOptions> options) : InteractionModuleBase<SocketInteractionContext>
{
    [RequireBotAdmin(ownerOnly: true)]
    [SlashCommand("add", "Let a user control the bot.")]
    public async Task Add([Summary(description: "The user to make a bot admin")] IUser user)
    {
        if (user.IsBot)
        {
            await RespondAsync("Bots can't be admins.", ephemeral: true);
            return;
        }

        var added = await admins.AddAsync(user.Id);
        await RespondAsync(added ? $"{user.Mention} is now a bot admin." : $"{user.Mention} is already a bot admin.",
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [RequireBotAdmin(ownerOnly: true)]
    [SlashCommand("remove", "Stop a user from controlling the bot.")]
    public async Task Remove([Summary(description: "The bot admin to remove")] IUser user)
    {
        var message = await admins.RemoveAsync(user.Id) switch
        {
            AdminRemoveResult.Removed => $"{user.Mention} is no longer a bot admin.",
            AdminRemoveResult.InConfig => $"{user.Mention} is listed in AdminUserIds in config.json, so they can only be removed there.",
            _ => $"{user.Mention} isn't a bot admin added with `/admin add`.",
        };
        await RespondAsync(message, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [RequireBotAdmin]
    [SlashCommand("list", "Show who can control the bot.")]
    public async Task List()
    {
        var config = options.CurrentValue;
        var text = new StringBuilder();
        AppendSection(text, "Owner", (await admins.GetOwnerIdsAsync()).Select(MentionUtils.MentionUser));
        AppendSection(text, "Admins (config.json)", config.AdminUserIds.Select(MentionUtils.MentionUser));
        AppendSection(text, "Admin roles (config.json)", config.AdminRoleIds.Select(MentionUtils.MentionRole));
        AppendSection(text, "Admins (added with /admin add)", (await admins.GetRuntimeAdminIdsAsync()).Select(MentionUtils.MentionUser));

        // Mentions render as names without pinging anyone.
        await RespondAsync(text.ToString(), ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private static void AppendSection(StringBuilder text, string heading, IEnumerable<string> entries)
    {
        var list = entries.ToList();
        text.Append("**").Append(heading).Append("**\n")
            .Append(list.Count == 0 ? "None" : string.Join(", ", list))
            .Append("\n\n");
    }
}
