using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Reddit_Wallpaper_Changer
{
    public class Database
    {
        private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.FFF";
        private const string DbName = "Reddit-Wallpaper-Changer.sqlite";

        private readonly string _dbPath, _connectionString;

        public Database(string appDataPath)
        {
            _dbPath = $@"{appDataPath}\{DbName}";
            _connectionString = $"Data Source={_dbPath};Version=3;";
        }

        private enum Statuses
        {
            DoNotUse,
            Blacklist,
            Favourite
        }

        //======================================================================
        // Create the SQLite Blacklist database
        //======================================================================
        public void InitialiseDatabase()
        {
            if (!File.Exists(_dbPath))
            {
                try
                {
                    SQLiteConnection.CreateFile(_dbPath);

                    Logger.Instance.LogMessageToFile($"Database '{DbName}' created successfully: {_dbPath}", LogLevel.Information);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessageToFile($"Unexpected error creating database: {ex.Message}", LogLevel.Error);
                    throw ex;
                }
            }

            try
            {
                using (var con = GetNewOpenConnection())
                {
                    Logger.Instance.LogMessageToFile($"Successfully connected to database '{DbName}'", LogLevel.Information);

                    Logger.Instance.LogMessageToFile("Ensuring tables and indexes exist", LogLevel.Information);

                    var sql = $@"
                        BEGIN;
                            CREATE TABLE IF NOT EXISTS version (version STRING, date STRING);
                            CREATE TABLE IF NOT EXISTS blacklist (thumbnail STRING, title STRING, threadid STRING, url STRING, date STRING);
                            CREATE INDEX IF NOT EXISTS idx_blacklist ON blacklist (url);
                            CREATE INDEX IF NOT EXISTS idx_blacklist_date ON blacklist (datetime(date) DESC);
                            CREATE TABLE IF NOT EXISTS favourites (thumbnail STRING, title STRING, threadid STRING, url STRING, date STRING);
                            CREATE INDEX IF NOT EXISTS idx_favourites ON favourites (url);
                            CREATE INDEX IF NOT EXISTS idx_favourites_date ON favourites (datetime(date) DESC);
                            CREATE TABLE IF NOT EXISTS history (thumbnail STRING, title STRING, threadid STRING, url STRING, date STRING);
                            CREATE INDEX IF NOT EXISTS idx_history ON history (url);
                            CREATE INDEX IF NOT EXISTS idx_history_date ON history (datetime(date) DESC);

                            CREATE TABLE IF NOT EXISTS versionlog (version TEXT PRIMARY KEY NOT NULL, date TEXT NOT NULL);

                            INSERT INTO versionlog (version, date)
                            SELECT version, MIN(date)
                            FROM version
                            GROUP BY version;

                            DROP TABLE version;

                            CREATE TABLE IF NOT EXISTS redditlink
                            (
                                threadid TEXT PRIMARY KEY NOT NULL,
                                title TEXT NOT NULL,
                                url TEXT NOT NULL,
                                thumbnail TEXT,
                                UNIQUE(threadid, title, url)
                            );
                            CREATE TABLE IF NOT EXISTS historylog
                            (
                                threadid TEXT NOT NULL REFERENCES redditlink(threadid),
                                date TEXT NOT NULL,
                                PRIMARY KEY(threadid, date)
                            );
                            CREATE TABLE IF NOT EXISTS statuses
                            (
                                threadid TEXT NOT NULL REFERENCES redditlink(threadid),
                                status INT NOT NULL CHECK(status IN ({(int)Statuses.Blacklist}, {(int)Statuses.Favourite})),
                                date TEXT NOT NULL,
                                PRIMARY KEY(threadid, status)
                            );

                            WITH alltables_cte AS
                            (
	                            SELECT threadid, title, url, thumbnail, date
	                            FROM blacklist
	
	                            UNION ALL
	
	                            SELECT threadid, title, url, thumbnail, date
	                            FROM history
	
	                            UNION ALL
	
	                            SELECT threadid, title, url, thumbnail, date
	                            FROM favourites
                            ),
                            row_nums AS
                            (
	                            SELECT
		                            threadid,
		                            title,
		                            url,
		                            thumbnail,
	                                ROW_NUMBER() OVER
	                                (
		                                PARTITION BY threadid
		                                ORDER BY
                                            title DESC,
                                            date DESC
	                                ) AS row_num
	                            FROM alltables_cte
                            )
                            INSERT INTO redditlink (threadid, title, url, thumbnail)
                            SELECT threadid, title, url, thumbnail
                            FROM row_nums
                            WHERE row_num = 1;

                            INSERT INTO historylog (threadid, date)
                            SELECT DISTINCT threadid, date
                            FROM history;

                            DROP TABLE history;

                            WITH statuses_cte AS
                            (
	                            SELECT threadid, 1 AS status, date
	                            FROM blacklist

	                            UNION ALL
	
	                            SELECT threadid, 2, date
	                            FROM favourites
                            ),
                            row_nums AS
                            (
	                            SELECT
	                                threadid,
		                            status,
		                            date,
		                            ROW_NUMBER() OVER
		                            (
			                            PARTITION BY
				                            threadid,
				                            status
			                            ORDER BY date DESC
		                            ) AS row_num
	                            FROM statuses_cte
                            )
                            INSERT INTO statuses (threadid, status, date)
                            SELECT threadid, status, date
                            FROM row_nums
                            WHERE row_num = 1;

                            DROP TABLE blacklist;
                            DROP TABLE favourites;

                            CREATE INDEX IF NOT EXISTS idx_statuses ON statuses (threadid, status, date DESC);
                            CREATE INDEX IF NOT EXISTS idx_historylog ON historylog (threadid, date DESC);
                        END;

                        VACUUM;";

                    using (var command = new SQLiteCommand(sql, con))
                    {
                        command.ExecuteNonQuery();
                    }

                    Logger.Instance.LogMessageToFile($"Successfully created/validated tables and indexes", LogLevel.Information);

                    AddVersion();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error creating database: {ex.Message}", LogLevel.Error);
                throw ex;
            }
        }

        //======================================================================
        // One off task to migrate old Blacklist.xml file 
        //======================================================================
        public async Task MigrateOldBlacklistAsync()
        {
            Logger.Instance.LogMessageToFile($"Migrating Blacklist.xml to '{DbName}'. This is a one off task...", LogLevel.Information);

            var fullFilePath = $@"{Settings.Default.AppDataPath}\Blacklist.xml";

            if (File.Exists(fullFilePath))
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(fullFilePath);

                    foreach (XmlNode xn in doc.SelectNodes("Blacklisted/Wallpaper"))
                    {
                        try
                        {
                            var redditLink = new RedditLink(
                                xn["Title"].InnerText,
                                xn["ThreadID"].InnerText,
                                xn["URL"].InnerText);

                            Logger.Instance.LogMessageToFile($"Migrating: {redditLink.Title}, {redditLink.ThreadId}, {redditLink.Url}", LogLevel.Information);

                            await InsertWallpaperWithStatusAsync(redditLink, Statuses.Blacklist);

                            Logger.Instance.LogMessageToFile($"Successfully migrated: {redditLink.Title}, {redditLink.ThreadId}, {redditLink.Url}", LogLevel.Information);
                        }
                        catch (WebException ex)
                        {
                            Logger.Instance.LogMessageToFile($"Unexpected error migrating: {ex.Message}", LogLevel.Error);
                        }
                    }

                    Logger.Instance.LogMessageToFile("Migration to database completed successfully!", LogLevel.Information);

                    File.Delete(fullFilePath);

                    Logger.Instance.LogMessageToFile("Blacklist.xml deleted.", LogLevel.Information);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessageToFile("Error migrating: " + ex.Message, LogLevel.Error);
                }
            }
            else
            {
                Logger.Instance.LogMessageToFile("No blacklist.xml file to migrate", LogLevel.Information);
            }

            Settings.Default.dbMigrated = true;
            Settings.Default.Save();
        }

        //======================================================================
        // Add wallpaper to blacklisted
        //======================================================================
        public async Task<bool> BlacklistWallpaperAsync(RedditLink redditLink)
        {
            try
            {
                await InsertWallpaperWithStatusAsync(redditLink, Statuses.Blacklist).ConfigureAwait(false);

                Logger.Instance.LogMessageToFile($"Wallpaper blacklisted! Title: {redditLink.Title}, Thread ID: {redditLink.ThreadId}, URL: {redditLink.Url}", LogLevel.Information);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error blacklisting wallpaper: {ex.Message}", LogLevel.Warning);
                return false;
            }
        }

        //======================================================================
        // Add wallpaper to favourites
        //======================================================================
        public async Task<bool> FaveWallpaperAsync(RedditLink redditLink)
        {
            try
            {
                await InsertWallpaperWithStatusAsync(redditLink, Statuses.Favourite).ConfigureAwait(false);

                Logger.Instance.LogMessageToFile($"Wallpaper added to favourites! Title: {redditLink.Title}, Thread ID: {redditLink.ThreadId}, URL: {redditLink.Url}", LogLevel.Information);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error favouriting wallpaper: {ex.Message}", LogLevel.Warning);
                return false;
            }
        }

        //======================================================================
        // Add version
        //======================================================================
        private void AddVersion()
        {
            try
            {
                var currentVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
                var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF");

                using (var connection = GetNewOpenConnection())
                using (var command = new SQLiteCommand(
                    "INSERT INTO versionlog (version, date) " +
                    "VALUES (@version, @dateTime) " +
                    "ON CONFLICT (version) " +
                    "DO NOTHING", connection))
                {
                    command.Parameters.AddWithValue("version", currentVersion);
                    command.Parameters.AddWithValue("dateTime", dateTime);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error adding version to database: {ex.Message}", LogLevel.Warning);
            }
        }

        //======================================================================
        // Add wallpaper to history
        //======================================================================
        public void AddWallpaperToHistory(RedditLink redditLink, string thumbnail)
        {
            try
            {
                var now = DateTime.Now.ToString(DateTimeFormat);

                using (var connection = GetNewOpenConnection())
                using (var command = new SQLiteCommand(
                    "BEGIN; " +
                        "INSERT INTO redditlink (threadid, title, url, thumbnail) " +
                        "VALUES (@threadid, @title, @url, @thumbnail) " +
                        "ON CONFLICT (threadid) " +
                        "DO UPDATE SET thumbnail = excluded.thumbnail; " +

                        "INSERT INTO historylog (threadid, date) " +
                        "VALUES (@threadid, @dateTime); " +
                    "END;", connection))
                {
                    command.Parameters.AddWithValue("@thumbnail", thumbnail);
                    command.Parameters.AddWithValue("@title", redditLink.Title);
                    command.Parameters.AddWithValue("@threadid", redditLink.ThreadId);
                    command.Parameters.AddWithValue("@url", redditLink.Url);
                    command.Parameters.AddWithValue("@dateTime", now);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error adding wallpaper to history: {ex.Message}", LogLevel.Warning);

                throw;
            }
        }

        //======================================================================
        // Remove wallpaper from blacklist
        //======================================================================
        public void RemoveFromBlacklist(string threadId)
        {
            try
            {
                DeleteStatusByThreadId(threadId, Statuses.Blacklist);

                Logger.Instance.LogMessageToFile($"Wallpaper removed from blacklist! Thread ID: {threadId}", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error removing wallpaper from Blacklist: {ex.Message}", LogLevel.Warning);
            }
        }

        //======================================================================
        // Remove wallpaper from favourites
        //======================================================================
        public void RemoveFromFavourites(string threadId)
        {
            try
            {
                DeleteStatusByThreadId(threadId, Statuses.Favourite);

                Logger.Instance.LogMessageToFile($"Wallpaper removed from favourites! Thread ID: {threadId}", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error removing wallpaper from favourites: {ex.Message}", LogLevel.Warning);
            }
        }

        //======================================================================
        // Remove wallpaper from History
        //======================================================================
        public void RemoveFromHistory(string threadId)
        {
            try
            {
                DeleteFromHistory(threadId);

                Logger.Instance.LogMessageToFile($"Wallpaper removed from history! Thread ID: {threadId}", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error removing wallpaper from history: {ex.Message}", LogLevel.Warning);
            }
        }

        //======================================================================
        // Retrieve history from the database
        //======================================================================
        public IEnumerable<RedditLink> GetFromHistory()
        {
            try
            {
                return GetHistoryImagesFromDatabase();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error retrieving History from database: {ex.Message}", LogLevel.Warning);
                return new List<RedditLink>();
            }
        }

        //======================================================================
        // Retrieve blacklist from the database
        //======================================================================
        public IEnumerable<RedditLink> GetFromBlacklist()
        {
            try
            {
                return GetRedditImagesByStatus(Statuses.Blacklist);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error retrieving Blacklist from database: {ex.Message}", LogLevel.Warning);
                return new List<RedditLink>();
            }
        }

        //======================================================================
        // Retrieve favourites from the database
        //======================================================================
        public IEnumerable<RedditLink> GetFromFavourites()
        {
            try
            {
                return GetRedditImagesByStatus(Statuses.Favourite);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error retrieving favourites from database: {ex.Message}", LogLevel.Warning);
                return new List<RedditLink>();
            }
        }

        //======================================================================
        // Check for blacklisted wallpaper
        //======================================================================
        public bool IsBlacklisted(string threadId)
        {
            try
            {
                using (var con = GetNewOpenConnection())
                using (var command = new SQLiteCommand(
                    "SELECT " +
                        "CASE " +
                            "WHEN EXISTS " +
                            "(" +
                                "SELECT * " +
                                "FROM statuses " +
                                "WHERE threadid = @threadid " +
                               $"AND status = {(int)Statuses.Blacklist}" +
                            ") " +
                            "THEN 1 " +
                            "ELSE 0 " +
                        "END", con))

                {
                    command.Parameters.AddWithValue("@threadid", threadId);

                    return Convert.ToInt32(command.ExecuteScalar()) == 1;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error checking for Blacklist entry: {ex.Message}", LogLevel.Warning);
                return false;
            }
        }

        public bool WipeFavourites()
            => WipeStatusesTable(Statuses.Favourite);

        public bool WipeBlacklist()
            => WipeStatusesTable(Statuses.Blacklist);

        //======================================================================
        // Delete all values from table
        //======================================================================
        private bool WipeStatusesTable(Statuses status)
        {
            try
            {
                using (var con = GetNewOpenConnection())
                using (var command = new SQLiteCommand(
                    "BEGIN; " +
                        "DELETE " +
                        "FROM statuses " +
                        "WHERE status = @status; " +

                        "DELETE " +
                        "FROM redditlink " +
                        "WHERE NOT EXISTS " +
                        "(" +
                            "SELECT 1 " +
                            "FROM historylog " +
                            "WHERE redditlink.threadid = historylog.threadid " +

                            "UNION ALL " +

                            "SELECT 1 " +
                            "FROM statuses " +
                            "WHERE redditlink.threadid = statuses.threadid " +
                            "AND status <> @status" +
                        "); " +
                    "END;", con))
                {
                    command.Parameters.AddWithValue("@status", (int)status);

                    command.ExecuteNonQuery();
                }

                Logger.Instance.LogMessageToFile($"All {status} contents have been successfully deleted!", LogLevel.Information);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error deleting {status} data: {ex.Message}", LogLevel.Warning);
                return false;
            }
        }

        //======================================================================
        // Delete all values from history table
        //======================================================================
        public bool WipeHistory()
        {
            try
            {
                using (var con = GetNewOpenConnection())
                using (var command = new SQLiteCommand(
                    "BEGIN; " +
                        "DELETE " +
                        "FROM historylog; " +

                        "DELETE " +
                        "FROM redditlink " +
                        "WHERE NOT EXISTS " +
                        "(" +
                            "SELECT * " +
                            "FROM statuses " +
                            "WHERE redditlink.threadid = statuses.threadid" +
                        "); " +
                    "END;", con))
                {
                    command.ExecuteNonQuery();
                }

                Logger.Instance.LogMessageToFile($"All history contents have been successfully deleted!", LogLevel.Information);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error deleting data from history: {ex.Message}", LogLevel.Warning);
                return false;
            }
        }

        //======================================================================
        // Backup database
        //======================================================================
        public bool BackupDatabase(string backupPath)
        {
            try
            {
                var fullBackupPath = Path.Combine(backupPath, DbName);

                Logger.Instance.LogMessageToFile($"Backing up database to: {fullBackupPath}", LogLevel.Information);

                File.Copy(_dbPath, fullBackupPath, true);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error backing up database: {ex.Message}", LogLevel.Error);

                return false;
            }
        }

        //======================================================================
        // Restore sqlite database
        //======================================================================
        public bool RestoreDatabase(string restoreSource)
        {
            try
            {
                Logger.Instance.LogMessageToFile($"Restoring database to: {_dbPath}", LogLevel.Information);

                File.Copy(restoreSource, _dbPath, true);

                InitialiseDatabase();

                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error restoring database: {ex.Message}", LogLevel.Error);

                return false;
            }
        }

        //======================================================================
        // Generate thumbnails for History
        //======================================================================
        public async Task BuildThumbnailCacheAsync()
        {
            try
            {
                Logger.Instance.LogMessageToFile("Updating wallpaper thumbnail cache.", LogLevel.Information);

                var threadThumbnails = GetAllThumbnailsFromDatabase();

                foreach (var (ThreadId, Thumbnail, Url) in threadThumbnails)
                {
                    if (string.IsNullOrWhiteSpace(Thumbnail))
                    {
                        var updatedThumbnail = await HelperMethods.GetThumbnailAsync(Url).ConfigureAwait(false);

                        if (string.IsNullOrWhiteSpace(updatedThumbnail))
                            continue;

                        SaveThumbnailToDatabase(ThreadId, updatedThumbnail);
                    }

                    HelperMethods.SaveToThumbnailCache(ThreadId, Thumbnail);
                }

                Logger.Instance.LogMessageToFile("Wallpaper thumbnail cache updated.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Error updating Wallpaper thumbnail cache: {ex.Message}", LogLevel.Warning);
            }
        }

        private IEnumerable<(string ThreadId, string Thumbnail, string Url)> GetAllThumbnailsFromDatabase()
        {
            var threadThumbnails = new List<(string, string, string)>();

            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand(
                "SELECT thumbnail, threadid, url " +
                "FROM redditlink", connection))
            using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
            {
                if (!reader.HasRows)
                    return threadThumbnails;

                while (reader.Read())
                {
                    var thumbnail = reader.GetFieldValue<string>(0);    // thumbnail
                    var threadId = reader.GetFieldValue<string>(1);     // threadid
                    var url = reader.GetFieldValue<string>(2);          // url

                    threadThumbnails.Add((threadId, thumbnail, url));
                }
            }

            return threadThumbnails;
        }

        private void SaveThumbnailToDatabase(string threadId, string thumbnail)
        {
            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand(
                "UPDATE redditlink " +
                "SET thumbnail = @thumbnail " +
                "WHERE threadid = @threadid", connection))
            {
                command.Parameters.AddWithValue("@threadid", threadId);
                command.Parameters.AddWithValue("@thumbnail", thumbnail);

                command.ExecuteNonQuery();
            }
        }

        private IEnumerable<RedditLink> GetHistoryImagesFromDatabase()
        {
            var imageList = new List<RedditLink>();

            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand(
                "SELECT " +
                    "redditlink.title, " +
                    "redditlink.threadid, " +
                    "redditlink.url " +
                "FROM redditlink " +
                "JOIN historylog " +
                "ON redditlink.threadid = historylog.threadid " +
                "ORDER BY date DESC", connection))
            using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
            {
                if (!reader.HasRows)
                    return imageList;

                while (reader.Read())
                {
                    var redditLink = new RedditLink(
                        reader.GetFieldValue<string>(0),    // title
                        reader.GetFieldValue<string>(1),    // threadid
                        reader.GetFieldValue<string>(2));   // url   

                    imageList.Add(redditLink);
                }
            }

            return imageList;
        }

        private IEnumerable<RedditLink> GetRedditImagesByStatus(Statuses status)
        {
            var imageList = new List<RedditLink>();

            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand(
                "SELECT " +
                    "redditlink.title, " +
                    "redditlink.threadid, " +
                    "redditlink.url " +
                "FROM redditlink " +
                "JOIN statuses " +
                "ON redditlink.threadid = statuses.threadid " +
                "WHERE statuses.status = @status " +
                "ORDER BY date DESC", connection))
            {
                command.Parameters.AddWithValue("@status", (int)status);

                using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    if (!reader.HasRows)
                        return imageList;

                    while (reader.Read())
                    {
                        var redditLink = new RedditLink(
                            reader.GetFieldValue<string>(0),    // title
                            reader.GetFieldValue<string>(1),    // threadid
                            reader.GetFieldValue<string>(2));   // url

                        imageList.Add(redditLink);
                    }
                }
            }

            return imageList;
        }

        private void DeleteFromHistory(string threadId)
        {
            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand(
                "BEGIN; " +
                    "DELETE " +
                    "FROM historylog " +
                    "WHERE threadid = @threadid; " +

                    "DELETE " +
                    "FROM redditlink " +
                    "WHERE threadid = @threadid " +
                    "AND NOT EXISTS " +
                    "(" +
                        "SELECT * " +
                        "FROM statuses " +
                        "WHERE threadid = @threadid" +
                    "); " +
                "END;", connection))
            {
                command.Parameters.AddWithValue("@threadid", threadId);

                command.ExecuteNonQuery();
            }
        }

        private void DeleteStatusByThreadId(string threadid, Statuses status)
        {
            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand(
                "BEGIN; " +
                    "DELETE " +
                    "FROM statuses " +
                    "WHERE threadid = @threadid " +
                    "AND status = @status; " +

                    "DELETE " +
                    "FROM redditlink " +
                    "WHERE threadid = @threadid " +
                    "AND NOT EXISTS " +
                    "(" +
                        "SELECT 1 " +
                        "FROM historylog " +
                        "WHERE threadid = @threadid " +

                        "UNION ALL " +
                        
                        "SELECT 1 " +
                        "FROM statuses " +
                        "WHERE threadid = @threadid " +
                        "AND status <> @status" +
                    "); " +
                "END;", connection))
            {
                command.Parameters.AddWithValue("@threadid", threadid);
                command.Parameters.AddWithValue("@status", (int)status);

                command.ExecuteNonQuery();
            }
        }

        private async Task InsertWallpaperWithStatusAsync(RedditLink redditLink, Statuses status)
        {
            var thumbnail = await HelperMethods.GetThumbnailAsync(redditLink.Url).ConfigureAwait(false);
            var now = DateTime.Now.ToString(DateTimeFormat);

            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand(
                "BEGIN; " +
                    "INSERT INTO redditlink (threadid, title, url, thumbnail) " +
                    "VALUES (@threadid, @title, @url, @thumbnail) " +
                    "ON CONFLICT (threadid, title, url) " +
                    "DO UPDATE SET thumbnail = excluded.thumbnail; " +

                    "INSERT INTO statuses (threadid, status, date) " +
                    "VALUES (@threadid, @status, @dateTime) " +
                    "ON CONFLICT (threadid, status) " +
                    "DO UPDATE SET date = excluded.date; " +
                "END;", connection))
            {
                command.Parameters.AddWithValue("@thumbnail", thumbnail);
                command.Parameters.AddWithValue("@title", redditLink.Title);
                command.Parameters.AddWithValue("@threadid", redditLink.ThreadId);
                command.Parameters.AddWithValue("@url", redditLink.Url);
                command.Parameters.AddWithValue("@status", (int)status);
                command.Parameters.AddWithValue("@dateTime", now);

                command.ExecuteNonQuery();
            }
        }

        private SQLiteConnection GetNewOpenConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}


