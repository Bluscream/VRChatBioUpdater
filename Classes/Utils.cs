using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
    }
}
