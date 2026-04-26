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
        
        // Helper to get group members with fallback to VRCX DB
        List<string> GetGroupMembers(string groupName, string vrcxGroupName)
        {
            var apiGroup = favorites?.Where(f => f.Tags != null && f.Tags.Contains(groupName)).Select(f => f.FavoriteId).ToList();
            if (apiGroup != null && apiGroup.Count > 0) return apiGroup;
            
            // Fallback to VRCX local DB
            return vrcxDb.GetFavoriteFriends(vrcxGroupName);
        }

        var group0 = GetGroupMembers("group_0", "friend:group_1");
        var group1 = GetGroupMembers("group_1", "friend:group_2");
        var group2 = GetGroupMembers("group_2", "friend:group_3");

        var group0Names = await GetDisplayNameList(group0);
        var group1Names = await GetDisplayNameList(group1);
        var group2Names = await GetDisplayNameList(group2);
        Console.WriteLine($"Group sizes: G0={group0Names.Count}, G1={group1Names.Count}, G2={group2Names.Count}");

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

        var vars = new Dictionary<string, string>
        {
            { "{last_activity}", ((long)(DateTime.UtcNow - currentUser.LastActivity).TotalMilliseconds).ToText() },
            { "{playtime}", playtimeText },
            { "{vrcx_playtime}", TimeSpan.FromMilliseconds(vrcxPlaytimeSeconds).ToText() },
            { "{date_joined}", currentUser.DateJoined.ToString("yyyy-MM-dd") },
            { "{now}", now.ToString("yyyy-MM-dd HH:mm:ss") + " GMT+1" },
            { "{friends}", currentUser.Friends?.Count.ToString() ?? "0" },
            { "{blocked}", blocks.ToString() },
            { "{muted}", mutes.ToString() },
            { "{group0}", string.Join(", ", group0Names) },
            { "{group1}", string.Join(", ", group1Names) },
            { "{group2}", string.Join(", ", group2Names) },

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

        var evalVars = new Dictionary<string, string>
        {
            { "group0.Count", group0Names.Count.ToString() },
            { "group1.Count", group1Names.Count.ToString() },
            { "group2.Count", group2Names.Count.ToString() },
            { "friends.Count", (currentUser.Friends?.Count ?? 0).ToString() }
        };

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
        var newBioPart = template;
        
        // Multi-pass replacement to support nested custom variables
        string lastBio;
        int passes = 0;
        do
        {
            lastBio = newBioPart;
            foreach (var v in vars)
            {
                newBioPart = newBioPart.Replace(v.Key, v.Value);
            }
            passes++;
        } while (newBioPart != lastBio && passes < 3);

        var finalBio = (string.IsNullOrWhiteSpace(oldBio) ? "" : oldBio + separator) + newBioPart;
        if (finalBio.Length > 512) {
            Console.WriteLine($"\nWarning: Final bio length ({finalBio.Length}) exceeds 512 characters. Truncating...");
            finalBio = finalBio.Substring(0, 509) + "...";
        }

        Console.WriteLine($"\nFinal Bio Preview:\n------------------\n{finalBio}\n------------------");
        Console.WriteLine("Updating bio on VRChat servers...");
        await client.Users.UpdateUserAsync(currentUser.Id, new UpdateUserRequest(bio: finalBio));
        Console.WriteLine("Bio update complete.");
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
