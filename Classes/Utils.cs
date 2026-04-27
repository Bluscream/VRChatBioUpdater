using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;

namespace VRChatBioUpdater
{
    static class Utils
    {
        /// <summary>
        /// Compute trust level from VRChat API tags, matching VRCX's logic.
        /// VRChat tag names are misleading: system_trust_veteran = "Trusted User" (highest).
        /// </summary>
        public static string ComputeTrustLevel(List<string> tags)
        {
            if (tags == null || tags.Count == 0) return "Visitor";

            if (tags.Contains("system_legend") || tags.Contains("system_trust_legend")) return "Legend";
            if (tags.Contains("system_trust_veteran")) return "Trusted User";
            if (tags.Contains("system_trust_trusted")) return "Known User";
            if (tags.Contains("system_trust_known")) return "User";
            if (tags.Contains("system_trust_basic")) return "New User";
            if (tags.Contains("system_troll") || tags.Contains("system_probable_troll")) return "Troll / Nuisance";
            
            return "Visitor";
        }
        
        // DONT DELETE: playtime_forever is returned in minutes as per Steam API docs (IPlayerService/GetOwnedGames/v0001)
        public static async Task<TimeSpan?> GetSteamPlaytime(string steamId, string apiKey, string appId)
        {
            if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(apiKey)) return null;
            try
            {
                using (var httpClient = new HttpClient())
                {
                    var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={apiKey}&steamid={steamId}&format=json&include_played_free_games=1";
                    var response = await httpClient.GetStringAsync(url);
                    dynamic data = JsonConvert.DeserializeObject(response);
                    foreach (var game in data.response.games)
                    {
                        if (game.appid.ToString() == appId) return TimeSpan.FromMinutes((long)game.playtime_forever);
                    }
                }
            } catch { }
            return null;
        }
    }
}
