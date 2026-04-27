using System;
using System.Linq;
using System.Threading.Tasks;
using VRChat.API.Client;
using VRChat.API.Model;
using VRChat.API.Api;

namespace VRChatBioUpdater
{
    internal class VRChatApiClient
    {
        public IVRChat VrcClient { get; }
        
        // Compatibility properties for Program.cs (Mapping to IVRChat interfaces)
        public IAuthenticationApi Auth => VrcClient.Authentication;
        public IPlayermoderationApi Playermoderations => VrcClient.Moderations;
        public IFavoritesApi Favorites => VrcClient.Favorites;
        public IUsersApi Users => VrcClient.Users;

        // Cookie extraction for persistence in Configuration
        public string AuthCookie => VrcClient.GetCookies().FirstOrDefault(c => c.Name == "auth")?.Value;
        public string TwoFactorAuthCookie => VrcClient.GetCookies().FirstOrDefault(c => c.Name == "twoFactorAuth")?.Value;

        public VRChatApiClient(string username, string password, string totpSecret = null, string authCookie = null, string twoFactorAuthCookie = null)
        {
            var builder = new VRChatClientBuilder()
                .WithUsername(username)
                .WithPassword(password)
                .WithTwoFactorSecret(totpSecret)
                .WithApplication("VRChatBioUpdater", "1.0.0", "bluscream@users.noreply.github.com");

            if (!string.IsNullOrEmpty(authCookie))
                builder.WithAuthCookie(authCookie, twoFactorAuthCookie);

            VrcClient = builder.Build();

            // Workaround for potential builder bug: ensure cookies are set directly on configuration
            if (!string.IsNullOrEmpty(authCookie))
                VrcClient.Configuration.AddApiKeyPrefix("auth", authCookie);
            if (!string.IsNullOrEmpty(twoFactorAuthCookie))
                VrcClient.Configuration.AddApiKeyPrefix("twoFactorAuth", twoFactorAuthCookie);

            Console.WriteLine($"[Debug] Client Initialized using Native Fluent Builder. AuthCookie Provided={!string.IsNullOrEmpty(authCookie)}");
            
            // LoginAsync handles TOTP challenge automatically if secret is provided
            try
            {
                // We use LoginAsync to handle the multi-step authentication process (2FA check -> Verify -> GetUser)
                var currentUser = VrcClient.LoginAsync().GetAwaiter().GetResult();
                if (currentUser != null)
                {
                    Console.WriteLine($"[VRChat API] Successfully logged in as {currentUser.DisplayName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VRChat API] Initial login failed: {ex.Message}");
            }
        }
    }
}
