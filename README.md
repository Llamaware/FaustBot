# FaustBot

A Discord bot for monitoring SoftEther VPN hubs.

## Commands

`/vpn status [hub]` - Print status of a VPN hub (all hubs if omitted).

`/vpn list <hub>` - List all sessions on a VPN hub.

`/vpn start` - Start VPN monitoring. (Admin)

`/vpn stop` - Pause VPN monitoring, including across restarts. (Admin)

`/bot ping` - Ping the bot.

`/bot reload` - Re-read `config.json` without restarting. (Owner)

`/bot restart` - Restart the bot. Requires systemd. (Admin)

`/bot shutdown` - Shut down the bot. (Admin)

`/admin add|remove <user>` - Add or remove a bot admin. (Owner)

`/admin list` - List bot admins. (Admin)

Admins are the bot owner, `AdminUserIds`, members with an `AdminRoleIds` role, and users added with `/admin add`.

Commands are registered to `GuildId` only. Interactions from other guilds are ignored, so the token can be shared with other bots that are each isolated to their own guild.

## Config

Copy `config.example.json` to `config.json` next to the binary. Runtime state (embed message, admins added with `/admin add`, paused state) is kept in `state.json`.

`Token` - Your bot token.

`GuildId` - Your server ID. (Get it by using Developer Mode)

`EmbedChannelId` - Channel ID for the persistent embed. (Needs Read Message History)

`LogChannelId` - Channel ID to send the logs to.

`EnableLogs` - Log when a user joins or leaves a hub.

`AutoStartMonitoring` - Start monitoring when the bot starts.

`UpdateDelay` - How many seconds to wait before updating the embed. (Minimum 10)

`AdminUserIds` - User IDs that can control the bot.

`AdminRoleIds` - Role IDs that can control the bot.

`VpnServerIp` - SoftEther VPN server IP.

`VpnServerPort` - SoftEther VPN server admin port.

`VpnServerPassword` - The password for your SoftEther VPN server. (Only if not using `VirtualHubMode`)

`VirtualHubMode` - Whether to use Virtual Hub Administrator mode. (Each hub needs a `Password`)

`Hubs` - List of hubs to monitor, as `{ "Name": "HUB", "Password": "..." }`. (`Password` only if using `VirtualHubMode`)

`IgnoreList` - List of usernames to ignore. (Not counted as online users)

`TerminalName` - Displays `DT` next to the hub name if this user is found on the hub.

`TimeZone` - Time zone to print logs with.

`DisplaySessionTime` - Whether to display the session time next to the username in the embed.

`MaxPlayersPerHub` - Shown as `n/MaxPlayersPerHub Players`. (0 to show only the count)

`TitleText` - The persistent embed's title text.

`FooterText` - The persistent embed's footer text.

`MentionUserIds` - Enable this only if the usernames on your SoftEther server are Discord User IDs.

`CustomEmojis` - Whether to use custom emojis to display the hub online/offline status.

`HubOnlineEmoji` - Emoji to use for an online hub. (If `CustomEmojis` is enabled)

`HubOfflineEmoji` - Emoji to use for an offline hub. (If `CustomEmojis` is enabled)

Any setting can be overridden with a `FAUSTBOT_` environment variable, e.g. `FAUSTBOT_Token`.

## Deployment

Releases are built by GitHub Actions when a `v*` tag is pushed (`v3.0.0-rc1` style tags are pre-releases):

```
git tag v3.0.0 && git push origin v3.0.0
```

Install on Linux, from the directory the bot should run in:

```
curl -fL https://github.com/Llamaware/FaustBot/releases/latest/download/faustbot-linux-x64.tar.gz | tar -xz
cp config.example.json config.json    # then fill it in
sudo ./install.sh                     # installs and starts the faustbot systemd service
```

Update with `./update.sh` (`--rollback` to undo, `--version <tag>` for a specific release). Logs: `journalctl -u faustbot -f`

Exit codes: `75` restart (`/bot restart`), `78` invalid config (not restarted by systemd).

### Upgrading from v2

Stop the service, keep the old binary as `FaustBot.old` (so `./update.sh --rollback` can return to it), then install as above. `VpnHubList`/`VpnHubPasswords` are replaced by `Hubs`. The existing embed message is reused.

## Build

```
dotnet publish FaustBot/FaustBot.csproj -p:PublishProfile=linux-x64
```
