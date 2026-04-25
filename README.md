# VRChatBioUpdater

Automatically updates your VRChat bio with dynamic statistics like playtime, friend count, and more.

## Installation
1. Download `VRChatBioUpdater.exe`.
2. Launch once to generate `VRChatBioUpdater.json`.
3. Edit `VRChatBioUpdater.json` with your VRChat credentials, Steam API key (optional), and your bio template.
4. Launch and let it run in the background.

## Configuration
Example `VRChatBioUpdater.json`:
```json
{
  "WaitOnExit": false,
  "FetchDetails": true,
  "Username": "your_username",
  "Password": "your_password",
  "TOTPSecret": "your_totp_secret",
  "UpdateInterval": 7200000,
  "InitialDelay": 20000,
  "SteamId": "your_steam_id64",
  "SteamApiKey": "your_steam_api_key",
  "BioTemplate": "Friends: {friends} | Playtime: {playtime}\nLast updated: {now}",
  "Separator": "\n-\n"
}
```

## Placeholders
- `{playtime}`: Total VRChat playtime (from Steam or VRChat)
- `{vrcx_playtime}`: Total VRChat playtime recorded in VRCX
- `{friends}`: Total friend count
- `{blocked}`: Number of blocked users
- `{muted}`: Number of muted users
- `{memos}`: Number of user memos in VRCX
- `{notes}`: Number of notes in VRCX
- `{tags_loaded}`: Total number of tags in VRCX
- `{tagged_users}`: Number of unique users tagged in VRCX
- `{rank}`: Your VRChat trust rank
- `{now}`: Current timestamp
- `{user_id}`: Your VRChat User ID
- `{group1}`, `{group2}`, `{group3}`: Display names of your favorited users in those groups
- `{last_activity}`: Time since your last activity
