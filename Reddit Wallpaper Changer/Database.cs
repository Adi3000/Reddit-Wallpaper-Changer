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
        public async Task ConnectToDatabaseAsync()
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
                using (var con = await GetNewOpenConnectionAsync().ConfigureAwait(false))
                {
                    Logger.Instance.LogMessageToFile($"Successfully connected to database '{DbName}'", LogLevel.Information);

                    bool databaseEmpty = false;

                    using (var existsCommand = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name='blacklist';", con))
                    {
                        await existsCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                        databaseEmpty = !existsCommand.ExecuteReader().HasRows;
                    }

                    if (databaseEmpty)
                    {
                        using (var beginCommand = new SQLiteCommand("BEGIN", con))
                        using (var versionTableCommand = new SQLiteCommand("CREATE TABLE version (version STRING, date STRING)", con))
                        using (var blacklistTableCommand = new SQLiteCommand("CREATE TABLE blacklist (thumbnail STRING, title STRING, threadid STRING, url STRING, date STRING)", con))
                        using (var blacklistIndexCommand = new SQLiteCommand("CREATE INDEX idx_blacklist ON blacklist (url)", con))
                        using (var blacklistIndexDateTimeCommand = new SQLiteCommand("CREATE INDEX idx_blacklist_date ON blacklist (datetime(date) DESC)", con))
                        using (var favouritesTableCommand = new SQLiteCommand("CREATE TABLE favourites (thumbnail STRING, title STRING, threadid STRING, url STRING, date STRING)", con))
                        using (var favouritesIndexCommand = new SQLiteCommand("CREATE INDEX idx_favourites ON favourites (url)", con))
                        using (var favouritesIndexDateTimeCommand = new SQLiteCommand("CREATE INDEX idx_favourites_date ON favourites (datetime(date) DESC)", con))
                        using (var historyTableCommand = new SQLiteCommand("CREATE TABLE history (thumbnail STRING, title STRING, threadid STRING, url STRING, date STRING)", con))
                        using (var historyIndexCommand = new SQLiteCommand("CREATE INDEX idx_history ON history (url)", con))
                        using (var historyIndexDateTimeCommand = new SQLiteCommand("CREATE INDEX idx_history_date ON history (datetime(date) DESC)", con))
                        using (var endCommand = new SQLiteCommand("END", con))
                        {
                            await beginCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

                            await versionTableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Table 'version' successfully created.", LogLevel.Information);

                            await blacklistTableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Table 'blacklist' successfully created.", LogLevel.Information);

                            await blacklistIndexCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Index 'idx_blacklist' successfully created.", LogLevel.Information);

                            await blacklistIndexDateTimeCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Index 'idx_blacklist_date' successfully created.", LogLevel.Information);

                            await favouritesTableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Table 'favourites' successfully created.", LogLevel.Information);

                            await favouritesIndexCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Index 'idx_favourites' successfully created.", LogLevel.Information);

                            await favouritesIndexDateTimeCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Index 'idx_favourites_date' successfully created.", LogLevel.Information);

                            await historyTableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Table 'history' successfully created.", LogLevel.Information);

                            await historyIndexCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Index 'idx_history' successfully created.", LogLevel.Information);

                            await historyIndexDateTimeCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                            Logger.Instance.LogMessageToFile("Index 'idx_history_date' successfully created.", LogLevel.Information);

                            await endCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }

                        await AddVersionAsync().ConfigureAwait(false);
                    }
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

                                using (var con = await GetNewOpenConnectionAsync().ConfigureAwait(false))
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
        public async Task AddVersionAsync()
        {
            try
            {
                var currentVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
                var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF");

                using (var connection = await GetNewOpenConnectionAsync().ConfigureAwait(false))
                using (var command = new SQLiteCommand("INSERT INTO version (version, date) " +
                                                       "VALUES (@version, @dateTime)", connection))
                {
                    command.Parameters.AddWithValue("version", currentVersion);
                    command.Parameters.AddWithValue("dateTime", dateTime);

                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
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
        public async Task RemoveFromBlacklistAsync(string url)
        {
            try
            {
                await DeleteFromTableAsync("blacklist", url);

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
        public async Task RemoveFromFavouritesAsync(string url)
        {
            try
            {
                await DeleteFromTableAsync("favourites", url);

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
        public async Task RemoveFromHistoryAsync(string url)
        {
            try
            {
                await DeleteFromTableAsync("history", url);

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
        public async Task<IEnumerable<RedditImage>> GetFromHistoryAsync()
        {
            try
            {
                return await GetRedditImagesFromDatabaseAsync("history").ConfigureAwait(false);
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
        public async Task<IEnumerable<RedditImage>> GetFromBlacklistAsync()
        {
            try
            {
                return await GetRedditImagesFromDatabaseAsync("blacklist").ConfigureAwait(false);
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
        public async Task<IEnumerable<RedditImage>> GetFromFavouritesAsync()
        {
            try
            {
                return await GetRedditImagesFromDatabaseAsync("favourites").ConfigureAwait(false);
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
        public async Task<bool> IsBlacklistedAsync(string url)
        {
            try
            {
                using (var con = await GetNewOpenConnectionAsync().ConfigureAwait(false))
                using (var command = new SQLiteCommand("SELECT COUNT(*) " +
                                                       "FROM blacklist " +
                                                       "WHERE url = @url", con))

                {
                    command.Parameters.AddWithValue("@url", url);

                    var count = await command.ExecuteScalarAsync().ConfigureAwait(false);
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
        public async Task<bool> WipeTableAsync(string tableName)
        {
            try
            {
                switch (tableName)
                {
                    case "favourites":
                    case "history":
                    case "blacklist":
                        using (var con = await GetNewOpenConnectionAsync().ConfigureAwait(false))
                        using (var command = new SQLiteCommand($"DELETE FROM {tableName}", con))
                        {
                            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
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
        public async Task<bool> BackupDatabaseAsync(string backupPath)
        {
            try
            {
                var fullBackupPath = Path.Combine(backupPath, DbName);

                Logger.Instance.LogMessageToFile($"Backing up database to: {fullBackupPath}", LogLevel.Information);

                File.Copy(_dbPath, fullBackupPath, true);

                await ConnectToDatabaseAsync().ConfigureAwait(false);

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
        public async Task<bool> RestoreDatabaseAsync(string restoreSource)
        {
            try
            {
                Logger.Instance.LogMessageToFile($"Restoring database to: {_dbPath}", LogLevel.Information);

                File.Copy(restoreSource, _dbPath, true);

                await ConnectToDatabaseAsync().ConfigureAwait(false);

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

                var images = await GetAllImagesFromDatabaseAsync().ConfigureAwait(false);

                foreach (var image in images)
                {
                    image.SaveToThumbnailCache();
                }

                Logger.Instance.LogMessageToFile("Wallpaper thumbnail cache updated.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Error updating Wallpaper thumbnail cache: {ex.Message}", LogLevel.Warning);
            }
        }

        private async Task<IEnumerable<RedditImage>> GetAllImagesFromDatabaseAsync()
        {
            var imageList = new List<RedditImage>();

            using (var connection = await GetNewOpenConnectionAsync().ConfigureAwait(false))
            using (var command = new SQLiteCommand("SELECT * FROM blacklist " +
                                                   "UNION " +
                                                   "SELECT * FROM history " +
                                                   "UNION " +
                                                   "SELECT * FROM favourites", connection))
            using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess).ConfigureAwait(false))
            {
                if (!reader.HasRows)
                    return imageList;

                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var redditImage = new RedditImage
                    (
                        await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false), // thumbnail
                        await reader.GetFieldValueAsync<string>(1).ConfigureAwait(false), // title
                        await reader.GetFieldValueAsync<string>(2).ConfigureAwait(false), // threadid
                        await reader.GetFieldValueAsync<string>(3).ConfigureAwait(false), // url
                        await reader.GetFieldValueAsync<string>(4).ConfigureAwait(false)  // date
                    );

                    imageList.Add(redditImage);
                }
            }

            return imageList;
        }

        private async Task<IEnumerable<RedditImage>> GetRedditImagesFromDatabaseAsync(string tableName)
        {
            var imageList = new List<RedditImage>();

            using (var connection = await GetNewOpenConnectionAsync().ConfigureAwait(false))
            using (var command = new SQLiteCommand("SELECT * " +
                                                  $"FROM {tableName} " +
                                                   "ORDER BY datetime(date) DESC", connection))
            using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess).ConfigureAwait(false))
            {
                if (!reader.HasRows)
                    return imageList;

                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var redditImage = new RedditImage
                    (
                        await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false), // thumbnail
                        await reader.GetFieldValueAsync<string>(1).ConfigureAwait(false), // title
                        await reader.GetFieldValueAsync<string>(2).ConfigureAwait(false), // threadid
                        await reader.GetFieldValueAsync<string>(3).ConfigureAwait(false), // url
                        await reader.GetFieldValueAsync<string>(4).ConfigureAwait(false)  // date
                    );

                    imageList.Add(redditImage);
                }
            }

            return imageList;
        }

        private async Task DeleteFromTableAsync(string tableName, string url)
        {
            using (var connection = await GetNewOpenConnectionAsync().ConfigureAwait(false))
            using (var command = new SQLiteCommand("DELETE " +
                                                  $"FROM {tableName} " +
                                                   "WHERE url = @url", connection))
            {
                command.Parameters.AddWithValue("@url", url);

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private async Task<RedditImage> InsertWallpaperIntoDatabaseAsync(string tableName, RedditLink redditLink)
        {
            var thumbnail = await HelperMethods.GetThumbnailAsync(redditLink.Url).ConfigureAwait(false);
            var dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.FFF");
            var redditImage = new RedditImage(thumbnail, redditLink.Title, redditLink.ThreadId, redditLink.Url, dateTime);

            redditLink.Title = redditLink.Title.Replace("'", "''");

            using (var connection = await GetNewOpenConnectionAsync().ConfigureAwait(false))
            using (var command = new SQLiteCommand($"INSERT INTO {tableName} (thumbnail, title, threadid, url, date) " +
                                                    "VALUES (@thumbnail, @title, @threadid, @url, @dateTime)", connection))
            {
                command.Parameters.AddWithValue("@thumbnail", thumbnail);
                command.Parameters.AddWithValue("@title", redditLink.Title);
                command.Parameters.AddWithValue("@threadid", redditLink.ThreadId);
                command.Parameters.AddWithValue("@url", redditLink.Url);
                command.Parameters.AddWithValue("@dateTime", dateTime);

                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            return redditImage;
        }

        private async Task<SQLiteConnection> GetNewOpenConnectionAsync()
        {
            var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            return connection;
        }
    }
}


