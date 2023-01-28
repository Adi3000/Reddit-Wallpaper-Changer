using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Reddit_Wallpaper_Changer.DTOs
{
    public class RedditImageViewModel
    {
        public Bitmap Bitmap { get; }
        public string Title { get; }
        public string ThreadId { get; }
        public string Url { get; }
        public DateTime Date { get; }

        public RedditImageViewModel(RedditImage image, Bitmap bitmap)
        {
            Title = image.Title;
            ThreadId = image.ThreadId;
            Url = image.Url;
            Date = image.DateTime;
            Bitmap = bitmap;
        }
    }

    public static class RedditImageExtensions
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Bitmap> BitmapCache = new Dictionary<string, Bitmap>();

        public static RedditImageViewModel ToViewModel(this RedditImage redditImage)
        {
            Bitmap bitmap;
            if (File.Exists(GetThumbnailFileName(redditImage.ThreadId)))
                bitmap = GetBitmap(redditImage.ThreadId);
            else
                bitmap = Resources.null_thumb;

            return new RedditImageViewModel(redditImage, bitmap);
        }

        private static Bitmap GetBitmap(string threadId)
        {
            lock (CacheLock)
            {
                if (!BitmapCache.TryGetValue(threadId, out var result))
                {
                    using (var fs = new FileStream(GetThumbnailFileName(threadId), FileMode.Open, FileAccess.Read))
                    using (var tempImage = Image.FromStream(fs))
                    {
                        result = new Bitmap(tempImage);
                    }

                    BitmapCache[threadId] = result;
                }

                return result;
            }
        }

        private static string GetThumbnailFileName(string threadId)
            => $@"{Settings.Default.thumbnailCache}\{threadId}.jpg";
    }
}
