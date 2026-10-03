using Discord;
using Discord.WebSocket;
using FaustBot.Options;
using FaustBot.State;
using Microsoft.Extensions.Options;

namespace FaustBot.Services;

public enum AdminRemoveResult
{
    Removed,
    NotAnAdmin,

    /// <summary>The user is listed in config.json, which can't be changed from Discord.</summary>
    InConfig,
}

/// <summary>Decides who can control the bot: the owner, config.json admins and roles, and admins added at runtime.</summary>
public sealed class AdminService(DiscordSocketClient client, StateStore state, IOptionsMonitor<BotOptions> options)
{
    private BotOptions Settings => options.CurrentValue;

    /// <summary>The application owner, or every member of the team that owns it.</summary>
    public async Task<IReadOnlyList<ulong>> GetOwnerIdsAsync()
    {
        // Discord.Net caches the application info after the first request.
        var application = await client.GetApplicationInfoAsync();
        return application.Team is { } team
            ? team.TeamMembers.Select(m => m.User.Id).ToList()
            : [application.Owner.Id];
    }

    public async Task<bool> IsOwnerAsync(IUser user) => (await GetOwnerIdsAsync()).Contains(user.Id);

    public async Task<bool> IsAdminAsync(IUser user)
    {
        if (Settings.AdminUserIds.Contains(user.Id)
            || (user is IGuildUser member && member.RoleIds.Any(Settings.AdminRoleIds.Contains))
            || await state.ReadAsync(s => s.AdminUserIds.Contains(user.Id)))
        {
            return true;
        }

        return await IsOwnerAsync(user);
    }

    public Task<IReadOnlyList<ulong>> GetRuntimeAdminIdsAsync() =>
        state.ReadAsync<IReadOnlyList<ulong>>(s => s.AdminUserIds.ToList());

    /// <summary>Returns false if the user was already an admin by user ID.</summary>
    public async Task<bool> AddAsync(ulong userId)
    {
        if (Settings.AdminUserIds.Contains(userId) || await state.ReadAsync(s => s.AdminUserIds.Contains(userId)))
        {
            return false;
        }

        await state.UpdateAsync(s => s.AdminUserIds.Add(userId));
        return true;
    }

    public async Task<AdminRemoveResult> RemoveAsync(ulong userId)
    {
        if (await state.ReadAsync(s => s.AdminUserIds.Contains(userId)))
        {
            await state.UpdateAsync(s => s.AdminUserIds.Remove(userId));
            return AdminRemoveResult.Removed;
        }

        return Settings.AdminUserIds.Contains(userId) ? AdminRemoveResult.InConfig : AdminRemoveResult.NotAnAdmin;
    }
}
