using System;
using System.Linq;
using System.Threading.Tasks;
using VRChat.API.Api;
using VRChat.API.Client;
using static Program;

namespace VRChatBioUpdater
{
    internal class VrcApiClient : ApiClient
    {
        internal VRChat.API.Client.Configuration Configuration { get; set; }
        
        // Re-creating these on access ensures they always use the latest Configuration (which contains our cookies)
        internal AuthenticationApi Auth => new AuthenticationApi(this, this, Configuration);
        internal UsersApi Users => new UsersApi(this, this, Configuration);
        internal PlayermoderationApi Moderations => new PlayermoderationApi(this, this, Configuration);
        internal FavoritesApi Favorites => new FavoritesApi(this, this, Configuration);

        public VrcApiClient() : base() { }
    }
}
