# FaustBot

A Discord bot for monitoring SoftEther VPN hubs.

## Commands

`/ping` - Pings the bot.

`/shutdown` - Shut down the bot. (Owner only)

`/status [hubName]` - Print status of VPN hub.

`/list [hubName]` - List all sessions on VPN hub.

`/start` - Start VPN monitoring service. (Owner only)

`/stop` - Stop VPN monitoring service. (Owner only)

## Config

In `config.json`:

```
{
    "Token": "PutYourBotTokenHere",
    "GuildId": "1234567890",
    "LogChannelId": "1234567890",
    "EmbedChannelId": "1234567890",
    "EnableLogs": "false",
    "UpdateDelay": "60",
    "VpnServerIp": "123.456.789.10",
    "VpnServerPassword": "PasswordHere",
    "VirtualHubMode": "false",
    "VpnHubList": [
        "TEST-A",
        "TEST-B",
        "TEST-C"
    ],
    "VpnHubPasswords": [
        "HubPassword-A",
        "HubPassword-B",
        "HubPassword-C"
    ],
    "IgnoreList": [
        "Local Bridge",
        "SecureNAT",
        "MaxiTerm"
    ],
    "TerminalName": "MaxiTerm",
    "TimeZone": "Pacific Standard Time",
    "DisplaySessionTime": "true",
    "TitleText": "My VPN Network Status\nVPN Location",
    "FooterText": "VPN Bot will auto-update this message every minute",
    "MentionUserIds": "false",
    "CustomEmojis": "false",
    "HubOnlineEmoji": "<:emoji_ok:1234567890>",
    "HubOfflineEmoji": "<:emoji_ng:1234567890>"
}
```

`Token` - Your bot token.

`GuildId` - Your server ID. (Get it by using Developer Mode)

`LogChannelId` - Channel ID to send the logs to.

`EmbedChannelId` - Channel ID to send the persistent embed to.

`EnableLogs` - Print logs every time a user joins or leaves a hub.

`UpdateDelay` - How many seconds to wait before updating the embed.

`VpnServerIp` - SoftEther VPN server IP.

`VpnServerPassword` - The password for your SoftEther VPN server. (Only if not using `VirtualHubMode`)

`VirtualHubMode` - Whether to use Virtual Hub Administrator mode. (Need to set `VpnHubPasswords`)

`VpnHubList` - List of hub names to monitor.

`VpnHubPasswords` - List of hub passwords. (Only if using `VirtualHubMode`)

`IgnoreList` - List of usernames to ignore. (Not counted as online users)

`TerminalName` - Displays `DT` next to the hub name if this user is found on the hub.

`TimeZone` - Time zone to print logs with.

`DisplaySessionTime` - Whether to display the session time next to the username in the embed.

`TitleText` - The persistent embed's title text.

`FooterText` - The persistent embed's footer text.

`MentionUserIds` - Enable this only if the usernames on your SoftEther server are Discord User IDs.

`CustomEmojis` - Whether to use custom emojis to display the hub online/offline status.

`HubOnlineEmoji` - Emoji to use for an online hub. (If `CustomEmojis` is enabled)

`HubOfflineEmoji` - Emoji to use for an offline hub. (If `CustomEmojis` is enabled)
