using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;

namespace Reddit_Wallpaper_Changer
{
    public class Database
    {
        private const string DbName = "Reddit-Wallpaper-Changer.sqlite";

        private readonly string _dbPath, _connectionString;

        public Database(string appDataPath)
        {
            _dbPath = $@"{appDataPath}\{DbName}";
            _connectionString = $"Data Source={_dbPath};Version=3;";
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

                    const string Sql = @"
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
                        END;";

                    using (var command = new SQLiteCommand(Sql, con))
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
            try
            {
                Logger.Instance.LogMessageToFile($"Migrating Blacklist.xml to '{DbName}'. This is a one off task...", LogLevel.Information);

                var fullFilePath = $@"{Settings.Default.AppDataPath}\Blacklist.xml";

                if (File.Exists(fullFilePath))
                {
                    var doc = new XmlDocument();
                    XmlNodeList list;

                    doc.Load(fullFilePath);
                    list = doc.SelectNodes("Blacklisted/Wallpaper");

                    using (var wc = HelperMethods.CreateWebClient())
                    {
                        foreach (XmlNode xn in list)
                        {
                            try
                            {
                                var url = xn["URL"].InnerText;
                                var title = xn["Title"].InnerText.Replace("'", "''");
                                var threadId = xn["ThreadID"].InnerText;

                                Logger.Instance.LogMessageToFile($"Migrating: {title}, {threadId}, {url}", LogLevel.Information);

                                var bytes = await wc.DownloadDataTaskAsync(url).ConfigureAwait(false);

                                var base64ImageRepresentation = HelperMethods.ConvertImageBytesToBase64String(bytes);

                                var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF");

                                using (var con = GetNewOpenConnection())
                                using (var command = new SQLiteCommand(
                                    "INSERT INTO blacklist (thumbnail, title, threadid, url, date) " +
                                    "VALUES (@thumbnail, @title, @threadid, @url, @date)", con))
                                {
                                    command.Parameters.AddWithValue("@thumbnail", base64ImageRepresentation);
                                    command.Parameters.AddWithValue("@title", title);
                                    command.Parameters.AddWithValue("@threadid", threadId);
                                    command.Parameters.AddWithValue("@url", url);
                                    command.Parameters.AddWithValue("@date", dateTime);

                                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                                }

                                Logger.Instance.LogMessageToFile($"Successfully migrated: {title}, {threadId}, {url}", LogLevel.Information);
                            }
                            catch (WebException ex)
                            {
                                Logger.Instance.LogMessageToFile($"Unexpected error migrating: {ex.Message}", LogLevel.Error);
                            }
                        }
                    }

                    Logger.Instance.LogMessageToFile("Migration to database completed successfully!", LogLevel.Information);

                    File.Delete(fullFilePath);

                    Logger.Instance.LogMessageToFile("Blacklist.xml deleted.", LogLevel.Information);
                }
                else
                    Logger.Instance.LogMessageToFile("No blacklist.xml file to migrate", LogLevel.Information);

                Settings.Default.dbMigrated = true;
                Settings.Default.Save();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Error migrating: " + ex.Message, LogLevel.Error);
            }
        }

        //======================================================================
        // Add wallpaper to blacklisted
        //======================================================================
        public async Task<bool> BlacklistWallpaperAsync(RedditLink redditLink)
        {
            try
            {
                await InsertWallpaperIntoDatabaseAsync("blacklist", redditLink).ConfigureAwait(false);

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
                await InsertWallpaperIntoDatabaseAsync("favourites", redditLink).ConfigureAwait(false);

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
        public void AddVersion()
        {
            try
            {
                var currentVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
                var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF");

                using (var connection = GetNewOpenConnection())
                using (var command = new SQLiteCommand("INSERT INTO version (version, date) " +
                                                       "VALUES (@version, @dateTime)", connection))
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
        public async Task<RedditImage> AddWallpaperToHistoryAsync(RedditLink redditLink)
        {
            try
            {
                return await InsertWallpaperIntoDatabaseAsync("history", redditLink).ConfigureAwait(false);
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
        public void RemoveFromBlacklist(string url)
        {
            try
            {
                DeleteFromTable("blacklist", url);

                Logger.Instance.LogMessageToFile($"Wallpaper removed from blacklist! URL: {url}", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error removing wallpaper from Blacklist: {ex.Message}", LogLevel.Warning);
            }
        }

        //======================================================================
        // Remove wallpaper from favourites
        //======================================================================
        public void RemoveFromFavourites(string url)
        {
            try
            {
                DeleteFromTable("favourites", url);

                Logger.Instance.LogMessageToFile($"Wallpaper removed from favourites! URL: {url}", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error removing wallpaper from favourites: {ex.Message}", LogLevel.Warning);
            }
        }

        //======================================================================
        // Remove wallpaper from History
        //======================================================================
        public void RemoveFromHistory(string url)
        {
            try
            {
                DeleteFromTable("history", url);

                Logger.Instance.LogMessageToFile($"Wallpaper removed from history! URL: {url}", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error removing wallpaper from history: {ex.Message}", LogLevel.Warning);
            }
        }

        //======================================================================
        // Retrieve history from the database
        //======================================================================
        public IEnumerable<RedditImage> GetFromHistory()
        {
            try
            {
                return GetRedditImagesFromDatabase("history");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error retrieving History from database: {ex.Message}", LogLevel.Warning);
                return null;
            }
        }

        //======================================================================
        // Retrieve blacklist from the database
        //======================================================================
        public IEnumerable<RedditImage> GetFromBlacklist()
        {
            try
            {
                return GetRedditImagesFromDatabase("blacklist");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error retrieving Blacklist from database: {ex.Message}", LogLevel.Warning);
                return null;
            }
        }

        //======================================================================
        // Retrieve favourites from the database
        //======================================================================
        public IEnumerable<RedditImage> GetFromFavourites()
        {
            try
            {
                return GetRedditImagesFromDatabase("favourites");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error retrieving favourites from database: {ex.Message}", LogLevel.Warning);
                return null;
            }
        }

        //======================================================================
        // Check for blacklisted wallpaper
        //======================================================================
        public bool IsBlacklisted(string url)
        {
            try
            {
                using (var con = GetNewOpenConnection())
                using (var command = new SQLiteCommand("SELECT COUNT(*) " +
                                                       "FROM blacklist " +
                                                       "WHERE url = @url", con))

                {
                    command.Parameters.AddWithValue("@url", url);

                    var count = command.ExecuteScalar();
                    if (count != null)
                        return Convert.ToInt32(count) > 0;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error checking for Blacklist entry: {ex.Message}", LogLevel.Warning);
                return false;
            }
        }

        //======================================================================
        // Delete all values from table
        //======================================================================
        public bool WipeTable(string tableName)
        {
            try
            {
                switch (tableName)
                {
                    case "favourites":
                    case "history":
                    case "blacklist":
                        using (var con = GetNewOpenConnection())
                        using (var command = new SQLiteCommand($"DELETE FROM {tableName}", con))
                        {
                            command.ExecuteNonQuery();
                        }
                        break;
                }

                Logger.Instance.LogMessageToFile($"All {tableName} contents have been successfully deleted!", LogLevel.Information);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected error deleting data from {tableName}: {ex.Message}", LogLevel.Warning);
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

                InitialiseDatabase();

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
        public void BuildThumbnailCache()
        {
            try
            {
                Logger.Instance.LogMessageToFile("Updating wallpaper thumbnail cache.", LogLevel.Information);

                var threadThumbnails = GetAllThumbnailsFromDatabase();

                foreach (var (ThreadId, Thumbnail) in threadThumbnails)
                {
                    HelperMethods.SaveToThumbnailCache(ThreadId, Thumbnail);
                }

                Logger.Instance.LogMessageToFile("Wallpaper thumbnail cache updated.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Error updating Wallpaper thumbnail cache: {ex.Message}", LogLevel.Warning);
            }
        }

        private IEnumerable<(string ThreadId, string Thumbnail)> GetAllThumbnailsFromDatabase()
        {
            var threadThumbnails = new List<(string, string)>();

            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand(
                @"WITH cte AS
                (
                    SELECT thumbnail, threadid, date FROM blacklist
                    UNION ALL
                    SELECT thumbnail, threadid, date FROM history
                    UNION ALL
                    SELECT thumbnail, threadid, date FROM favourites
                ),
                row_nums AS (
                    SELECT
                        thumbnail,
                        threadid,
                        ROW_NUMBER() OVER (PARTITION BY threadid ORDER BY datetime(date) DESC) AS row_num
                    FROM cte
                )
                SELECT thumbnail, threadid
                FROM row_nums
                WHERE row_num = 1", connection))
            using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
            {
                if (!reader.HasRows)
                    return threadThumbnails;

                while (reader.Read())
                {
                    var thumbnail = reader.GetFieldValue<string>(0);    // thumbnail
                    var threadId = reader.GetFieldValue<string>(1);     // threadid

                    threadThumbnails.Add((threadId, thumbnail));
                }
            }

            return threadThumbnails;
        }

        private IEnumerable<RedditImage> GetRedditImagesFromDatabase(string tableName)
        {
            var imageList = new List<RedditImage>();

            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand("SELECT * " +
                                                  $"FROM {tableName} " +
                                                   "ORDER BY datetime(date) DESC", connection))
            using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
            {
                if (!reader.HasRows)
                    return imageList;

                while (reader.Read())
                {
                    var redditImage = new RedditImage
                    (
                        reader.GetFieldValue<string>(0), // thumbnail
                        reader.GetFieldValue<string>(1), // title
                        reader.GetFieldValue<string>(2), // threadid
                        reader.GetFieldValue<string>(3), // url
                        reader.GetFieldValue<string>(4)  // date
                    );

                    imageList.Add(redditImage);
                }
            }

            return imageList;
        }

        private void DeleteFromTable(string tableName, string url)
        {
            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand("DELETE " +
                                                  $"FROM {tableName} " +
                                                   "WHERE url = @url", connection))
            {
                command.Parameters.AddWithValue("@url", url);

                command.ExecuteNonQuery();
            }
        }

        private async Task<RedditImage> InsertWallpaperIntoDatabaseAsync(string tableName, RedditLink redditLink)
        {
            var thumbnail = await HelperMethods.GetThumbnailAsync(redditLink.Url).ConfigureAwait(false);
            var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF");
            var redditImage = new RedditImage(thumbnail, redditLink.Title, redditLink.ThreadId, redditLink.Url, dateTime);

            using (var connection = GetNewOpenConnection())
            using (var command = new SQLiteCommand($"INSERT INTO {tableName} (thumbnail, title, threadid, url, date) " +
                                                    "VALUES (@thumbnail, @title, @threadid, @url, @dateTime)", connection))
            {
                command.Parameters.AddWithValue("@thumbnail", thumbnail);
                command.Parameters.AddWithValue("@title", redditLink.Title);
                command.Parameters.AddWithValue("@threadid", redditLink.ThreadId);
                command.Parameters.AddWithValue("@url", redditLink.Url);
                command.Parameters.AddWithValue("@dateTime", dateTime);

                command.ExecuteNonQuery();
            }

            return redditImage;
        }

        private SQLiteConnection GetNewOpenConnection()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}


