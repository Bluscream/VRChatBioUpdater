using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;

namespace VRChatBioUpdater
{
    internal class VrcxDatabase
    {
        private string _dbPath;

        public VrcxDatabase(string dbPath)
        {
            _dbPath = Environment.ExpandEnvironmentVariables(dbPath);
            // Fallback: if %APPDATA% didn't expand (e.g. single-file publish), resolve manually
            if (_dbPath.Contains("%APPDATA%", StringComparison.OrdinalIgnoreCase))
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _dbPath = _dbPath.Replace("%APPDATA%", appData, StringComparison.OrdinalIgnoreCase);
            }
            Console.WriteLine($"[VRCX DB] Resolved path: {_dbPath}");
        }

        private SqliteConnection GetConnection()
        {
            if (!File.Exists(_dbPath))
            {
                Console.WriteLine($"[VRCX DB] File not found: {_dbPath}");
                return null;
            }
            try
            {
                // Microsoft.Data.Sqlite connection string format
                var connectionString = $"Data Source={_dbPath};Mode=ReadOnly;";
                var connection = new SqliteConnection(connectionString);
                connection.Open();
                return connection;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VRCX DB] Failed to open connection: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private string GetUserPrefix(string userId)
        {
            var prefix = userId.Replace("-", "").Replace("_", "");
            if (char.IsDigit(prefix[0]))
            {
                prefix = "_" + prefix;
            }
            return prefix;
        }

        public long GetTotalPlaytimeSeconds(string userId)
        {
            try
            {
                var prefix = GetUserPrefix(userId);
                using (var conn = GetConnection())
                {
                    if (conn == null) return 0;
                    var tableName = $"{prefix}_activity_sessions_v2";
                    using (var cmd = new SqliteCommand($"SELECT SUM(end_at - start_at) FROM {tableName}", conn))
                    {
                        var result = cmd.ExecuteScalar();
                        return result == DBNull.Value || result == null ? 0 : Convert.ToInt64(result);
                    }
                }
            }
            catch (Exception ex) { 
                Console.WriteLine($"[VRCX DB] Error in GetTotalPlaytimeSeconds: {ex.Message}");
                return 0; 
            }
        }

        public int GetMemosCount()
        {
            try
            {
                using (var conn = GetConnection())
                {
                    if (conn == null) return 0;
                    using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM memos", conn))
                    {
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex) { 
                Console.WriteLine($"[VRCX DB] Error in GetMemosCount: {ex.Message}");
                return 0; 
            }
        }

        public int GetNotesCount(string userId)
        {
            try
            {
                var prefix = GetUserPrefix(userId);
                using (var conn = GetConnection())
                {
                    if (conn == null) return 0;
                    var tableName = $"{prefix}_notes";
                    using (var cmd = new SqliteCommand($"SELECT COUNT(*) FROM {tableName}", conn))
                    {
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch (Exception ex) { 
                Console.WriteLine($"[VRCX DB] Error in GetNotesCount: {ex.Message}");
                return 0; 
            }
        }
        public List<string> GetFavoriteFriends(string groupName)
        {
            var results = new List<string>();
            try
            {
                using (var conn = GetConnection())
                {
                    if (conn == null) return results;
                    using (var cmd = new SqliteCommand("SELECT user_id FROM favorite_friend WHERE group_name = @groupName", conn))
                    {
                        cmd.Parameters.AddWithValue("@groupName", groupName);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                results.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VRCX DB] Error in GetFavoriteFriends: {ex.Message}");
            }
            return results;
        }
    }
}
