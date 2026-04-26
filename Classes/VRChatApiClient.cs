using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using OtpNet;
using ApiConfig = VRChat.API.Client.Configuration;

namespace VRChatBioUpdater
{
    internal class VRChatApiClient
    {
        public UsersApi Users { get; }
        public FavoritesApi Favorites { get; }
        public PlayermoderationApi Playermoderations { get; }
        public AuthenticationApi Auth { get; }
        public ApiConfig ApiConfiguration { get; }

        public string AuthCookie => ApiConfiguration.GetApiKeyWithPrefix("auth");
        public string TwoFactorAuthCookie => ApiConfiguration.GetApiKeyWithPrefix("twoFactorAuth");

        public VRChatApiClient(string username, string password, string totpSecret = null, string authCookie = null, string twoFactorAuthCookie = null)
        {
            ApiConfiguration = new ApiConfig
            {
                Username = username,
                Password = password,
                UserAgent = "VRChatBioUpdater/1.0.0"
            };

            if (!string.IsNullOrEmpty(authCookie))
                ApiConfiguration.AddApiKey("auth", authCookie);
            if (!string.IsNullOrEmpty(twoFactorAuthCookie))
                ApiConfiguration.AddApiKey("twoFactorAuth", twoFactorAuthCookie);

            Users = new UsersApi(ApiConfiguration);
            Favorites = new FavoritesApi(ApiConfiguration);
            Playermoderations = new PlayermoderationApi(ApiConfiguration);
            Auth = new AuthenticationApi(ApiConfiguration);

            if (string.IsNullOrEmpty(authCookie))
            {
                LoginAsync(totpSecret).GetAwaiter().GetResult();
            }
        }

        private async Task LoginAsync(string totpSecret)
        {
            try
            {
                var currentUser = await Auth.GetCurrentUserAsync();
                Console.WriteLine($"Logged in as {currentUser.DisplayName}");
            }
            catch (ApiException e)
            {
                if (e.ErrorContent.ToString().Contains("2fa") || e.ErrorCode == 401)
                {
                    if (!string.IsNullOrEmpty(totpSecret))
                    {
                        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));
                        var code = totp.ComputeTotp();
                        try 
                        {
                            await Auth.Verify2FAAsync(new TwoFactorAuthCode(code));
                            // After verification, we might need to fetch the user again to ensure cookies are set
                            await Auth.GetCurrentUserAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[VRChat API] 2FA Verification failed: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[VRChat API] 2FA required but no TOTP secret provided.");
                    }
                }
                else
                {
                    Console.WriteLine($"[VRChat API] Login failed: {e.Message}");
                }
            }
        }
    }
}
