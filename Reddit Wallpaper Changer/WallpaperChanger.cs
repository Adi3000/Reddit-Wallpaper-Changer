using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Reddit_Wallpaper_Changer
{
    public class WallpaperChanger
    {
        private const int MaxRetryAttempts = 50;

        private static readonly IReadOnlyList<string> TopValues = new List<string> { "&t=day", "&t=year", "&t=all", "&t=month", "&t=week" };
        private static readonly IReadOnlyList<string> SortValues = new List<string> { "&sort=relevance", "&sort=hot", "&sort=top", "&sort=comments", "&sort=new" };
        private static readonly IReadOnlyList<string> ImageExtensions = new List<string> { ".JPG", ".JPEG", ".BMP", ".GIF", ".PNG" };

        public MainThreadMarshaller UiMarshaller { private get; set; }

        private readonly Database _database;
        private readonly RedditClient _redditClient;
        private readonly Random _random = new Random();
        private readonly HashSet<string> _currentSessionHistory = new HashSet<string>();

        private int _noResultCount;

        public WallpaperChanger(Database database, RedditClient redditClient)
        {
            _database = database;
            _redditClient = redditClient;
        }

        //======================================================================
        // Set the wallpaper
        //======================================================================
        public async Task<bool> SetWallpaperAsync(RedditLink redditLink)
        {
            Logger.Instance.LogMessageToFile("Setting wallpaper.", LogLevel.Information);

            if (!WallpaperLinkValid(redditLink.ThreadId))
                return false;

            HelperMethods.ResetManualOverride();

            UiMarshaller.UpdateStatus("Setting Wallpaper");

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
                    var downloadSuccessful = await DownloadWallpaperAsync(uri.AbsoluteUri, wallpaperFile);

                    if (!downloadSuccessful || !await SetWallpaperAsync(redditLink, wallpaperFile))
                        return false;
                }
                else
                {
                    Logger.Instance.LogMessageToFile($"Wallpaper URL failed validation: {extension.ToUpper()}", LogLevel.Warning);

                    UiMarshaller.RestartChangeWallpaperTimer();
                }

                using (var wc = HelperMethods.CreateWebClient())
                {
                    var bytes = await wc.DownloadDataTaskAsync(uri).ConfigureAwait(false);
                    if (!bytes.Any())
                        UiMarshaller.RestartChangeWallpaperTimer();
                }
            }
            else
                UiMarshaller.RestartChangeWallpaperTimer();

            return true;
        }

        // TODO refactor
        //======================================================================
        // Search for a wallpaper
        //======================================================================
        public async Task SearchForWallpaperAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Logger.Instance.LogMessageToFile("Looking for a wallpaper.", LogLevel.Information);

                if (MaxRetriesExceeded())
                    return;

                UiMarshaller.UpdateStatus("Finding New Wallpaper");

                try
                {
                    var url = GetRedditSearchUrl(_random, UiMarshaller);
                    var jsonData = await GetJsonDataAsync(url).ConfigureAwait(false);

                    try
                    {
                        if (jsonData.Any())
                        {
                            var redditResult = GetRedditResult(JToken.Parse(jsonData));

                            JToken jToken = null;

                            try
                            {
                                foreach (var toke in redditResult.Reverse())
                                {
                                    jToken = toke;
                                }

                                if (jToken == null)
                                {
                                    if (redditResult.HasValues)
                                    {
                                        var randIndex = _random.Next(0, redditResult.Count() - 1);
                                        jToken = redditResult.ElementAt(randIndex);
                                    }
                                    else
                                    {
                                        ++_noResultCount;

                                        UiMarshaller.UpdateStatus("No results found, searching again.");
                                        Logger.Instance.LogMessageToFile("No search results, trying to change wallpaper again.", LogLevel.Information);

                                        continue;
                                    }
                                }

                                if ((WallpaperGrabType)Settings.Default.wallpaperGrabType == WallpaperGrabType.Random)
                                    jToken = redditResult.ElementAt(_random.Next(0, redditResult.Count() - 1));

                                var jTokenData = jToken["data"];

                                var redditLink = new RedditLink(
                                    jTokenData["title"].ToString(),
                                    jTokenData["id"].ToString(),
                                    jTokenData["url"].ToString())
                                {
                                    Permalink = jTokenData["permalink"].ToString()
                                };

                                if (!await ChangeWallpaperIfValidImageAsync(redditLink, token).ConfigureAwait(false))
                                    return;
                            }
                            catch (InvalidOperationException)
                            {
                                await UiMarshaller.LogFailureAsync("Your search query is bringing up no results.",
                                    "No results from the search query.");
                            }
                        }
                        else
                        {
                            await UiMarshaller.LogFailureAsync("Subreddit Probably Doesn't Exist",
                                "Subreddit probably does not exist.");

                            _noResultCount++;

                            return;
                        }
                    }
                    catch (JsonReaderException ex)
                    {
                        await UiMarshaller.LogFailureAsync($"Unexpected error: {ex.Message}",
                            $"Unexpected error: {ex.Message}", LogLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessageToFile(ex.Message, LogLevel.Warning);
                }

                return;
            }
        }

        private async Task<bool> SetWallpaperAsync(RedditLink redditLink, string wallpaperFile)
        {
            if (!await WallpaperSizeValidAsync(wallpaperFile))
                return false;

            await ActiveDesktop.SetWallpaperAsync(wallpaperFile).ConfigureAwait(false);

            _noResultCount = 0;

            UiMarshaller.UpdateStatus("Wallpaper Changed!");

            Logger.Instance.LogMessageToFile("Wallpaper changed!", LogLevel.Information);

            _currentSessionHistory.Add(redditLink.ThreadId);

            var thumbnail = await HelperMethods.GetThumbnailAsync(redditLink.Url).ConfigureAwait(false);

            _database.AddWallpaperToHistory(redditLink, thumbnail);

            HelperMethods.SaveToThumbnailCache(redditLink.ThreadId, thumbnail);

            if (Settings.Default.autoSave)
                HelperMethods.SaveCurrentWallpaper(Settings.Default.currentWallpaperName);

            UiMarshaller.SetWallpaperChanged(redditLink);

            return true;
        }

        private async Task<bool> WallpaperSizeValidAsync(string wallpaperFile)
        {
            if (Settings.Default.fitWallpaper)
            {
                var screen = ControlHelpers.GetScreenDimensions();

                using (var img = Image.FromFile(wallpaperFile))
                {
                    if (screen.Width != img.Width || screen.Height != img.Height)
                    {
                        await UiMarshaller.LogFailureAsync("Wallpaper resolution mismatch.",
                            $"Wallpaper size mismatch. Screen: {screen.Width}x{screen.Height}, Wallpaper: {img.Width}x{img.Height}");

                        _noResultCount++;
                        return false;
                    }
                }
            }

            return true;
        }

        private bool WallpaperLinkValid(string threadId)
        {
            if (_database.IsBlacklisted(threadId))
            {
                UiMarshaller.DisableAndUpdateStatus("Wallpaper is blacklisted.", "The selected wallpaper has been blacklisted, searching again.");
                return false;
            }

            if (!Settings.Default.manualOverride && 
                Settings.Default.suppressDuplicates && 
                _currentSessionHistory.Contains(threadId))
            {
                UiMarshaller.DisableAndUpdateStatus("Wallpaper already used this session.", "The selected wallpaper has already been used this session, searching again.");
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

            UiMarshaller.DisableChangeTimerNoResults(MaxRetryAttempts);

            return true;
        }

        private async Task<bool> ChangeWallpaperIfValidImageAsync(RedditLink redditLink, CancellationToken token)
        {
            Logger.Instance.LogMessageToFile($"Found a wallpaper! Title: {redditLink.Title}, URL: {redditLink.Url}, ThreadID: {redditLink.ThreadId}", LogLevel.Information);

            // Validate URL 
            if (await HelperMethods.ValidateImageAsync(redditLink).ConfigureAwait(false))
            {
                if (await HelperMethods.ValidateImgurImageAsync(redditLink.Url).ConfigureAwait(false))
                {
                    if (!await SetWallpaperAsync(redditLink).ConfigureAwait(false))
                    {
                        _noResultCount++;

                        await SearchForWallpaperAsync(token).ConfigureAwait(false);
                    }
                }
                else
                {
                    await UiMarshaller.LogFailureAsync("Wallpaper has been removed from Imgur.", 
                        "The selected wallpaper was deleted from Imgur, searching again.");

                    _noResultCount++;

                    await SearchForWallpaperAsync(token).ConfigureAwait(false);
                }
            }
            else
            {
                await UiMarshaller.LogFailureAsync("The selected URL is not for an image.", 
                    "Not a direct wallpaper URL, searching again.");

                _noResultCount++;
                return false;
            }

            return true;
        }

        private static async Task<bool> DownloadWallpaperAsync(string uri, string fileName)
        {
            try
            {
                using (var wc = HelperMethods.CreateWebClient())
                {
                    await wc.DownloadFileTaskAsync(uri, fileName).ConfigureAwait(false);
                }

                return true;
            }
            catch (WebException ex)
            {
                Logger.Instance.LogMessageToFile($"Unexpected Error: {ex.Message}", LogLevel.Error);
                return false;
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
            var formURL = GetBaseUrl(random, uiMarshaller);

            var query = "/search.json?q=" +
                WebUtility.UrlEncode(Settings.Default.searchQuery) +
                "+self%3Ano+((url%3A.png+OR+url%3A.jpg+OR+url%3A.jpeg)+OR+(url%3Aimgur.png+OR+url%3Aimgur.jpg+OR+url%3Aimgur.jpeg)+OR+(url%3Adeviantart))" +
                "&restrict_sr=on";

            if (Settings.Default.includeNsfw)
                query += "&include_over_18=on";

            switch ((WallpaperGrabType)Settings.Default.wallpaperGrabType)
            {
                case WallpaperGrabType.Random:
                    formURL.Append(query)
                           .Append(SortValues[random.Next(0, SortValues.Count)])
                           .Append(TopValues[random.Next(0, TopValues.Count)]);
                    break;
                case WallpaperGrabType.Newest:
                    formURL.Append(query).Append("&sort=new");
                    break;
                case WallpaperGrabType.HotToday:
                    formURL.Append(query).Append("&sort=hot&t=day");
                    break;
                case WallpaperGrabType.TopLastHour:
                    formURL.Append(query).Append("&sort=top&t=hour");
                    break;
                case WallpaperGrabType.TopToday:
                    formURL.Append(query).Append("&sort=top&t=day");
                    break;
                case WallpaperGrabType.TopWeek:
                    formURL.Append(query).Append("&sort=top&t=week");
                    break;
                case WallpaperGrabType.TopMonth:
                    formURL.Append(query).Append("&sort=top&t=month");
                    break;
                case WallpaperGrabType.TopYear:
                    formURL.Append(query).Append("&sort=top&t=year");
                    break;
                case WallpaperGrabType.TopAllTime:
                    formURL.Append(query).Append("&sort=top&t=all");
                    break;
                case WallpaperGrabType.TrulyRandom:
                    formURL.Append("/random.json?p=").Append(Guid.NewGuid());
                    break;
                default:
                    throw new InvalidEnumArgumentException(nameof(Settings.Default.wallpaperGrabType), Settings.Default.wallpaperGrabType, typeof(WallpaperGrabType));
            }

            var result = formURL.ToString();

            Logger.Instance.LogMessageToFile("Full URL Search String: " + result, LogLevel.Information);

            return result;
        }

        private static StringBuilder GetBaseUrl(Random random, MainThreadMarshaller uiMarshaller)
        {
            var subreddits = new StringBuilder(Settings.Default.subredditsUsed)
                .Replace(" ", "")
                .Replace("www.reddit.com/", "")
                .Replace("reddit.com/", "")
                .Replace("http://", "")
                .Replace("/r/", "")
                .ToString();

            var subs = subreddits.Split('+');
            var sub = subs[random.Next(0, subs.Length)];

            uiMarshaller.UpdateStatus("Searching " + sub + " for a wallpaper...");
            Logger.Instance.LogMessageToFile("Selected sub to search: " + sub, LogLevel.Information);

            if (sub.Contains("/m/"))
            {
                var multireddit = sub.Replace("http://", "")
                       .Replace("https://", "")
                       .Replace("user/", "u/");

                return new StringBuilder("http://www.reddit.com/")
                    .Append(multireddit);
            }
            else
            {
                return new StringBuilder("https://oauth.reddit.com/r/")
                    .Append(sub.Length > 0 ? sub : "all");
            }
        }

        private async Task<string> GetJsonDataAsync(string url)
        {
            try
            {
                Logger.Instance.LogMessageToFile("Searching Reddit for a wallpaper.", LogLevel.Information);

                return await _redditClient.GetJsonAsync(url, CancellationToken.None).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                await UiMarshaller.LogFailureAsync(ex.Message, $"Reddit server error: {ex}", 
                    LogLevel.Error);

                throw;
            }
            catch (Exception ex)
            {
                await UiMarshaller.LogFailureAsync("Error downloading search results.", 
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
