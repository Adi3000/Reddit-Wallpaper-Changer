using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Drawing;
using System.IO;

namespace Reddit_Wallpaper_Changer
{
    public class RedditImage
    {
        public string Thumbnail { get; }
        public string Title { get; }
        public string ThreadId { get; }
        public string Url { get; }
        public string Date { get; }

        public RedditImage(string thumbnail, string title, string threadId, string url, string date)
        {
            Thumbnail = thumbnail;
            Title = title;
            ThreadId = threadId;
            Url = url;
            Date = date;
        }

        public void SaveToThumbnailCache()
        {
            var fileName = $@"{Settings.Default.thumbnailCache}\{ThreadId}.jpg";

            if (File.Exists(fileName)) return;

            try
            {
                using (var ms = new MemoryStream(Convert.FromBase64String(Thumbnail)))
                using (var image = Image.FromStream(ms))
                {
                    image.Save(fileName);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unable to save thumbnail: {Thumbnail} to file: {fileName}. {ex.Message}", LogLevel.Warning);
            }
        }
    }
}
