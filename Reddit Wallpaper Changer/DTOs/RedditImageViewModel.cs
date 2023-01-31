using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Reddit_Wallpaper_Changer.DTOs
{
    public class RedditImageViewModel : IEquatable<RedditImageViewModel>
    {
        public Bitmap Bitmap { get; }
        public string Title { get; }
        public string ThreadId { get; }
        public string Url { get; }

        public RedditImageViewModel(RedditLink redditLink, Bitmap bitmap)
        {
            Title = redditLink.Title;
            ThreadId = redditLink.ThreadId;
            Url = redditLink.Url;
            Bitmap = bitmap;
        }

        public override bool Equals(object obj)
            => Equals(obj as RedditImageViewModel);

        public bool Equals(RedditImageViewModel other)
            => !(other is null) &&
            EqualityComparer<Bitmap>.Default.Equals(Bitmap, other.Bitmap) &&
            Title == other.Title &&
            ThreadId == other.ThreadId &&
            Url == other.Url;

        public override int GetHashCode()
        {
            var hashCode = -287158351;

            hashCode = hashCode * -1521134295 + EqualityComparer<Bitmap>.Default.GetHashCode(Bitmap);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Title);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(ThreadId);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Url);
            
            return hashCode;
        }

        public static bool operator ==(RedditImageViewModel left, RedditImageViewModel right)
            => EqualityComparer<RedditImageViewModel>.Default.Equals(left, right);

        public static bool operator !=(RedditImageViewModel left, RedditImageViewModel right)
            => !(left == right);
    }

    public static class RedditImageExtensions
    {
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Bitmap> BitmapCache = new Dictionary<string, Bitmap>();

        public static RedditImageViewModel ToViewModel(this RedditLink redditLink)
        {
            var bitmap = GetBitmap(redditLink.ThreadId) ?? Resources.null_thumb;

            return new RedditImageViewModel(redditLink, bitmap);
        }

        private static Bitmap GetBitmap(string threadId)
        {
            lock (CacheLock)
            {
                if (!BitmapCache.TryGetValue(threadId, out var result))
                {
                    var fileName = GetThumbnailFileName(threadId);

                    if (File.Exists(fileName))
                    {
                        using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                        using (var tempImage = Image.FromStream(fs))
                        {
                            result = new Bitmap(tempImage);
                        }

                        BitmapCache[threadId] = result;
                    }
                }

                return result;
            }
        }

        private static string GetThumbnailFileName(string threadId)
            => $@"{Settings.Default.thumbnailCache}\{threadId}.jpg";
    }
}
