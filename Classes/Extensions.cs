using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Web;
using Humanizer;

namespace VRChatBioUpdater
{
    [SupportedOSPlatform("windows")]
    static class Extensions
    {
        #region Process
        internal static string GetCommandLine(this Process process)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id))
                using (var objects = searcher.Get())
                {
                    var result = objects.Cast<ManagementBaseObject>().SingleOrDefault();
                    return result?["CommandLine"]?.ToString() ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }
        #endregion Process
        #region CookieContainer
        internal static IEnumerable<Cookie> GetAllCookies(this CookieContainer c)
        {
            if (c == null) return Enumerable.Empty<Cookie>();
            
            var cookies = new List<Cookie>();
            try
            {
                var domainTableField = c.GetType().GetField("m_domainTable", BindingFlags.Instance | BindingFlags.NonPublic);
                if (domainTableField == null) return cookies;
                var k = domainTableField.GetValue(c) as Hashtable;
                if (k == null) return cookies;
                
                foreach (DictionaryEntry element in k)
                {
                    if (element.Value == null) continue;
                    var listField = element.Value.GetType().GetField("m_list", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (listField == null) continue;
                    var l = listField.GetValue(element.Value) as SortedList;
                    if (l == null) continue;
                    
                    foreach (var e in l)
                    {
                        if (e == null) continue;
                        var entry = e as DictionaryEntry?;
                        if (!entry.HasValue) continue;
                        var cl = entry.Value.Value as CookieCollection;
                        if (cl == null) continue;
                        
                        foreach (Cookie fc in cl)
                        {
                            if (fc != null) cookies.Add(fc);
                        }
                    }
                }
            }
            catch
            {
                // Reflection-based access failed, return empty collection
            }
            return cookies;
        }
        #endregion CookieContainer
        #region Uri
        internal static Uri AddQuery(this Uri uri, string key, string value, bool encode = true)
        {
            var uriBuilder = new UriBuilder(uri);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            if (encode)
            {
                query[key] = value;
                uriBuilder.Query = query.ToString();
            }
            else
            {
                var queryDict = query.AllKeys.Where(k => k != null).ToDictionary(k => k, k => query[k]);
                queryDict[key] = value;
                var queryString = string.Join("&", queryDict.Select(kvp => $"{HttpUtility.UrlEncode(kvp.Key)}={(kvp.Key == key ? value : HttpUtility.UrlEncode(kvp.Value))}"));
                uriBuilder.Query = queryString;
            }
            return uriBuilder.Uri;
        }
        #endregion Uri
        #region DirectoryInfo
        internal static DirectoryInfo Combine(this DirectoryInfo dir, params string[] paths)
        {
            var final = dir.FullName;
            foreach (var path in paths)
            {
                final = Path.Combine(final, path);
            }
            return new DirectoryInfo(final);
        }
        internal static bool IsEmpty(this DirectoryInfo directory)
        {
            return !Directory.EnumerateFileSystemEntries(directory.FullName).Any();
        }
        #endregion
        #region FileInfo

        internal static FileInfo CombineFile(this DirectoryInfo dir, params string[] paths)
        {
            var final = dir.FullName;
            foreach (var path in paths)
            {
                final = Path.Combine(final, path);
            }
            return new FileInfo(final);
        }
        internal static FileInfo Combine(this FileInfo file, params string[] paths)
        {
            var basePath = file.DirectoryName ?? Environment.CurrentDirectory;
            var final = basePath;
            foreach (var path in paths)
            {
                final = Path.Combine(final, path);
            }
            return new FileInfo(final);
        }
        internal static string FileNameWithoutExtension(this FileInfo file)
        {
            return Path.GetFileNameWithoutExtension(file.Name);
        }
        internal static bool WriteAllText(this FileInfo file, string content)
        {
            if (file.Directory != null && !file.Directory.Exists) file.Directory.Create();
            try
            {
                File.WriteAllText(file.FullName, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to file {file.FullName}: {ex.Message}");
                return false;
            }
            return true;
        }
        internal static string ReadAllText(this FileInfo file)
        {
            if (file.Directory != null && !file.Directory.Exists) file.Directory.Create();
            if (!file.Exists) return string.Empty;
            return File.ReadAllText(file.FullName);
        }
        #endregion
        #region Time
        internal static string ToHuman(this TimeSpan span)
        {
            if (span.TotalSeconds < 0) return "now";
            return span.Humanize();
        }

        internal static string ToHuman(this long ms)
        {
            if (ms <= 0) return "now";
            return TimeSpan.FromMilliseconds(ms).ToHuman();
        }

        internal static string ToPrettyString(this TimeSpan span) => span.ToHuman();
        internal static string ToPrettyString(this long ms) => ms.ToHuman();

        internal static string GetHighestRank(this IEnumerable<string> tags)
        {
            if (tags == null) return "User";
            
            // Priority order: Admin -> Legendary (Veteran) -> Veteran (Trusted) -> Trusted (Known) -> Known (User) -> Basic (New User) -> Visitor
            if (tags.Any(t => t.Equals("admin", StringComparison.OrdinalIgnoreCase))) return "Admin";
            if (tags.Any(t => t.EndsWith("_trust_legend", StringComparison.OrdinalIgnoreCase))) return "Veteran User";
            if (tags.Any(t => t.EndsWith("_trust_veteran", StringComparison.OrdinalIgnoreCase))) return "Trusted User";
            if (tags.Any(t => t.EndsWith("_trust_trusted", StringComparison.OrdinalIgnoreCase))) return "Known User";
            if (tags.Any(t => t.EndsWith("_trust_known", StringComparison.OrdinalIgnoreCase))) return "User";
            if (tags.Any(t => t.EndsWith("_trust_basic", StringComparison.OrdinalIgnoreCase))) return "New User";
            
            return "Visitor";
        }
        #endregion
    }
}
