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

            Console.WriteLine($"[Debug] Client Constructor: AuthCookie Provided={!string.IsNullOrEmpty(authCookie)}, 2FAProvided={!string.IsNullOrEmpty(twoFactorAuthCookie)}");
            LoginAsync(totpSecret).GetAwaiter().GetResult();
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
                    // Clear invalid cookies
                    ApiConfiguration.AddApiKey("auth", null);
                    ApiConfiguration.AddApiKey("twoFactorAuth", null);

                    if (!string.IsNullOrEmpty(totpSecret))
                    {
                        Console.WriteLine($"[VRChat API] Session expired or invalid. Attempting fresh login with TOTP... (Secret length: {totpSecret?.Length ?? 0})");
                        // Performing a call to GetCurrentUser without cookies will trigger the initial auth challenge
                        try { await Auth.GetCurrentUserAsync(); } catch { } 

                        var totp = new Totp(Base32Encoding.ToBytes(totpSecret));
                        var code = totp.ComputeTotp();
                        try 
                        {
                            await Auth.Verify2FAAsync(new TwoFactorAuthCode(code));
                            
                            // Try to extract cookies from the header if the SDK didn't automatically add them to config
                            var currentUserResponse = await Auth.GetCurrentUserWithHttpInfoAsync();
                            var currentUser = currentUserResponse.Data;

                            if (currentUserResponse.Headers.TryGetValue("Set-Cookie", out var setCookie)) {
                                foreach (var cookie in setCookie) {
                                    if (cookie.StartsWith("auth=")) {
                                        var val = cookie.Split(';')[0].Split('=')[1];
                                        ApiConfiguration.AddApiKey("auth", val);
                                    } else if (cookie.StartsWith("twoFactorAuth=")) {
                                        var val = cookie.Split(';')[0].Split('=')[1];
                                        ApiConfiguration.AddApiKey("twoFactorAuth", val);
                                    }
                                }
                            }

                            Console.WriteLine($"Logged in as {currentUser.DisplayName}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[VRChat API] 2FA Verification failed: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[VRChat API] 2FA required but no TOTP secret provided and cookies are invalid.");
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
