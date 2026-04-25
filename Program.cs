using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OtpNet;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using VRChatBioUpdater;
using Configuration = VRChatBioUpdater.Configuration;

internal class Program
{
    public static readonly Uri RepositoryUrl = new Uri("https://github.com/Bluscream/VRChatBioUpdater/");
    private static readonly FileInfo ownExe = new FileInfo(Assembly.GetExecutingAssembly().Location);
    private static readonly DirectoryInfo baseDir = ownExe.Directory ?? new DirectoryInfo(Environment.CurrentDirectory);
    private static readonly string exeName = Path.GetFileNameWithoutExtension(ownExe.Name);
    internal static VrcApiClient client;
    internal static Configuration cfg;

    static async Task Main(string[] _args)
    {
        cfg = new Configuration(new FileInfo(Path.Combine(baseDir.FullName, $"{exeName}.json")));
        cfg.LoadConfiguration();

        try
        {
            bool loginSuccessful = false;
            CurrentUser currentUser = null;
            try
            {
                client = new VrcApiClient();
                client.Configuration = new VRChat.API.Client.Configuration()
                {
                    UserAgent = cfg.App.UserAgent,
                    Username = cfg.App.Username,
                    Password = cfg.App.Password
                };

                Console.WriteLine("Logging in...");
                
                // Set up initial cookies from config
                if (!string.IsNullOrWhiteSpace(cfg.App._AuthCookie))
                {
                    Console.WriteLine("Using existing cookies from App Config");
                    var domains = new[] { ".vrchat.com", "api.vrchat.cloud", "vrchat.com" };
                    foreach (var domain in domains)
                    {
                        ApiClient.CookieContainer.Add(new System.Net.Cookie("auth", cfg.App._AuthCookie, "/", domain));
                        if (!string.IsNullOrWhiteSpace(cfg.App._TwoFactorAuthCookie))
                            ApiClient.CookieContainer.Add(new System.Net.Cookie("twoFactorAuth", cfg.App._TwoFactorAuthCookie, "/", domain));
                    }
                }

                // Initial check
                var currentUserResp = await client.Auth.GetCurrentUserWithHttpInfoAsync();
                
                // If we already have user data, we are logged in!
                if (currentUserResp.Data != null)
                {
                    currentUser = currentUserResp.Data;
                    Console.WriteLine($"Already logged in as {currentUser.DisplayName}");
                    loginSuccessful = true;
                }
                else
                {
                    // Handle 2FA only if not logged in
                    if (currentUserResp.RawContent.Contains("emailOtp"))
                    {
                        Console.WriteLine("Email 2FA required");
                        Console.Write("Enter 2FA Code: ");
                        var code = Console.ReadLine();
                        if (string.IsNullOrWhiteSpace(code)) throw new Exception("2FA code cannot be empty.");
                        await client.Auth.Verify2FAEmailCodeAsync(new TwoFactorEmailCode(code));
                    }
                    else if (currentUserResp.RawContent.Contains("requiresTwoFactorAuth") || currentUserResp.RawContent.Contains("twoFactorAuth"))
                    {
                        Console.WriteLine("Regular 2FA required");
                        var code = string.Empty;
                        if (string.IsNullOrWhiteSpace(cfg.App.TOTPSecret))
                        {
                            Console.Write("Enter 2FA Code: ");
                            code = Console.ReadLine();
                            if (string.IsNullOrWhiteSpace(code)) throw new Exception("2FA code cannot be empty.");
                        }
                        else
                        {
                            try
                            {
                                var secretBytes = Base32Encoding.ToBytes(cfg.App.TOTPSecret);
                                code = new Totp(secretBytes).ComputeTotp(DateTime.UtcNow);
                                Console.WriteLine($"Generated 2FA Code: {code}");
                            }
                            catch (Exception ex)
                            {
                                throw new Exception($"Error generating TOTP code: {ex.Message}");
                            }
                        }
                        await client.Auth.Verify2FAAsync(new TwoFactorAuthCode(code));
                        Console.WriteLine("2FA verified. Waiting for session to stabilize...");
                        await Task.Delay(2000);
                    }

                    // Verify auth token explicitly
                    Console.WriteLine("Verifying Auth Token...");
                    try {
                        await client.Auth.VerifyAuthTokenAsync();
                    } catch (ApiException ex) {
                        Console.WriteLine($"Auth Token verification warning: {ex.Message} ({ex.ErrorCode})");
                    }

                    // Capture fresh cookies
                    CaptureCookies();

                    // Final verification
                    var finalUserResp = await client.Auth.GetCurrentUserWithHttpInfoAsync();
                    currentUser = finalUserResp.Data;
                    
                    if (currentUser == null && finalUserResp.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        Console.WriteLine("Data was null but status OK. Attempting manual deserialization...");
                        try {
                            currentUser = JsonConvert.DeserializeObject<CurrentUser>(finalUserResp.RawContent, new JsonSerializerSettings { 
                                MissingMemberHandling = MissingMemberHandling.Ignore,
                                NullValueHandling = NullValueHandling.Ignore
                            });
                        } catch (Exception ex) {
                            Console.WriteLine($"Manual deserialization failed: {ex.Message}");
                        }
                    }

                    if (currentUser != null)
                    {
                        Console.WriteLine($"Logged in as {currentUser.DisplayName}");
                        loginSuccessful = true;
                    }
                    else
                    {
                        throw new Exception($"Login verification failed (Status: {finalUserResp.StatusCode}). Content: {finalUserResp.RawContent}");
                    }
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
                    }

                    Console.WriteLine($"Waiting {cfg.App.UpdateInterval}ms for next update...");
                    await Task.Delay(cfg.App.UpdateInterval);
                }
            }
            else
            {
                Console.WriteLine("Login failed. Cannot update bio.");
            }
        }
        finally
        {
            cfg?.SaveConfiguration();
            Console.WriteLine("Exiting. Press any key to close...");
            Console.ReadKey();
        }
    }

    private static void CaptureCookies()
    {
        if (ApiClient.CookieContainer != null)
        {
            var cookies = Extensions.GetAllCookies(ApiClient.CookieContainer).ToList();
            foreach (var cookie in cookies)
            {
                if (cookie.Name == "auth") cfg.App._AuthCookie = cookie.Value;
                if (cookie.Name == "twoFactorAuth") cfg.App._TwoFactorAuthCookie = cookie.Value;
            }
            Console.WriteLine($"Cookies captured: Auth={!string.IsNullOrEmpty(cfg.App._AuthCookie)}, 2FA={!string.IsNullOrEmpty(cfg.App._TwoFactorAuthCookie)}");
        }
    }

    private static async Task UpdateBio(CurrentUser initialUser = null)
    {
        Console.WriteLine("\n--- Starting Bio Update ---");
        
        var currentUser = initialUser;
        var userResp = (ApiResponse<CurrentUser>)null;

        if (currentUser == null)
        {
            userResp = await client.Auth.GetCurrentUserWithHttpInfoAsync();
            currentUser = userResp.Data;

            // Handle mid-loop 2FA requirements
            if (currentUser == null && userResp.RawContent.Contains("requiresTwoFactorAuth") && !string.IsNullOrWhiteSpace(cfg.App.TOTPSecret))
            {
            Console.WriteLine("Session expired or requires 2FA. Attempting automatic re-verification...");
            try
            {
                var secretBytes = Base32Encoding.ToBytes(cfg.App.TOTPSecret);
                var code = new Totp(secretBytes).ComputeTotp(DateTime.UtcNow);
                await client.Auth.Verify2FAAsync(new TwoFactorAuthCode(code));
                Console.WriteLine("Re-verified 2FA. Retrying profile fetch...");
                userResp = await client.Auth.GetCurrentUserWithHttpInfoAsync();
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
            try {
                currentUser = JsonConvert.DeserializeObject<CurrentUser>(userResp.RawContent, new JsonSerializerSettings { 
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                });
            } catch (Exception ex) {
                Console.WriteLine($"Manual profile deserialization failed: {ex.Message}");
            }
        }

        if (currentUser == null) {
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

        Console.WriteLine("Fetching favorites for group placeholders...");
        var favorites = await client.Favorites.GetFavoritesAsync(type: "friend");
        var group1 = favorites?.Where(f => f.Tags != null && f.Tags.Contains("group_1")).Select(f => f.FavoriteId).ToList() ?? new List<string>();
        var group2 = favorites?.Where(f => f.Tags != null && f.Tags.Contains("group_2")).Select(f => f.FavoriteId).ToList() ?? new List<string>();
        var group3 = favorites?.Where(f => f.Tags != null && f.Tags.Contains("group_3")).Select(f => f.FavoriteId).ToList() ?? new List<string>();

        var group1Names = await GetDisplayNameList(group1);
        var group2Names = await GetDisplayNameList(group2);
        var group3Names = await GetDisplayNameList(group3);
        Console.WriteLine($"Group sizes: G1={group1Names.Count}, G2={group2Names.Count}, G3={group3Names.Count}");

        if (!string.IsNullOrWhiteSpace(cfg.App.SteamId)) {
            Console.WriteLine($"Fetching Steam playtime for {cfg.App.SteamId}...");
        }
        long? steamPlaytimeMinutes = await GetSteamPlaytime(cfg.App.SteamId, cfg.App.SteamApiKey, cfg.App.SteamAppId);
        string playtimeText = TimeToText(steamPlaytimeMinutes * 60 * 1000 ?? 0);
        if (steamPlaytimeMinutes.HasValue) playtimeText += $" ({Math.Floor(steamPlaytimeMinutes.Value / 60.0):00}h)";

        var separator = cfg.App.Separator ?? "\n-\n";
        var bioParts = (currentUser.Bio ?? "").Split(new[] { separator }, StringSplitOptions.None);
        var oldBio = bioParts.Length > 1 ? bioParts[0] : "";

        Console.WriteLine($"Loading VRCX database from: {cfg.App.VrcxDbPath}");
        var vrcxDb = new VrcxDatabase(cfg.App.VrcxDbPath);
        var vrcxPlaytimeSeconds = vrcxDb.GetTotalPlaytimeSeconds(currentUser.Id);

        Console.WriteLine("\n--- Template & Variables ---");
        var template = cfg.App.EffectiveBioTemplate;
        Console.WriteLine($"Raw Template:\n{template}\n");

        var vars = new Dictionary<string, string>
        {
            { "{last_activity}", TimeToText((long)(DateTime.UtcNow - currentUser.LastActivity).TotalMilliseconds) },
            { "{playtime}", playtimeText },
            { "{vrcx_playtime}", TimeToText(vrcxPlaytimeSeconds * 1000) },
            { "{date_joined}", currentUser.DateJoined.ToString("yyyy-MM-dd") },
            { "{friends}", currentUser.Friends?.Count.ToString() ?? "0" },
            { "{blocked}", blocks.ToString() },
            { "{muted}", mutes.ToString() },
            { "{now}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " GMT+1" },
            { "{group1}", string.Join(", ", group1Names) },
            { "{group2}", string.Join(", ", group2Names) },
            { "{group3}", string.Join(", ", group3Names) },
            { "{tags_loaded}", vrcxDb.GetTotalTagsCount().ToString() },
            { "{tagged_users}", vrcxDb.GetTaggedUsersCount().ToString() },
            { "{memos}", vrcxDb.GetMemosCount().ToString() },
            { "{notes}", vrcxDb.GetNotesCount(currentUser.Id).ToString() },
            { "{user_id}", currentUser.Id },
            { "{rank}", currentUser.Tags?.FirstOrDefault(t => t.StartsWith("system_trust_"))?.Replace("system_trust_", "") ?? "unknown" }
        };

        Console.WriteLine("Variables:");
        foreach (var v in vars) {
            Console.WriteLine($"  {v.Key,-20} : {v.Value}");
        }

        Console.WriteLine("\nGenerating new bio content...");
        var newBioPart = template;
        foreach (var v in vars) {
            newBioPart = newBioPart.Replace(v.Key, v.Value);
        }

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

    private static string TimeToText(long ms)
    {
        if (ms <= 0) return "0s";
        var t = TimeSpan.FromMilliseconds(ms);
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {(int)t.Hours}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {(int)t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {(int)t.Seconds}s";
        return $"{(int)t.TotalSeconds}s";
    }
}
