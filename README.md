# VRChatBioUpdater

Automatically updates your VRChat bio with dynamic statistics like playtime, friend count, trust rank, and custom conditional variables.

## Features
- **Dynamic Stats**: Show your friends, blocks, mutes, and trust rank.
- **Playtime Tracking**: Integration with Steam and local VRCX database.
- **Custom Variables**: Define your own variables with logic (e.g. "Show my relationship status only if I have friends in Group 2").
- **VRCX Integration**: Fetch memos, notes, and counts of favorited users.
- **Single File**: Portable executable with no external dependencies required.
- **Headless Mode**: Use `--once` for one-off updates in scheduled tasks.

## Installation
1. Download `VRChatBioUpdater.exe` from the [Releases](https://github.com/Bluscream/VRChatBioUpdater/releases) page.
2. Launch once to generate `VRChatBioUpdater.json`.
3. Edit `VRChatBioUpdater.json` with your credentials and template.
4. Launch and let it run, or schedule it with `--once`.

## Configuration
See [VRChatBioUpdater.json.example](VRChatBioUpdater.json.example) for a full template.

### Custom Variables
You can define variables that only appear in your bio when a certain condition is met:
```json
"CustomVariables": {
  "relationship": {
    "Content": "Relationship: {group2} <3\n",
    "VisibleWhen": "group2.Count > 0"
  }
}
```

## Placeholders
- `{playtime}`: Total VRChat playtime (from Steam)
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
- `{interval}`: Current update interval (formatted)
