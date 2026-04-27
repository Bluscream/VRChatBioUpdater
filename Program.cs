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
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
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

            client = new VRChatApiClient(appConfig.Username, appConfig.Password, appConfig.TOTPSecret, appConfig.AuthCookie, appConfig.TwoFactorAuthCookie);
            tagManager = new TagManager(appConfig.TagUrls);

            Console.WriteLine($"Initial delay: {appConfig.InitialDelay.ToHuman()}");
            await Task.Delay(appConfig.InitialDelay);

            // Capture cookies for next time
            Console.WriteLine($"[Debug] Cookies after delay: Auth={!string.IsNullOrEmpty(client.AuthCookie)}, 2FA={!string.IsNullOrEmpty(client.TwoFactorAuthCookie)}");
            config.App.AuthCookie = client.AuthCookie;
            config.App.TwoFactorAuthCookie = client.TwoFactorAuthCookie;
            config.SaveConfiguration();

            do
            {
                try
                {
                    var currentUser = await client.Auth.GetCurrentUserAsync();
                    if (currentUser.Friends == null || currentUser.Friends.Count == 0) {
                        try {
                            var friends = await client.Friends.GetFriendsAsync();
                            currentUser.Friends = friends.Select(f => f.Id).ToList();
                        } catch {
                            currentUser.Friends = new List<string>();
                        }
                    }
                    Console.WriteLine($"[Debug] User Friends Count: {currentUser.Friends.Count}");
                    Console.WriteLine($"\n--- Starting Updates for {currentUser.DisplayName} ---");
                    
                    var context = await CreateTemplateContext(currentUser);

                    await UpdateProfile(currentUser, context);
                    
                    Console.WriteLine("--- Done ---");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during updates: {ex.Message}\n{ex.StackTrace}");
                }

                if (once) break;

                Console.WriteLine($"Waiting {appConfig.UpdateInterval.ToHuman()} for next update...");
                await Task.Delay(appConfig.UpdateInterval);
            } while (true);
        }

        static async Task<TemplateContext> CreateTemplateContext(CurrentUser currentUser)
        {
            var moderations = await client.Playermoderations.GetPlayerModerationsAsync();
            var blockedCount = moderations.Count(m => m.Type == PlayerModerationType.Block);
            var mutedCount = moderations.Count(m => m.Type == PlayerModerationType.Mute);

            var vrcxDb = new VrcxDatabase(config.App.VrcxDbPath);
            var steamPlaytime = await Utils.GetSteamPlaytime(config.App.SteamId, config.App.SteamApiKey, config.App.SteamAppId);

            var favorites = await client.Favorites.GetFavoritesAsync(n: 100);
            
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

            Console.WriteLine("Fetching favorite groups...");
            var favoriteGroups = await client.Favorites.GetFavoriteGroupsAsync();
            var tagToDisplayName = favoriteGroups.ToDictionary(f => f.Name, f => f.DisplayName, StringComparer.OrdinalIgnoreCase);

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
                
                if (group.Key.StartsWith("group_", StringComparison.OrdinalIgnoreCase)) {
                    groupNames[group.Key.Replace("_", "")] = names;
                }
                else if (group.Key.StartsWith("friend:group_", StringComparison.OrdinalIgnoreCase)) {
                    var indexStr = group.Key.Substring("friend:group_".Length);
                    if (int.TryParse(indexStr, out int idx)) {
                        groupNames[$"group{idx}"] = names;
                        groupNames[$"friend_group_{idx}_names"] = names;
                    }
                }
            }

            await tagManager.RefreshTags();
            var taggedUsers = tagManager.GetTaggedUsers(currentUser.Friends);

            var scriptObject = new ScriptObject();
            scriptObject.Import(typeof(Humanizer.StringDehumanizeExtensions));
            scriptObject.Import(typeof(Humanizer.StringHumanizeExtensions));
            scriptObject.Import(typeof(Humanizer.TimeSpanHumanizeExtensions));
            scriptObject.Import(typeof(TemplateHelpers));

            var userObj = new ScriptObject();
            userObj.Import(currentUser, renamer: member => member.Name);
            userObj.Add("DateJoined", currentUser.DateJoined.ToDateTime(TimeOnly.MinValue));
            scriptObject.Add("user", userObj);
            scriptObject.Add("friends", currentUser.Friends);
            scriptObject.Add("user_rank", currentUser.Tags.GetHighestRank());
            
            var stats = new {
                friends = currentUser.Friends.Count,
                blocked = blockedCount,
                muted = mutedCount,
                tagged = taggedUsers,
                total_tags = tagManager.TotalTags
            };

            var playtime = new {
                steam = steamPlaytime,
                vrcx = vrcxDb.GetTotalPlaytime(currentUser.Id)
            };

            scriptObject.Add("stats", stats);
            scriptObject.Add("playtime", playtime);
            scriptObject.Add("config", config);
            scriptObject.Add("now", DateTime.Now);
            scriptObject.Add("interval", config.App.UpdateInterval);

            var favoritesHelper = new ScriptObject();
            foreach (var fg in favoriteGroups.Where(f => f.Type == FavoriteType.Friend))
            {
                var names = groupNames.TryGetValue(fg.Name, out var n) ? n : new List<string>();
                var item = new { id = fg.Id, name = fg.DisplayName, tag = fg.Name, names = names };
                
                var safeTag = fg.Name.Replace(":", "_").Replace("-", "_");
                if (!favoritesHelper.ContainsKey(safeTag)) favoritesHelper.Add(safeTag, item);

                // Add groupX aliases (e.g. friend-group-0 -> group0)
                var parts = fg.Name.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && int.TryParse(parts.Last(), out var idx))
                {
                    var alias = "group" + idx;
                    if (!favoritesHelper.ContainsKey(alias)) favoritesHelper.Add(alias, item);
                }
            }
            scriptObject.Add("favorites", favoritesHelper);

            // Backwards compatibility for groups object
            var groupsObj = new ScriptObject();
            foreach (var group in groupNames) {
                var safeName = group.Key.Replace(":", "_").Replace("-", "_");
                if (!groupsObj.ContainsKey(safeName)) groupsObj.Add(safeName, group.Value);
            }
            scriptObject.Add("groups", groupsObj);

            var context = new TemplateContext();
            context.PushGlobal(scriptObject);
            return context;
        }

        static async Task UpdateProfile(CurrentUser currentUser, TemplateContext context)
        {
            var finalBio = await GenerateBio(currentUser, context);
            var finalStatus = await GenerateStatus(currentUser, context);
            var finalLinks = await GenerateLinks(currentUser, context);

            var updateReq = new UpdateUserRequest();
            if (finalBio != null) updateReq.Bio = finalBio;
            if (finalStatus != null) updateReq.StatusDescription = finalStatus;
            if (finalLinks != null && finalLinks.Count > 0) updateReq.BioLinks = finalLinks;

            Console.WriteLine("Updating Profile...");
            await client.Users.UpdateUserAsync(currentUser.Id, updateReq);
            Console.WriteLine("Profile update complete.");
        }

        static async Task<string> GenerateBio(CurrentUser currentUser, TemplateContext context)
        {
            if (config.App.Bio == null || config.App.Bio.Count == 0) return null;
            var prefix = currentUser.Bio ?? "";
            if (prefix.Contains(config.App.BioSeparator)) {
                prefix = prefix.Split(new[] { config.App.BioSeparator }, StringSplitOptions.None)[0];
            }

            var rendered = await RenderSmartTemplate(config.App.Bio, context, 512 - (string.IsNullOrEmpty(prefix) ? 0 : prefix.Length + config.App.BioSeparator.Length));
            var finalBio = (string.IsNullOrEmpty(prefix) ? "" : prefix + config.App.BioSeparator) + rendered;
            
            Console.WriteLine($"Final Bio Preview:\n------------------\n{finalBio}\n------------------");
            return finalBio;
        }

        static async Task<string> GenerateStatus(CurrentUser currentUser, TemplateContext context)
        {
            if (config.App.Status == null || config.App.Status.Count == 0) return null;
            
            var prefix = currentUser.StatusDescription ?? "";
            if (prefix.Contains(config.App.StatusSeparator)) {
                prefix = prefix.Split(new[] { config.App.StatusSeparator }, StringSplitOptions.None)[0];
            }

            var rendered = await RenderSmartTemplate(config.App.Status, context, 32 - (string.IsNullOrEmpty(prefix) ? 0 : prefix.Length + config.App.StatusSeparator.Length));
            var finalStatus = (string.IsNullOrEmpty(prefix) ? "" : prefix + config.App.StatusSeparator) + rendered;

            Console.WriteLine($"Final Status: {finalStatus}");
            return finalStatus;
        }

        static async Task<List<string>> GenerateLinks(CurrentUser currentUser, TemplateContext context)
        {
            if (config.App.Links == null || config.App.Links.Count == 0) return null;

            var finalLinks = new List<string>();
            foreach (var linkTpl in config.App.Links) {
                try {
                    var rendered = await Template.Parse(linkTpl).RenderAsync(context);
                    if (!string.IsNullOrWhiteSpace(rendered)) finalLinks.Add(rendered);
                } catch (Exception ex) {
                    Console.WriteLine($"[Warning] Failed to render link template '{linkTpl}': {ex.Message}");
                }
            }

            if (finalLinks.Count > 0) {
                Console.WriteLine($"Final Links: {string.Join(", ", finalLinks)}");
                return finalLinks;
            }
            return null;
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
    }

    public static class TemplateHelpers {
        public static bool is_valid(object val) {
            if (val == null) return false;
            var s = val.ToString();
            return !string.IsNullOrEmpty(s) && s != "null" && s != "0s";
        }
        public static DateTime date_parse(string s) => DateTime.Parse(s);
        public static string humanize_span(TimeSpan span, string maxUnit = null) {
            if (string.IsNullOrEmpty(maxUnit)) return span.ToHuman();
            if (Enum.TryParse<Humanizer.TimeUnit>(maxUnit, true, out var unit)) {
                return span.ToHuman(unit);
            }
            return span.ToHuman();
        }
    }
}
