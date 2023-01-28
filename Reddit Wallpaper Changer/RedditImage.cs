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
    }
}
