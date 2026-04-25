using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace VRChatBioUpdater
{
    internal class VrcxDatabase
    {
        private string _dbPath;

        public VrcxDatabase(string dbPath)
        {
            _dbPath = Environment.ExpandEnvironmentVariables(dbPath);
        }

        private SQLiteConnection GetConnection()
        {
            if (!File.Exists(_dbPath)) return null;
            var connectionString = $"Data Source={_dbPath};Version=3;ReadOnly=True;";
            var connection = new SQLiteConnection(connectionString);
            connection.Open();
            return connection;
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

        public int GetTaggedUsersCount()
        {
            try
            {
                using (var conn = GetConnection())
                {
                    if (conn == null) return 0;
                    using (var cmd = new SQLiteCommand("SELECT COUNT(DISTINCT avatar_id) FROM avatar_tags", conn))
                    {
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch { return 0; }
        }

        public int GetTotalTagsCount()
        {
            try
            {
                using (var conn = GetConnection())
                {
                    if (conn == null) return 0;
                    using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM avatar_tags", conn))
                    {
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch { return 0; }
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
                    using (var cmd = new SQLiteCommand($"SELECT SUM(end_at - start_at) FROM {tableName}", conn))
                    {
                        var result = cmd.ExecuteScalar();
                        return result == DBNull.Value ? 0 : Convert.ToInt64(result);
                    }
                }
            }
            catch { return 0; }
        }

        public int GetMemosCount()
        {
            try
            {
                using (var conn = GetConnection())
                {
                    if (conn == null) return 0;
                    using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM memos", conn))
                    {
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch { return 0; }
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
                    using (var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM {tableName}", conn))
                    {
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            catch { return 0; }
        }
    }
}
