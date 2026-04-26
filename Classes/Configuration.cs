using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace VRChatBioUpdater
{
    internal class Configuration
    {
        private FileInfo file;
        private string LoadedJson;
        internal AppConfig App = new AppConfig();
        public class AppConfig
        {
            public bool FetchDetails { get; set; } = true;
            public bool OverwriteComments { get; set; } = true;
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string TOTPSecret { get; set; } = "";
            public Dictionary<string, string> Ids { get; set; } = new Dictionary<string, string>();
            public string UserAgent { get; set; } = "VRChatBioUpdater/1.0";
            public string _AuthCookie { get; set; } = "";
            public string _TwoFactorAuthCookie { get; set; } = "";
            public List<string> TagUrls { get; set; } = new List<string> { "https://github.com/Bluscream/FewTags/raw/refs/heads/main/usertags.json" };


            public class CustomVariable
            {
                public string Content { get; set; } = "";
                public string VisibleWhen { get; set; } = "";
            }

            public Dictionary<string, CustomVariable> CustomVariables { get; set; } = new Dictionary<string, CustomVariable>();

            // Bio Updater Settings
            public int UpdateInterval { get; set; } = 7200000; // 2 hours
            public int InitialDelay { get; set; } = 5000; // 5 seconds
            public string SteamId { get; set; } = "";
            public string SteamApiKey { get; set; } = "";
            public string SteamAppId { get; set; } = "438100";
            public string BioTemplate { get; set; } = "Relationship: {group1} <3\nAuto Accept: {autojoin}\n{autoinviteprefix}{autoinvite}\n\nReal Rank: {rank}\nFriends: {friends} | Blocked: {blocked} | Muted: {muted}\nTime played: {playtime}\nDate joined: {date_joined}\nLast updated: {now} (every {interval})\nTagged: {tagged_users}/{tags_loaded}\n\nUser ID: {user_id}\nSteam ID: {steam_id}\nOculus ID: {oculus_id}\nPico ID: {pico_id}\nVive ID: {vive_id}";
            public string Separator { get; set; } = "\n-\n";
            public string VrcxDbPath { get; set; } = @"%APPDATA%\VRCX\VRCX.sqlite3";

            [JsonIgnore]
            public string EffectiveBioTemplate
            {
                get
                {
                    try
                    {
                        var txtPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VRChatBioUpdater.txt");
                        if (File.Exists(txtPath))
                        {
                            return File.ReadAllText(txtPath);
                        }
                    }
                    catch { }
                    return BioTemplate;
                }
            }
        }

        public Configuration(FileInfo file)
        {
            this.file = file;
        }
        internal AppConfig LoadConfiguration() => LoadConfiguration(file);
        private AppConfig LoadConfiguration(FileInfo file)
        {
            try
            {
                Console.WriteLine($"Loading {file.FullName}");

                if (!file.Exists)
                {
                    Console.WriteLine($"Configuration file not found. Creating default configuration file at\n\"{file.FullName}\"");
                    var defaultConfig = new AppConfig { };
                    SaveConfiguration(file, defaultConfig, null);

                    Console.WriteLine("Default configuration file created. Please edit it with your credentials and run the application again.");
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    Environment.Exit(0);
                }
                LoadedJson = file.ReadAllText();
                App = JsonConvert.DeserializeObject<AppConfig>(LoadedJson);

                if (App == null)
                {
                    throw new InvalidOperationException("Failed to deserialize configuration file.");
                }

                Console.WriteLine("Configuration loaded successfully.");
                return App;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
                return null; // Unreachable, but required by compiler
            }
        }
        internal bool SaveConfiguration() => SaveConfiguration(file, App, LoadedJson);
        internal bool SaveConfiguration(AppConfig appConfig) => SaveConfiguration(file, appConfig, LoadedJson);
        private bool SaveConfiguration(FileInfo file, AppConfig appConfig, string loadedJson = null)
        {
            try
            {
                var json = JsonConvert.SerializeObject(appConfig, Formatting.Indented);
                if (string.IsNullOrWhiteSpace(loadedJson) || json != loadedJson) {
                    Console.WriteLine($"Saving configuration to \"{file.FullName}\"");
                    file.WriteAllText(json);
                    Console.WriteLine("Configuration saved successfully.");
                    LoadedJson = json;
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration: {ex.Message}");
                return false;
            }
        }

    }
}
