using Reddit_Wallpaper_Changer.Properties;
using System;
using System.IO;

namespace Reddit_Wallpaper_Changer
{
    public class RedditImage
    {
        public string Image { get; }
        public string Title { get; }
        public string ThreadId { get; }
        public string Url { get; }
        public string Date { get; }

        public RedditImage(string image, string title, string threadId, string url, string date)
        {
            Image = image;
            Title = title;
            ThreadId = threadId;
            Url = url;
            Date = date;
        }

        public void SaveToThumbnailCache()
        {
            var fileName = $@"{Settings.Default.thumbnailCache}\{ThreadId}.jpg";

            if (File.Exists(fileName)) return;

            using (var ms = new MemoryStream(Convert.FromBase64String(Image)))
            using (var image = System.Drawing.Image.FromStream(ms))
            {
                image.Save(fileName);
            }
        }
    }
}
