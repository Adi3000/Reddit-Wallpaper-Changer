using Reddit_Wallpaper_Changer.Properties;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Reddit_Wallpaper_Changer
{
    public class RedditLink
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string ThreadId { get; }
        public string Permalink { get; set; }

        public RedditLink(string title, string threadId, string url)
        {
            Title = title;
            ThreadId = threadId;
            Url = url;
        }

        public void LogDetails()
        {
            Logger.Instance.LogMessageToFile("URL: " + Url, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Title: " + Title, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Thread ID: " + ThreadId, LogLevel.Information);
        }

        public void SaveAsCurrentWallpaper(string extension, string wallpaperFile)
        {
            Settings.Default.currentWallpaperFile = wallpaperFile;
            Settings.Default.url = Url;
            Settings.Default.threadTitle = Title;
            Settings.Default.currentWallpaperUrl = Url;
            Settings.Default.currentWallpaperName = Title + extension;
            Settings.Default.threadID = ThreadId;

            Settings.Default.Save();
        }

        //======================================================================
        // Override auto save faves if auto save all is enabled
        //======================================================================
        public async Task<bool> SaveImage()
        {
            try
            {
                var ext = Path.GetExtension(Url.ToString());
                var fileName = HelperMethods.StripInvalidCharactersFromFileName(Title);

                var fullFileName = fileName + ext;

                if (fileName != Title)
                    Logger.Instance.LogMessageToFile($"Removed illegal characters from post title: {fullFileName}", LogLevel.Information);

                var fileSaveLocation = $@"{Settings.Default.defaultSaveLocation}\{fullFileName}";

                if (!File.Exists(fileSaveLocation))
                {
                    using (var webClient = HelperMethods.CreateWebClient())
                    {
                        await webClient.DownloadFileTaskAsync(Url, fileSaveLocation).ConfigureAwait(false);
                    }

                    Logger.Instance.LogMessageToFile($"Saved {fullFileName} to {Settings.Default.defaultSaveLocation}", LogLevel.Information);
                }
                else
                    Logger.Instance.LogMessageToFile($"Not auto saving {fullFileName} because it already exists.", LogLevel.Warning);

                return true;
            }
            catch (Exception Ex)
            {
                Logger.Instance.LogMessageToFile($"Error Saving Wallpaper: {Ex.Message}", LogLevel.Error);
            }

            return true;
        }
    }
}
