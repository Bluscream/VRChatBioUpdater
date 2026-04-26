using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRChatBioUpdater
{
    /// <summary>
    /// Loads and manages user tags from FewTags-format JSON files (local or remote).
    /// Schema per entry: { "id": int, "active": bool, "malicious": bool, "tags": string[], "tag": string, "foreground_color": string, "sources": string[] }
    /// Tag strings contain Unity rich-text markup like <color=#ff0000>Text</color> and <b>/<i> formatting.
    /// </summary>
    internal class TagManager
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // userId -> entry data
        private Dictionary<string, TagEntry> _userTags = new Dictionary<string, TagEntry>();

        /// <summary>Number of unique tag strings across all users.</summary>
        public int TagsLoadedCount { get; private set; }

        /// <summary>Number of distinct users with at least one tag.</summary>
        public int TaggedUsersCount => _userTags.Count;

        /// <summary>Number of users flagged as malicious.</summary>
        public int MaliciousCount => _userTags.Values.Count(e => e.Malicious);

        /// <summary>Check if a user has any tags.</summary>
        public bool IsUserTagged(string userId) => _userTags.ContainsKey(userId);

        /// <summary>
        /// Load tags from a list of URLs or local file paths.
        /// Supports both http(s):// URLs and local filesystem paths.
        /// </summary>
        public async Task LoadTagsAsync(List<string> sources)
        {
            _userTags.Clear();
            TagsLoadedCount = 0;
            var allTagStrings = new HashSet<string>();

            foreach (var source in sources)
            {
                try
                {
                    string json;
                    if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[TagManager] Fetching tags from URL: {source}");
                        json = await _httpClient.GetStringAsync(source);
                    }
                    else
                    {
                        var localPath = Environment.ExpandEnvironmentVariables(source);
                        Console.WriteLine($"[TagManager] Loading tags from file: {localPath}");
                        if (!File.Exists(localPath))
                        {
                            Console.WriteLine($"[TagManager] File not found: {localPath}");
                            continue;
                        }
                        json = await File.ReadAllTextAsync(localPath);
                    }

                    var data = JsonConvert.DeserializeObject<Dictionary<string, TagEntry>>(json);
                    if (data == null) continue;

                    foreach (var kvp in data)
                    {
                        var userId = kvp.Key;
                        var entry = kvp.Value;

                        // Merge: later sources overwrite earlier ones for the same user
                        _userTags[userId] = entry;

                        // Count unique tag strings
                        if (entry.Tags != null)
                        {
                            foreach (var tag in entry.Tags)
                            {
                                var clean = StripRichText(tag);
                                if (!string.IsNullOrWhiteSpace(clean))
                                    allTagStrings.Add(clean);
                            }
                        }
                    }

                    Console.WriteLine($"[TagManager] Loaded {data.Count} user entries from source.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TagManager] Error loading tags from {source}: {ex.Message}");
                }
            }

            TagsLoadedCount = allTagStrings.Count;
        }

        /// <summary>
        /// Get the tag entry for a specific user, or null if not found.
        /// </summary>
        public TagEntry GetUserEntry(string userId)
        {
            return _userTags.TryGetValue(userId, out var entry) ? entry : null;
        }

        /// <summary>
        /// Get the clean (stripped) tag text for a user, or null.
        /// </summary>
        public string GetUserTagText(string userId)
        {
            var entry = GetUserEntry(userId);
            return entry?.Tag;
        }

        /// <summary>
        /// Check if a user is flagged as malicious.
        /// </summary>
        public bool IsUserMalicious(string userId)
        {
            var entry = GetUserEntry(userId);
            return entry?.Malicious ?? false;
        }

        /// <summary>
        /// Strip Unity rich-text markup from a tag string.
        /// Removes: color, /color, b, /b, i, /i tags.
        /// </summary>
        public static string StripRichText(string tagText)
        {
            if (string.IsNullOrEmpty(tagText)) return "";

            // Remove <color=#XXXXXX> and </color>
            var clean = Regex.Replace(tagText, @"<color=#[A-Fa-f0-9]{3,8}>", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"</color>", "", RegexOptions.IgnoreCase);

            // Remove <b>, </b>, <i>, </i>
            clean = Regex.Replace(clean, @"</?[bi]>", "", RegexOptions.IgnoreCase);

            return clean.Trim();
        }
    }

    /// <summary>
    /// Represents a single user's tag entry from a FewTags JSON file.
    /// </summary>
    internal class TagEntry
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; }

        [JsonProperty("malicious")]
        public bool Malicious { get; set; }

        /// <summary>Array of rich-text formatted tag strings.</summary>
        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        /// <summary>Plain-text summary tag (no rich-text markup).</summary>
        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("foreground_color")]
        public string ForegroundColor { get; set; }

        [JsonProperty("sources")]
        public List<string> Sources { get; set; }
    }
}
