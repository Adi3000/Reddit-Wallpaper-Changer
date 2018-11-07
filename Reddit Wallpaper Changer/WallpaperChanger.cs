using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Reddit_Wallpaper_Changer
{
    public class WallpaperChanger
    {
        private const int MaxRetryAttempts = 50;

        private static readonly IReadOnlyList<string> TopValues = new List<string> { "&t=day", "&t=year", "&t=all", "&t=month", "&t=week" };
        private static readonly IReadOnlyList<string> SortValues = new List<string> { "&sort=relevance", "&sort=hot", "&sort=top", "&sort=comments", "&sort=new" };
        private static readonly IReadOnlyList<string> ImageExtensions = new List<string> { ".JPG", ".JPEG", ".BMP", ".GIF", ".PNG" };

        private readonly MainThreadMarshaller _uiMarshaller;
        private readonly Database _database;
        private readonly Random _random = new Random();
        private readonly List<string> _currentSessionHistory = new List<string>();

        private int _noResultCount;

        public WallpaperChanger(MainThreadMarshaller uiMarshaller, Database database)
        {
            _uiMarshaller = uiMarshaller;
            _database = database;
        }

        //======================================================================
        // Set the wallpaper
        //======================================================================
        public async Task<bool> SetWallpaperAsync(RedditLink redditLink)
        {
            Logger.Instance.LogMessageToFile("Setting wallpaper.", LogLevel.Information);

            if (!await WallpaperLinkValidAsync(redditLink).ConfigureAwait(false))
                return false;

            HelperMethods.ResetManualOverride();

            _uiMarshaller.UpdateStatus("Setting Wallpaper");

            if (!string.IsNullOrEmpty(redditLink.Url))
            {
                redditLink.Url = await ConvertRedditLinkToImageLink(redditLink.Url, _random, ImageExtensions).ConfigureAwait(false);

                var uri = new Uri(redditLink.Url);
                var extension = Path.GetExtension(uri.LocalPath);
                var fileName = $"{redditLink.ThreadId}{extension}";
                var wallpaperFile = Path.Combine(Path.GetTempPath(), fileName);

                redditLink.SaveAsCurrentWallpaper(extension, wallpaperFile);
                redditLink.LogDetails();

                if (ImageExtensions.Contains(extension.ToUpper()))
                {
                    await DownloadWallpaperAsync(uri.AbsoluteUri, wallpaperFile);

                    if (!await SetWallpaperAsync(redditLink, wallpaperFile))
                        return false;
                }
                else
                {
                    Logger.Instance.LogMessageToFile($"Wallpaper URL failed validation: {extension.ToUpper()}", LogLevel.Warning);

                    _uiMarshaller.RestartChangeWallpaperTimer();
                }

                using (var wc = HelperMethods.CreateWebClient())
                {
                    var bytes = await wc.DownloadDataTaskAsync(uri).ConfigureAwait(false);
                    if (!bytes.Any())
                        _uiMarshaller.RestartChangeWallpaperTimer();
                }
            }
            else
                _uiMarshaller.RestartChangeWallpaperTimer();

            return true;
        }

        // TODO refactor
        //======================================================================
        // Search for a wallpaper
        //======================================================================
        public async Task<bool> SearchForWallpaperAsync()
        {
            Logger.Instance.LogMessageToFile("Looking for a wallpaper.", LogLevel.Information);

            if (MaxRetriesExceeded())
                return true;

            _uiMarshaller.UpdateStatus("Finding New Wallpaper");

            try
            {
                var url = GetRedditSearchUrl(_random, _uiMarshaller);
                var jsonData = await GetJsonDataAsync(url, _uiMarshaller).ConfigureAwait(false);

                try
                {
                    if (jsonData.Any())
                    {
                        var redditResult = GetRedditResult(JToken.Parse(jsonData));

                        JToken token = null;

                        try
                        {
                            foreach (var toke in redditResult.Reverse())
                            {
                                token = toke;
                            }

                            if (token == null)
                            {
                                if (redditResult.HasValues)
                                {
                                    var randIndex = _random.Next(0, redditResult.Count() - 1);
                                    token = redditResult.ElementAt(randIndex);
                                }
                                else
                                {
                                    ++_noResultCount;

                                    _uiMarshaller.UpdateStatus("No results found, searching again.");
                                    Logger.Instance.LogMessageToFile("No search results, trying to change wallpaper again.", LogLevel.Information);

                                    return false;
                                }
                            }

                            if ((WallpaperGrabType)Settings.Default.wallpaperGrabType == WallpaperGrabType.Random)
                                token = redditResult.ElementAt(_random.Next(0, redditResult.Count() - 1));

                            _uiMarshaller.SetCurrentThread($"http://reddit.com{token["data"]["permalink"]}");

                            if (!await ChangeWallpaperIfValidImageAsync(token).ConfigureAwait(false))
                                return true;
                        }
                        catch (InvalidOperationException)
                        {
                            _uiMarshaller.LogFailure("Your search query is bringing up no results.", 
                                "No results from the search query.");
                        }
                    }
                    else
                    {
                        _uiMarshaller.LogFailure("Subreddit Probably Doesn't Exist", 
                            "Subreddit probably does not exist.");

                        _noResultCount++;

                        return true;
                    }
                }
                catch (JsonReaderException ex)
                {
                    _uiMarshaller.LogFailure($"Unexpected error: {ex.Message}", 
                        $"Unexpected error: {ex.Message}", LogLevel.Error);
                }
            }
            catch { }

            return true;
        }

        private async Task<bool> SetWallpaperAsync(RedditLink redditLink, string wallpaperFile)
        {
            if (!WallpaperSizeValid(wallpaperFile))
                return false;

            await ActiveDesktop.SetWallpaperAsync(wallpaperFile).ConfigureAwait(false);

            _noResultCount = 0;

            _uiMarshaller.UpdateStatus("Wallpaper Changed!");

            Logger.Instance.LogMessageToFile("Wallpaper changed!", LogLevel.Information);

            _currentSessionHistory.Add(redditLink.ThreadId);

            var redditImage = await _database.AddWallpaperToHistoryAsync(redditLink)
                                             .ConfigureAwait(false);

            await _database.BuildThumbnailCacheAsync().ConfigureAwait(false);

            _uiMarshaller.AddImageToHistory(redditImage);

            if (!Settings.Default.disableNotifications && Settings.Default.wallpaperInfoPopup)
                _uiMarshaller.OpenPopupInfoWindow(redditLink);

            if (Settings.Default.autoSave)
                HelperMethods.SaveCurrentWallpaper(Settings.Default.currentWallpaperName);

            _uiMarshaller.UpdateStatus("");

            return true;
        }

        private bool WallpaperSizeValid(string wallpaperFile)
        {
            if (Settings.Default.fitWallpaper)
            {
                var screen = ControlHelpers.GetScreenDimensions();

                using (var img = Image.FromFile(wallpaperFile))
                {
                    if (screen.Width != img.Width || screen.Height != img.Height)
                    {
                        _uiMarshaller.LogFailure("Wallpaper resolution mismatch.",
                            $"Wallpaper size mismatch. Screen: {screen.Width}x{screen.Height}, Wallpaper: {img.Width}x{img.Height}");

                        _noResultCount++;
                        return false;
                    }
                }
            }

            return true;
        }

        private async Task<bool> WallpaperLinkValidAsync(RedditLink redditLink)
        {
            if (await _database.IsBlacklistedAsync(redditLink.Url).ConfigureAwait(false))
            {
                _uiMarshaller.UpdateStatus("Wallpaper is blacklisted.");
                Logger.Instance.LogMessageToFile("The selected wallpaper has been blacklisted, searching again.", LogLevel.Warning);
                _uiMarshaller.DisableChangeWallpaperTimer();

                return false;
            }

            if (!Settings.Default.manualOverride && 
                Settings.Default.suppressDuplicates && 
                _currentSessionHistory.Contains(redditLink.ThreadId))
            {
                _uiMarshaller.UpdateStatus("Wallpaper already used this session.");
                Logger.Instance.LogMessageToFile("The selected wallpaper has already been used this session, searching again.", LogLevel.Warning);
                _uiMarshaller.DisableChangeWallpaperTimer();

                return false;
            }

            return true;
        }

        private bool MaxRetriesExceeded()
        {
            if (_noResultCount < MaxRetryAttempts)
                return false;

            _noResultCount = 0;

            Logger.Instance.LogMessageToFile($"No results after {MaxRetryAttempts} attempts. Disabling Reddit Wallpaper Changer.", LogLevel.Warning);

            _uiMarshaller.ShowNoResultsBalloonTip(MaxRetryAttempts);
            _uiMarshaller.UpdateStatus("RWC Disabled.");
            _uiMarshaller.DisableChangeWallpaperTimer();

            return true;
        }

        private async Task<bool> ChangeWallpaperIfValidImageAsync(JToken token)
        {
            var title = token["data"]["title"].ToString();
            var url = token["data"]["url"].ToString();
            var id = token["data"]["id"].ToString();

            Logger.Instance.LogMessageToFile($"Found a wallpaper! Title: {title}, URL: {url}, ThreadID: {id}", LogLevel.Information);

            // Validate URL 
            if (await HelperMethods.ValidateImageAsync(url).ConfigureAwait(false))
            {
                if (await HelperMethods.ValidateImgurImageAsync(url).ConfigureAwait(false))
                {
                    var imageDetails = new RedditLink(url, title, id);

                    if (!await SetWallpaperAsync(imageDetails).ConfigureAwait(false))
                    {
                        while (!await SearchForWallpaperAsync().ConfigureAwait(false)) { }
                    }
                }
                else
                {
                    _uiMarshaller.LogFailure("Wallpaper has been removed from Imgur.", 
                        "The selected wallpaper was deleted from Imgur, searching again.");

                    _noResultCount++;

                    while (!await SearchForWallpaperAsync().ConfigureAwait(false)) { }
                }
            }
            else
            {
                _uiMarshaller.LogFailure("The selected URL is not for an image.", 
                    "Not a direct wallpaper URL, searching again.");

                _noResultCount++;
                return false;
            }

            return true;
        }

        private static async Task DownloadWallpaperAsync(string uri, string fileName)
        {
            if (File.Exists(fileName))
            {
                try
                {
                    File.Delete(fileName);
                }
                catch (IOException ex)
                {
                    Logger.Instance.LogMessageToFile($"Unexpected error deleting old wallpaper: {ex.Message}", LogLevel.Warning);
                }
            }

            try
            {
                using (var wc = HelperMethods.CreateWebClient())
                {
                    await wc.DownloadFileTaskAsync(uri, fileName).ConfigureAwait(false);
                }
            }
            catch (WebException ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected Error: {ex.Message}", LogLevel.Error);
            }
        }

        private static async Task<string> ConvertRedditLinkToImageLink(string url, Random random, IEnumerable<string> imageExtensions)
        {
            var originalUri = new Uri(url);
            var originalExtension = Path.GetExtension(originalUri.LocalPath);
            var extensionNotImageType = !imageExtensions.Contains(originalExtension.ToUpper());

            if (url.Contains("imgur.com/a/"))
                return await GetImgurAlbumUrlAsync(originalUri, random).ConfigureAwait(false);
            else if (extensionNotImageType && url.Contains("deviantart"))
                return await GetDeviantArtUrlAsync(originalUri).ConfigureAwait(false);
            else if (extensionNotImageType && url.Contains("imgur.com"))
                return await GetImgurUrlAsync(originalUri).ConfigureAwait(false);

            return url;
        }

        private static async Task<string> GetDeviantArtUrlAsync(Uri uri)
        {
            var httpWebRequest = (HttpWebRequest)WebRequest.Create($"http://backend.deviantart.com/oembed?url={uri}");
            httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Accept = "*/*";
            httpWebRequest.Method = "GET";

            using (var httpResponse = (HttpWebResponse)await httpWebRequest.GetResponseAsync().ConfigureAwait(false))
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
            {
                var jsonResult = await streamReader.ReadToEndAsync().ConfigureAwait(false);

                return JToken.Parse(jsonResult)["url"].ToString();
            }
        }

        private static async Task<string> GetImgurAlbumUrlAsync(Uri uri, Random random)
        {
            var imgurId = new StringBuilder(uri.ToString()).Replace("https://", "")
                                                           .Replace("http://", "")
                                                           .Replace("imgur.com/a/", "")
                                                           .Replace("//", "")
                                                           .Replace("/", "")
                                                           .ToString();

            var imgurUri = new Uri($"https://api.imgur.com/3/album/{imgurId}");
            var jsonResult = await HelperMethods.GetImgurJsonStringAsync(imgurUri)
                                                .ConfigureAwait(false);

            var imgurResult = JToken.Parse(jsonResult)["data"]["images"];
            var i = imgurResult.Count();
            var selc = 0;

            if (i - 1 != 0)
                selc = random.Next(0, i - 1);

            return imgurResult.ElementAt(selc)["link"].ToString();
        }

        private static async Task<string> GetImgurUrlAsync(Uri uri)
        {
            var imgurId = new StringBuilder(uri.ToString()).Replace("https://", "")
                                                           .Replace("http://", "")
                                                           .Replace("imgur.com/", "")
                                                           .Replace("//", "")
                                                           .Replace("/", "")
                                                           .ToString();

            var baseUri = new Uri($"https://api.imgur.com/3/image/{imgurId}");

            var jsonResult = await HelperMethods.GetImgurJsonStringAsync(baseUri)
                                                .ConfigureAwait(false);

            return JToken.Parse(jsonResult)["data"]["link"].ToString();
        }

        // TODO refactor
        private static string GetRedditSearchUrl(Random random, MainThreadMarshaller uiMarshaller)
        {
            var query = WebUtility.UrlEncode(Settings.Default.searchQuery) + 
                "+self%3Ano+((url%3A.png+OR+url%3A.jpg+OR+url%3A.jpeg)+OR+(url%3Aimgur.png+OR+url%3Aimgur.jpg+OR+url%3Aimgur.jpeg)+OR+(url%3Adeviantart))";

            var subreddits = new StringBuilder(Settings.Default.subredditsUsed).Replace(" ", "")
                                                                               .Replace("www.reddit.com/", "")
                                                                               .Replace("reddit.com/", "")
                                                                               .Replace("http://", "")
                                                                               .Replace("/r/", "")
                                                                               .ToString();
            var subs = subreddits.Split('+');
            var sub = subs[random.Next(0, subs.Length)];

            uiMarshaller.UpdateStatus("Searching /r/" + sub + " for a wallpaper...");
            Logger.Instance.LogMessageToFile("Selected sub to search: " + sub, LogLevel.Information);

            var formURL = new StringBuilder("http://www.reddit.com/");

            if (!sub.Any())
                formURL.Append("r/all");
            else if (sub.Contains("/m/"))
                formURL.Append(subreddits)
                       .Replace("http://", "")
                       .Replace("https://", "")
                       .Replace("user/", "u/");
            else
                formURL.Append($"r/").Append(sub);

            switch (Settings.Default.wallpaperGrabType)
            {
                case (int)WallpaperGrabType.Random:
                    formURL.Append("/search.json?q=")
                           .Append(query)
                           .Append(SortValues[random.Next(0, 4)])
                           .Append(TopValues[random.Next(0, 5)])
                           .Append("&restrict_sr=on");
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
                case (int)WallpaperGrabType.Newest:
                    formURL.Append("/search.json?q=")
                           .Append(query)
                           .Append("&sort=new&restrict_sr=on");
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
                case (int)WallpaperGrabType.HotToday:
                    formURL.Append("/search.json?q=")
                           .Append(query)
                           .Append("&sort=hot&restrict_sr=on&t=day");
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
                case (int)WallpaperGrabType.TopLastHour:
                    formURL.Append("/search.json?q=")
                           .Append(query)
                           .Append("&sort=top&restrict_sr=on&t=hour");
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
                case (int)WallpaperGrabType.TopToday:
                    formURL.Append("/search.json?q=")
                           .Append(query)
                           .Append("&sort=top&restrict_sr=on&t=day");
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
                case (int)WallpaperGrabType.TopWeek:
                    formURL.Append("/search.json?q=")
                           .Append(query)
                           .Append("&sort=top&restrict_sr=on&t=week");
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
                case (int)WallpaperGrabType.TopMonth:
                    formURL.Append("/search.json?q=")
                           .Append(query)
                           .Append("&sort=top&restrict_sr=on&t=month");
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
                case (int)WallpaperGrabType.TopYear:
                    formURL.Append("/search.json?q=")
                           .Append(query)
                           .Append("&sort=top&restrict_sr=on&t=year");
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
                case (int)WallpaperGrabType.TopAllTime:
                    formURL.Append("/search.json?q=")
                           .Append(query)
                           .Append("&sort=top&restrict_sr=on&t=all");
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
                case (int)WallpaperGrabType.TrulyRandom:
                    formURL.Append("/random.json?p=").Append(Guid.NewGuid());
                    Logger.Instance.LogMessageToFile("Full URL Search String: " + formURL, LogLevel.Information);
                    break;
            }

            return formURL.ToString();
        }

        private static async Task<string> GetJsonDataAsync(string url, MainThreadMarshaller uiMarshaller)
        {
            try
            {
                Logger.Instance.LogMessageToFile("Searching Reddit for a wallpaper.", LogLevel.Information);

                using (var wc = HelperMethods.CreateWebClient())
                {
                    return await wc.DownloadStringTaskAsync(url).ConfigureAwait(false);
                }
            }
            catch (WebException ex)
            {
                uiMarshaller.LogFailure(ex.Message, $"Reddit server error: {ex.Message}", 
                    LogLevel.Error);

                throw;
            }
            catch (Exception ex)
            {
                uiMarshaller.LogFailure("Error downloading search results.", 
                    $"Error downloading search results: {ex.Message}", LogLevel.Error);

                throw;
            }
        }

        private static JToken GetRedditResult(JToken jToken)
        {
            if ((WallpaperGrabType)Settings.Default.wallpaperGrabType == WallpaperGrabType.TrulyRandom)
                return JToken.Parse(jToken.First.ToString())["data"]["children"];
            else
                return jToken["data"]["children"];
        }
    }
}
