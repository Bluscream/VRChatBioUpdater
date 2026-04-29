using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Linq;

namespace VRChatBioUpdater
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal class Configuration
    {
        private FileInfo file;
        private string LoadedJson;
        public AppConfig App { get; set; } = new AppConfig();
        public class TemplateLine
        {
            public string Content { get; set; }
            public string Compact { get; set; }
            public int Priority { get; set; } = 0;
        }

        public class AppConfig
        {
            public bool FetchDetails { get; set; } = true;
            public bool OverwriteComments { get; set; } = true;
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string TOTPSecret { get; set; } = "";
            public string UserAgent { get; set; } = "VRChatBioUpdater/1.0";
            [JsonProperty("AuthCookie")]
            public string AuthCookie { get; set; } = "";
            [JsonProperty("TwoFactorAuthCookie")]
            public string TwoFactorAuthCookie { get; set; } = "";
            
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> TagUrls { get; set; } = new List<string> { "https://github.com/Bluscream/FewTags/raw/refs/heads/main/usertags.json" };

            // Bio Updater Settings
            public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromHours(2);
            public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(5);
            public string SteamId { get; set; } = "";
            public string SteamApiKey { get; set; } = "";
            public string SteamAppId { get; set; } = "438100";
            
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<TemplateLine> Bio { get; set; } = new List<TemplateLine>();
            
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<TemplateLine> Status { get; set; } = new List<TemplateLine>();

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<TemplateLine> Pronouns { get; set; } = new List<TemplateLine>();

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Links { get; set; } = new List<string>();
            
            public string BioSeparator { get; set; } = "\n-\n";
            public string StatusSeparator { get; set; } = " | ";
            public string VrcxDbPath { get; set; } = @"%APPDATA%\VRCX\VRCX.sqlite3";
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
                return null;
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
