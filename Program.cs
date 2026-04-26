using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OtpNet;
using System.Runtime.Versioning;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using VRChatBioUpdater;
using Configuration = VRChatBioUpdater.Configuration;

[assembly: SupportedOSPlatform("windows")]

internal class Program
{
    public static readonly Uri RepositoryUrl = new Uri("https://github.com/Bluscream/VRChatBioUpdater/");
    private static readonly FileInfo ownExe = new FileInfo(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName + ".exe"));
    private static readonly DirectoryInfo baseDir = ownExe.Directory ?? new DirectoryInfo(Environment.CurrentDirectory);
    private static readonly string exeName = Path.GetFileNameWithoutExtension(ownExe.Name);
    internal static IVRChat client;
    internal static Configuration cfg;

    static async Task Main(string[] _args)
    {
        AppDomain.CurrentDomain.SetData("DataDirectory", AppContext.BaseDirectory);
        bool runOnce = _args.Any(arg => arg.Equals("--once", StringComparison.OrdinalIgnoreCase) || 
                                        arg.Equals("-once", StringComparison.OrdinalIgnoreCase) || 
                                        arg.Equals("/once", StringComparison.OrdinalIgnoreCase));
        cfg = new Configuration(new FileInfo(Path.Combine(baseDir.FullName, $"{exeName}.json")));
        cfg.LoadConfiguration();

        try
        {
            bool loginSuccessful = false;
            CurrentUser currentUser = null;
            try
            {
            try
            {
                Console.WriteLine("Initializing VRChat API client...");
                client = new VRChatClientBuilder()
                    .WithCredentials(cfg.App.Username, cfg.App.Password)
                    .WithApplication("VRChatBioUpdater", "1.0.0", "https://github.com/Bluscream/VRChatBioUpdater/")
                    .Build();

                // Set up initial cookies from config
                if (!string.IsNullOrWhiteSpace(cfg.App._AuthCookie))
                {
                    Console.WriteLine("Using existing cookies from App Config");
                    var domains = new[] { "api.vrchat.cloud", "vrchat.com" };
                    foreach (var domain in domains)
                    {
                        client.HttpClientHandler.CookieContainer.Add(new Uri($"https://{domain}"), new System.Net.Cookie("auth", cfg.App._AuthCookie));
                        if (!string.IsNullOrWhiteSpace(cfg.App._TwoFactorAuthCookie))
                            client.HttpClientHandler.CookieContainer.Add(new Uri($"https://{domain}"), new System.Net.Cookie("twoFactorAuth", cfg.App._TwoFactorAuthCookie));
                    }
                }

                Console.WriteLine("Logging in...");
                currentUser = await client.LoginWithExternalCodeAsync((providers) =>
                {
                    if (!string.IsNullOrWhiteSpace(cfg.App.TOTPSecret) && providers.Contains("totp"))
                    {
                        var secretBytes = Base32Encoding.ToBytes(cfg.App.TOTPSecret);
                        var code = new Totp(secretBytes).ComputeTotp(DateTime.UtcNow);
                        Console.WriteLine($"Generated 2FA Code (TOTP): {code}");
                        return new TwoFactorAuthCode(code);
                    }

                    if (providers.Contains("emailOtp"))
                    {
                        Console.WriteLine("Email 2FA required");
                        Console.Write("Enter 2FA Code: ");
                        var code = Console.ReadLine();
                        return new TwoFactorEmailCode(code ?? string.Empty);
                    }

                    Console.WriteLine($"2FA required (Available providers: {string.Join(", ", providers)})");
                    Console.Write("Enter 2FA Code: ");
                    var manualCode = Console.ReadLine();
                    return new TwoFactorAuthCode(manualCode ?? string.Empty);
                });

                if (currentUser != null)
                {
                    Console.WriteLine($"Logged in as {currentUser.DisplayName}");
                    loginSuccessful = true;
                    CaptureCookies();
                }
            }
            catch (ApiException ex)
            {
                Console.WriteLine($"Login failed (API): {ex.Message} (Code: {ex.ErrorCode})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login failed: {ex.Message}");
            }
            }
            catch (ApiException ex)
            {
                Console.WriteLine($"Login failed (API): {ex.Message} (Code: {ex.ErrorCode})");
                Environment.ExitCode = 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login failed: {ex.Message}");
                Environment.ExitCode = 1;
            }

            if (loginSuccessful)
            {
                Console.WriteLine($"Initial delay: {cfg.App.InitialDelay}ms");
                await Task.Delay(cfg.App.InitialDelay);

                while (true)
                {
                    try
                    {
                        // Use the currentUser we already fetched for the very first update to save an API call
                        await UpdateBio(currentUser);
                        // Reset currentUser so subsequent loops fetch it fresh
                        currentUser = null;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in bio update loop: {ex.Message}");
                        if (runOnce) Environment.ExitCode = 1;
                    }

                    if (runOnce) break;

                    Console.WriteLine($"Waiting {cfg.App.UpdateInterval}ms for next update...");
                    await Task.Delay(cfg.App.UpdateInterval);
                }
            }
            else
            {
                Console.WriteLine("Login failed. Cannot update bio.");
                Environment.ExitCode = 1;
            }
        }
        finally
        {
            cfg?.SaveConfiguration();
            if (!runOnce)
            {
                Console.WriteLine("Exiting. Press any key to close...");
                Console.ReadKey();
            }
        }
    }

    private static void CaptureCookies()
    {
        var cookies = client.GetCookies();
        foreach (var cookie in cookies)
        {
            if (cookie.Name == "auth") cfg.App._AuthCookie = cookie.Value;
            if (cookie.Name == "twoFactorAuth") cfg.App._TwoFactorAuthCookie = cookie.Value;
        }
        Console.WriteLine($"Cookies captured: Auth={!string.IsNullOrEmpty(cfg.App._AuthCookie)}, 2FA={!string.IsNullOrEmpty(cfg.App._TwoFactorAuthCookie)}");
    }

    private static async Task UpdateBio(CurrentUser initialUser = null)
    {
        Console.WriteLine("\n--- Starting Bio Update ---");
        
        var currentUser = initialUser;
        var userResp = (ApiResponse<CurrentUser>)null;

        if (currentUser == null)
        {
            userResp = await client.Authentication.GetCurrentUserWithHttpInfoAsync();
            currentUser = userResp.Data;

            // Handle mid-loop 2FA requirements
            if (currentUser == null && userResp.RawContent.Contains("requiresTwoFactorAuth") && !string.IsNullOrWhiteSpace(cfg.App.TOTPSecret))
            {
                Console.WriteLine("Session expired or requires 2FA. Attempting automatic re-verification...");
                try
                {
                    var secretBytes = Base32Encoding.ToBytes(cfg.App.TOTPSecret);
                    var code = new Totp(secretBytes).ComputeTotp(DateTime.UtcNow);
                    await client.Authentication.Verify2FAAsync(new TwoFactorAuthCode(code));
                    Console.WriteLine("Re-verified 2FA. Retrying profile fetch...");
                    userResp = await client.Authentication.GetCurrentUserWithHttpInfoAsync();
                    currentUser = userResp.Data;
                    CaptureCookies();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Auto 2FA re-verification failed: {ex.Message}");
                }
            }
        }

        if (currentUser == null && userResp != null && userResp.StatusCode == System.Net.HttpStatusCode.OK)
        {
            try
            {
                currentUser = JsonConvert.DeserializeObject<CurrentUser>(userResp.RawContent, new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Manual profile deserialization failed: {ex.Message}");
            }
        }

        if (currentUser == null)
        {
            var status = userResp?.StatusCode.ToString() ?? "Unknown";
            var content = userResp?.RawContent ?? "No content";
            Console.WriteLine($"Failed to fetch current user profile (Status: {status}). Content: {content}");
            return;
        }

        Console.WriteLine($"Fetched profile for: {currentUser.DisplayName}");

        Console.WriteLine("Fetching player moderations...");
        var moderations = await client.Moderations.GetPlayerModerationsAsync();
        var blocks = moderations?.Count(m => m.Type == PlayerModerationType.Block) ?? 0;
        var mutes = moderations?.Count(m => m.Type == PlayerModerationType.Mute) ?? 0;
        Console.WriteLine($"Found {blocks} blocks and {mutes} mutes.");

        Console.WriteLine($"Loading VRCX database from: {cfg.App.VrcxDbPath}");
        var vrcxDb = new VrcxDatabase(cfg.App.VrcxDbPath);

        Console.WriteLine("Fetching favorites for group placeholders...");
        var favorites = await client.Favorites.GetFavoritesAsync(type: "friend");
        
        // Dynamic group variables as suggested by user
        var allGroupIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // 1. Collect from VRChat API favorites
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

        // 2. Fetch Group Metadata for DisplayNames
        Console.WriteLine("Fetching favorite group metadata...");
        var favoriteGroups = await client.Favorites.GetFavoriteGroupsAsync();
        var tagToDisplayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (favoriteGroups != null) {
            foreach (var fg in favoriteGroups) {
                tagToDisplayName[fg.Name] = fg.DisplayName;
            }
        }

        // 3. Collect from VRCX (merge or fallback)
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

        // 4. Process all groups into display name lists
        var groupNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var groupVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var groupEvalVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var group in allGroupIds)
        {
            var names = await GetDisplayNameList(group.Value);
            var namesStr = string.Join(", ", names);
            
            // Store raw list for the advanced resolver
            groupNames[group.Key] = names;
            if (tagToDisplayName.TryGetValue(group.Key, out var dName)) {
                groupNames[dName] = names;
            }

            // Primary variables (e.g. {group_0} or {friend:group_1})
            groupVars[$"{{{group.Key}}}"] = namesStr;
            groupVars[$"{{{group.Key}:names}}"] = namesStr;
            groupEvalVars[$"{group.Key}.Count"] = names.Count.ToString();
            
            // Convenient aliases for standard groups
            if (group.Key.StartsWith("group_", StringComparison.OrdinalIgnoreCase)) {
                var alias = group.Key.Replace("_", ""); // group_0 -> group0
                groupVars[$"{{{alias}}}"] = namesStr;
                groupEvalVars[$"{alias}.Count"] = names.Count.ToString();
            }
            else if (group.Key.StartsWith("friend:group_", StringComparison.OrdinalIgnoreCase)) {
                var indexStr = group.Key.Substring("friend:group_".Length);
                if (int.TryParse(indexStr, out int idx)) {
                    var alias = $"group{idx-1}"; // friend:group_1 -> group0
                    groupVars[$"{{{alias}}}"] = namesStr;
                    groupEvalVars[$"{alias}.Count"] = names.Count.ToString();
                }
            }
            
            // Add DisplayName aliases if available
            if (tagToDisplayName.TryGetValue(group.Key, out var displayName)) {
                groupVars[$"{{{displayName}}}"] = namesStr;
                groupVars[$"{{{displayName}:names}}"] = namesStr;
                groupEvalVars[$"{displayName}.Count"] = names.Count.ToString();
            }
        }
        Console.WriteLine($"Dynamic Variables: Generated {groupVars.Count} group placeholders.");

        if (!string.IsNullOrWhiteSpace(cfg.App.SteamId)) {
            Console.WriteLine($"Fetching Steam playtime for {cfg.App.SteamId}...");
        }
        long? steamPlaytimeMinutes = await GetSteamPlaytime(cfg.App.SteamId, cfg.App.SteamApiKey, cfg.App.SteamAppId);
        string playtimeText = (steamPlaytimeMinutes * 60 * 1000 ?? 0).ToText();
        if (steamPlaytimeMinutes.HasValue) playtimeText += $" ({Math.Floor(steamPlaytimeMinutes.Value / 60.0):00}h)";

        var separator = cfg.App.Separator ?? "\n-\n";
        var bioParts = (currentUser.Bio ?? "").Split(new[] { separator }, StringSplitOptions.None);
        var oldBio = bioParts.Length > 1 ? bioParts[0] : "";

        var vrcxPlaytimeSeconds = vrcxDb.GetTotalPlaytimeSeconds(currentUser.Id);

        Console.WriteLine("\n--- Template & Variables ---");
        var template = cfg.App.EffectiveBioTemplate;
        Console.WriteLine($"Raw Template:\n{template}\n");
        // Fetch tags from GitHub
        Console.WriteLine("Fetching tags from configured URLs...");
        var tagManager = new TagManager();
        await tagManager.LoadTagsAsync(cfg.App.TagUrls);
        Console.WriteLine($"Tags loaded: {tagManager.TagsLoadedCount} unique tags across {tagManager.TaggedUsersCount} users.");



        var now = DateTime.Now;
        // Calculate local tagged users (similar to VRCX bio-updater)
        var taggedUsersCount = 0;
        if (currentUser.Friends != null)
        {
            taggedUsersCount += currentUser.Friends.Count(f => tagManager.IsUserTagged(f));
        }
        if (moderations != null)
        {
            var targetIds = moderations.Select(m => m.TargetUserId).Distinct();
            taggedUsersCount += targetIds.Count(id => tagManager.IsUserTagged(id));
        }

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "{last_activity}", ((long)(DateTime.UtcNow - currentUser.LastActivity).TotalMilliseconds).ToText() },
            { "{playtime}", playtimeText },
            { "{vrcx_playtime}", TimeSpan.FromMilliseconds(vrcxPlaytimeSeconds).ToText() },
            { "{date_joined}", currentUser.DateJoined.ToString("yyyy-MM-dd") },
            { "{now}", now.ToString("yyyy-MM-dd HH:mm:ss") + " GMT+1" },
            { "{friends}", currentUser.Friends?.Count.ToString() ?? "0" },
            { "{blocked}", blocks.ToString() },
            { "{muted}", mutes.ToString() },

            { "{tags_loaded}", tagManager.TagsLoadedCount.ToString() },
            { "{tagged_users}", taggedUsersCount.ToString() },
            { "{memos}", vrcxDb.GetMemosCount().ToString() },
            { "{notes}", vrcxDb.GetNotesCount(currentUser.Id).ToString() },
            { "{user_id}", currentUser.Id },
            { "{steam_id}", currentUser.SteamId ?? "none" },
            { "{oculus_id}", currentUser.OculusId ?? "none" },
            { "{pico_id}", currentUser.PicoId ?? "none" },
            { "{vive_id}", currentUser.ViveId ?? "none" },
            { "{rank}", Utils.ComputeTrustLevel(currentUser.Tags) },
            { "{interval}", ((long)cfg.App.UpdateInterval).ToText() }
        };

        // Merge dynamic group variables
        foreach (var gv in groupVars) vars[gv.Key] = gv.Value;

        var evalVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "friends.Count", (currentUser.Friends?.Count ?? 0).ToString() }
        };

        // Merge dynamic group eval variables
        foreach (var gev in groupEvalVars) evalVars[gev.Key] = gev.Value;

        var dt = new System.Data.DataTable();
        if (cfg.App.CustomVariables != null)
        {
            foreach (var cv in cfg.App.CustomVariables)
            {
                var condition = cv.Value.VisibleWhen;
                if (!string.IsNullOrWhiteSpace(condition))
                {
                    foreach (var ev in evalVars) { condition = condition.Replace(ev.Key, ev.Value); }
                    bool isVisible = false;
                    try { isVisible = Convert.ToBoolean(dt.Compute(condition, "")); } catch {}
                    vars[$"{{{cv.Key}}}"] = isVisible ? cv.Value.Content : "";
                }
                else
                {
                    vars[$"{{{cv.Key}}}"] = cv.Value.Content;
                }
            }
        }

        Console.WriteLine("Evaluation Variables:");
        foreach (var v in evalVars) {
            Console.WriteLine($"  {v.Key,-20} : {v.Value}");
        }

        Console.WriteLine("\nVariables:");
        foreach (var v in vars) {
            Console.WriteLine($"  {v.Key,-20} : {v.Value}");
        }

        Console.WriteLine("\nGenerating new bio content...");
        var finalBioContent = ResolveTemplate(template, vars, groupNames);
        var finalBio = (string.IsNullOrWhiteSpace(oldBio) ? "" : oldBio + separator) + finalBioContent;

        if (finalBio.Length > 512) {
            Console.WriteLine($"\nWarning: Final bio length ({finalBio.Length}) exceeds 512 characters. Truncating...");
            finalBio = finalBio.Substring(0, 509) + "...";
        }

        Console.WriteLine($"\nFinal Bio Preview:\n------------------\n{finalBio}\n------------------");
        Console.WriteLine("Updating bio on VRChat servers...");
        await client.Users.UpdateUserAsync(currentUser.Id, new UpdateUserRequest(bio: finalBio));
        Console.WriteLine("Bio update complete.");
    }

    private static string ResolveTemplate(string template, Dictionary<string, string> vars, Dictionary<string, List<string>> groupNames)
    {
        var result = template;
        var regex = new System.Text.RegularExpressions.Regex(@"\{(?<key>[^,}]+)(?:,(?<sep>[^}]+))?\}");
        
        string lastResult;
        int passes = 0;
        do {
            lastResult = result;
            result = regex.Replace(result, m => {
                var key = m.Groups["key"].Value.Trim();
                var sep = m.Groups["sep"].Success ? m.Groups["sep"].Value : null;
                
                // Normalize key for groups
                var groupKey = key;
                if (groupKey.StartsWith("favorites.", StringComparison.OrdinalIgnoreCase)) groupKey = groupKey.Substring(10);
                if (groupKey.EndsWith(".names", StringComparison.OrdinalIgnoreCase)) groupKey = groupKey.Substring(0, groupKey.Length - 6);

                if (groupNames.TryGetValue(groupKey, out var list)) {
                    var separator = sep != null ? sep.Replace("\\n", "\n") : ", ";
                    return string.Join(separator, list);
                }

                // If it's a count property
                if (key.EndsWith(".Count", StringComparison.OrdinalIgnoreCase)) {
                    var stem = key.Substring(0, key.Length - 6);
                    if (stem.StartsWith("favorites.", StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(10);
                    if (groupNames.TryGetValue(stem, out var list2)) return list2.Count.ToString();
                }

                // Fallback to simple variables
                if (vars.TryGetValue($"{{{key}}}", out var val)) return val;
                
                return m.Value; // Keep as is if not found
            });
            passes++;
        } while (result != lastResult && passes < 3);
        
        return result;
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
                var data = JsonConvert.DeserializeObject<dynamic>(response);
                foreach (var game in data.response.games)
                {
                    if (game.appid.ToString() == appId) return (long)game.playtime_forever;
                }
            }
        }
        catch { }
        return null;
    }


}
