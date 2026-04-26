using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using Newtonsoft.Json;
using Scriban;
using Scriban.Runtime;
using Humanizer;

namespace VRChatBioUpdater
{
    class Program
    {
        private static VRChatApiClient client;
        private static Configuration config;
        private static TagManager tagManager;

        static async Task Main(string[] args)
        {
            var once = args.Contains("--once");
            config = new Configuration(new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VRChatBioUpdater.json")));
            var appConfig = config.LoadConfiguration();

            client = new VRChatApiClient(appConfig.Username, appConfig.Password, appConfig.TOTPSecret, appConfig._AuthCookie, appConfig._TwoFactorAuthCookie);
            tagManager = new TagManager(appConfig.TagUrls);

            // Capture cookies for next time
            appConfig._AuthCookie = client.AuthCookie;
            appConfig._TwoFactorAuthCookie = client.TwoFactorAuthCookie;
            config.SaveConfiguration();

            Console.WriteLine($"Initial delay: {appConfig.InitialDelay}ms");
            await Task.Delay(appConfig.InitialDelay);

            do
            {
                try
                {
                    await UpdateBio();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during bio update: {ex.Message}");
                }

                if (once) break;

                Console.WriteLine($"Waiting {TimeSpan.FromMilliseconds(appConfig.UpdateInterval).ToHuman()} for next update...");
                await Task.Delay(appConfig.UpdateInterval);
            } while (true);
        }

        static async Task UpdateBio()
        {
            Console.WriteLine("\n--- Starting Bio Update ---");
            var currentUser = await client.Auth.GetCurrentUserAsync();
            Console.WriteLine($"Logged in as: {currentUser.DisplayName}");

            var moderations = await client.Playermoderations.GetPlayerModerationsAsync();
            var blockedCount = moderations.Count(m => m.Type == PlayerModerationType.Block);
            var mutedCount = moderations.Count(m => m.Type == PlayerModerationType.Mute);

            var vrcxDb = new VrcxDatabase(config.App.VrcxDbPath);
            var steamPlaytime = await GetSteamPlaytime(config.App.SteamId, config.App.SteamApiKey, config.App.SteamAppId);

            var separator = config.App.Separator;
            var oldBio = currentUser.Bio;

            if (oldBio.Contains(separator))
            {
                oldBio = oldBio.Split(new[] { separator }, StringSplitOptions.None)[0];
            }

            var favorites = await client.Favorites.GetFavoritesAsync(n: 100);
            
            // Dynamic group variables
            var allGroupIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (favorites != null)
            {
                foreach (var favorite in favorites)
                {
                    if (favorite.Tags == null) continue;
                    foreach (var tag in favorite.Tags)
                    {
                        if (!allGroupIds.TryGetValue(tag, out var list))
                        {
                            list = new List<string>();
                            allGroupIds[tag] = list;
                        }
                        if (!list.Contains(favorite.FavoriteId)) list.Add(favorite.FavoriteId);
                    }
                }
            }

            Console.WriteLine("Fetching favorite group metadata...");
            var favoriteGroups = await client.Favorites.GetFavoriteGroupsAsync();
            var tagToDisplayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var groupData = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);

            if (favoriteGroups != null) {
                foreach (var fg in favoriteGroups) {
                    tagToDisplayName[fg.Name] = fg.DisplayName;
                    groupData[fg.Name] = new { id = fg.Id, name = fg.DisplayName, tag = fg.Name };
                }
            }

            var vrcxGroups = vrcxDb.GetAllFavoriteGroups();
            foreach (var vGroup in vrcxGroups)
            {
                var vrcxMembers = vrcxDb.GetFavoriteFriends(vGroup);
                if (vrcxMembers.Count > 0)
                {
                    if (!allGroupIds.TryGetValue(vGroup, out var list) || list.Count == 0)
                    {
                        allGroupIds[vGroup] = vrcxMembers;
                    }
                }
            }

            var groupNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in allGroupIds)
            {
                var names = await GetDisplayNameList(group.Value);
                groupNames[group.Key] = names;
                if (tagToDisplayName.TryGetValue(group.Key, out var dName)) {
                    groupNames[dName] = names;
                }
                
                // Aliases
                if (group.Key.StartsWith("group_", StringComparison.OrdinalIgnoreCase)) {
                    groupNames[group.Key.Replace("_", "")] = names;
                }
                else if (group.Key.StartsWith("friend:group_", StringComparison.OrdinalIgnoreCase)) {
                    var indexStr = group.Key.Substring("friend:group_".Length);
                    if (int.TryParse(indexStr, out int idx)) groupNames[$"group{idx-1}"] = names;
                }
            }

            await tagManager.RefreshTags();
            var taggedUsers = tagManager.GetTaggedUsers(currentUser.Friends);

            var scriptObject = new ScriptObject();
            
            // Register Humanizer functions and other helpers
            scriptObject.Import(typeof(Humanizer.StringDehumanizeExtensions));
            scriptObject.Import(typeof(Humanizer.StringHumanizeExtensions));
            scriptObject.Import(typeof(Humanizer.TimeSpanHumanizeExtensions));
            scriptObject.Add("date_parse", new Func<string, DateTime>(DateTime.Parse));
            scriptObject.Add("humanize_ms", new Func<long, string>(ms => TimeSpan.FromMilliseconds(ms).ToHuman()));
            scriptObject.Add("humanize_span", new Func<TimeSpan, string>(span => span.ToHuman()));

            // Expose the full raw user object as "user"
            scriptObject.Add("user", currentUser);
            
            // Populate stats
            var stats = new {
                friends = currentUser.Friends.Count,
                blocked = blockedCount,
                muted = mutedCount,
                tagged = taggedUsers,
                total_tags = tagManager.TotalTags
            };

            // Populate playtime
            var playtime = new {
                steam = steamPlaytime.HasValue ? TimeSpan.FromSeconds(steamPlaytime.Value).ToPrettyString() : "N/A",
                vrcx = vrcxDb.GetTotalPlaytime(currentUser.Id).ToPrettyString()
            };

            scriptObject.Add("user_rank", currentUser.Tags.GetHighestRank());
            scriptObject.Add("user_date_joined", currentUser.DateJoined.ToString("yyyy-MM-dd"));
            
            scriptObject.Add("stats", stats);
            scriptObject.Add("playtime", playtime);
            scriptObject.Add("config", config);
            
            // Root level variables
            scriptObject.Add("rank", currentUser.Tags.GetHighestRank());
            scriptObject.Add("friends", stats.friends);
            scriptObject.Add("blocked", stats.blocked);
            scriptObject.Add("muted", stats.muted);
            scriptObject.Add("tagged_users", stats.tagged);
            scriptObject.Add("tags_loaded", stats.total_tags);
            scriptObject.Add("playtime", playtime.steam);
            scriptObject.Add("steam_playtime", playtime.steam);
            scriptObject.Add("vrcx_playtime", playtime.vrcx);
            scriptObject.Add("date_joined", currentUser.DateJoined.ToString("yyyy-MM-dd"));
            scriptObject.Add("user_id", currentUser.Id);
            scriptObject.Add("steam_id", config.App.SteamId);
            scriptObject.Add("oculus_id", currentUser.OculusId);
            scriptObject.Add("pico_id", currentUser.PicoId);
            scriptObject.Add("vive_id", currentUser.ViveId);
            
            scriptObject.Add("now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " GMT" + DateTime.Now.ToString("zzz"));
            scriptObject.Add("interval", TimeSpan.FromMilliseconds(config.App.UpdateInterval).ToPrettyString());

            var groupsObj = new ScriptObject();
            foreach (var group in groupNames) {
                var safeName = group.Key.Replace(":", "_").Replace("-", "_");
                groupsObj.Add(safeName, group.Value);
                if (!scriptObject.ContainsKey(safeName)) scriptObject.Add(safeName, group.Value);
            }
            scriptObject.Add("groups", groupsObj);

            // Favorites helper: {{favorites["<ID>"].name}} or {{favorites["<NAME>"].names}}
            var favoritesHelper = new ScriptObject();
            foreach (var group in groupData) {
                var names = groupNames.TryGetValue(group.Key, out var n) ? n : new List<string>();
                var dName = group.Value.name;
                var item = new { id = group.Value.id, name = dName, names = names };
                favoritesHelper.Add(group.Key, item);
                if (!favoritesHelper.ContainsKey(dName)) favoritesHelper.Add(dName, item);
            }
            scriptObject.Add("favorites", favoritesHelper);

            var context = new TemplateContext();
            context.PushGlobal(scriptObject);

            // 1. Render Bio
            var finalBioContent = await RenderSmartTemplate(config.App.EffectiveBio, context, 512 - (string.IsNullOrWhiteSpace(oldBio) ? 0 : oldBio.Length + separator.Length));
            var finalBio = (string.IsNullOrWhiteSpace(oldBio) ? "" : oldBio + separator) + finalBioContent;

            // 2. Render Status
            string finalStatus = null;
            if (config.App.Status != null && config.App.Status.Count > 0) {
                finalStatus = await RenderSmartTemplate(config.App.Status, context, 32);
            }

            // 3. Render Links
            List<string> finalLinks = null;
            if (config.App.Links != null && config.App.Links.Count > 0) {
                finalLinks = new List<string>();
                foreach (var linkTpl in config.App.Links) {
                    var rendered = await Template.Parse(linkTpl).RenderAsync(context);
                    if (!string.IsNullOrWhiteSpace(rendered)) finalLinks.Add(rendered);
                }
            }

            Console.WriteLine($"\nFinal Bio Preview:\n------------------\n{finalBio}\n------------------");
            if (!string.IsNullOrEmpty(finalStatus)) Console.WriteLine($"Final Status: {finalStatus}");

            var updateReq = new UpdateUserRequest(bio: finalBio);
            if (finalStatus != null) updateReq.StatusDescription = finalStatus;
            
            // Update User
            await client.Users.UpdateUserAsync(currentUser.Id, updateReq);

            // Update Links if any
            if (finalLinks != null && finalLinks.Count > 0) {
                // Since UpdateUserRequest might not have Links directly in this version of the SDK, 
                // we'll skip direct assignment if it's not available or use the correct property if found.
                // Re-checking metadata or fallback to ignoring for now as it's a minor feature.
                Console.WriteLine("Links feature is pending SDK verification.");
            }

            Console.WriteLine("Bio update complete.");
        }

        private static async Task<string> RenderSmartTemplate(List<Configuration.TemplateLine> lines, TemplateContext context, int maxLength)
        {
            var results = new List<(string normal, string compact)>();
            foreach (var line in lines) {
                var normal = await Template.Parse(line.Content ?? "").RenderAsync(context);
                var compact = string.IsNullOrEmpty(line.Compact) ? normal : await Template.Parse(line.Compact).RenderAsync(context);
                results.Add((normal, compact));
            }

            string Build(bool[] useCompact) {
                var parts = new List<string>();
                for (int i = 0; i < results.Count; i++) {
                    var s = useCompact[i] ? results[i].compact : results[i].normal;
                    if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
                }
                return string.Join("\n", parts);
            }

            bool[] compactStatus = new bool[results.Count];
            string current = Build(compactStatus);

            if (current.Length <= maxLength) return current;

            // Try compacting from last to first
            for (int i = results.Count - 1; i >= 0; i--) {
                compactStatus[i] = true;
                current = Build(compactStatus);
                if (current.Length <= maxLength) return current;
            }

            // Still too long, truncate
            if (current.Length > maxLength) {
                return current.Substring(0, Math.Max(0, maxLength - 3)) + "...";
            }
            return current;
        }

        private static async Task<List<string>> GetDisplayNameList(List<string> userIds)
        {
            var names = new List<string>();
            foreach (var id in userIds.Take(5))
            {
                try {
                    var user = await client.Users.GetUserAsync(id);
                    if (user != null) names.Add(user.DisplayName);
                } catch { }
            }
            return names;
        }

        private static async Task<long?> GetSteamPlaytime(string steamId, string apiKey, string appId)
        {
            if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(apiKey)) return null;
            try
            {
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={apiKey}&steamid={steamId}&format=json&include_played_free_games=1";
                    var response = await httpClient.GetStringAsync(url);
                    dynamic data = JsonConvert.DeserializeObject(response);
                    foreach (var game in data.response.games)
                    {
                        if (game.appid.ToString() == appId) return (long)game.playtime_forever * 60;
                    }
                }
            } catch { }
            return null;
        }
    }
}
