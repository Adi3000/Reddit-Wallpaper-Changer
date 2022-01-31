using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Reddit_Wallpaper_Changer
{
    public static class HelperMethods
    {
        public static void ResetManualOverride()
        {
            Settings.Default.manualOverride = false;
            Settings.Default.Save();
        }

        //======================================================================
        // Export user settings
        //======================================================================
        public static void ExportSettings(string fileName)
        {
            try
            {
                var version = Assembly.GetEntryAssembly().GetName().Version.ToString();

                var fileStream = new FileStream(fileName, FileMode.Create);

                var sw = new StreamWriter(fileStream);

                using (var writer = new XmlTextWriter(sw))
                {
                    writer.Formatting = Formatting.Indented;
                    writer.Indentation = 4;

                    writer.WriteStartDocument();
                    writer.WriteStartElement("RWC-Settings");
                    writer.WriteElementString("Version", version);
                    writer.WriteElementString("GrabType", Settings.Default.wallpaperGrabType.ToString());
                    writer.WriteElementString("Subreddits", Settings.Default.subredditsUsed);
                    writer.WriteElementString("SearchQuery", Settings.Default.searchQuery);
                    writer.WriteElementString("ChangeTimerValue", Settings.Default.changeTimeValue.ToString());
                    writer.WriteElementString("ChangeTimerType", Settings.Default.changeTimeType.ToString());
                    writer.WriteElementString("StartInTray", Settings.Default.startInTray.ToString());
                    writer.WriteElementString("AutoStart", Settings.Default.autoStart.ToString());
                    writer.WriteElementString("UseProxy", Settings.Default.useProxy.ToString());
                    writer.WriteElementString("ProxyServer", Settings.Default.proxyAddress);
                    writer.WriteElementString("ProxyAuthentication", Settings.Default.proxyAuth.ToString());
                    writer.WriteElementString("DefaultSaveLocation", Settings.Default.defaultSaveLocation);
                    writer.WriteElementString("AutoSave", Settings.Default.autoSave.ToString());
                    writer.WriteElementString("AutoSaveFaves", Settings.Default.autoSaveFaves.ToString());
                    writer.WriteElementString("WallpaperFade", Settings.Default.wallpaperFade.ToString());
                    writer.WriteElementString("DisableNotifications", Settings.Default.disableNotifications.ToString());
                    writer.WriteElementString("SuppressDuplicates", Settings.Default.suppressDuplicates.ToString());
                    writer.WriteElementString("ValidateWallpaperSize", Settings.Default.sizeValidation.ToString());
                    writer.WriteElementString("WallpaperInfoPopup", Settings.Default.wallpaperInfoPopup.ToString());
                    writer.WriteElementString("AutoUpdateCheck", Settings.Default.autoUpdateCheck.ToString());
                    writer.WriteElementString("WallpaperFit", Settings.Default.wallpaperStyle);
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }

                Logger.Instance.LogMessageToFile("Settings have been successfully exported to: " + fileName, LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Unexpected error exporting settings: " + ex.Message, LogLevel.Error);

                throw;
            }
        }

        //======================================================================
        // Import user settings
        //======================================================================
        public static void ImportSettings(string fileName)
        {
            var doc = new XmlDocument();
            doc.Load(fileName);

            var xnList = doc.SelectNodes("/RWC-Settings");
            foreach (XmlNode xn in xnList)
            {
                try
                {
                    Settings.Default.wallpaperGrabType = int.Parse(xn["GrabType"].InnerText);
                    Settings.Default.subredditsUsed = xn["Subreddits"].InnerText;
                    Settings.Default.searchQuery = xn["SearchQuery"].InnerText;
                    Settings.Default.changeTimeValue = Int32.Parse(xn["ChangeTimerValue"].InnerText);
                    Settings.Default.changeTimeType = Int32.Parse(xn["ChangeTimerType"].InnerText);
                    Settings.Default.startInTray = Boolean.Parse(xn["StartInTray"].InnerText);
                    Settings.Default.autoStart = Boolean.Parse(xn["AutoStart"].InnerText);
                    Settings.Default.useProxy = Boolean.Parse(xn["UseProxy"].InnerText);
                    Settings.Default.proxyAddress = xn["ProxyServer"].InnerText;
                    Settings.Default.proxyAuth = Boolean.Parse(xn["ProxyAuthentication"].InnerText);
                    Settings.Default.defaultSaveLocation = xn["DefaultSaveLocation"].InnerText;
                    Settings.Default.autoSave = Boolean.Parse(xn["AutoSave"].InnerText);
                    Settings.Default.autoSaveFaves = Boolean.Parse(xn["AutoSaveFaves"].InnerText);
                    Settings.Default.wallpaperFade = Boolean.Parse(xn["WallpaperFade"].InnerText);
                    Settings.Default.disableNotifications = Boolean.Parse(xn["DisableNotifications"].InnerText);
                    Settings.Default.suppressDuplicates = Boolean.Parse(xn["SuppressDuplicates"].InnerText);
                    Settings.Default.sizeValidation = Boolean.Parse(xn["ValidateWallpaperSize"].InnerText);
                    Settings.Default.wallpaperInfoPopup = Boolean.Parse(xn["WallpaperInfoPopup"].InnerText);
                    Settings.Default.autoUpdateCheck = Boolean.Parse(xn["AutoUpdateCheck"].InnerText);
                    Settings.Default.wallpaperStyle = xn["WallpaperFit"].InnerText;

                    Settings.Default.Save();

                    Logger.Instance.LogMessageToFile("Settings have been successfully imported. Restarting RWC.", LogLevel.Information);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessageToFile("Unexpected error importing settings file: " + ex.Message, LogLevel.Error);

                    throw;
                }
            }
        }

        //======================================================================
        // Set up proxy along with any credntials required for auth
        //======================================================================
        public static WebClient CreateWebClient()
        {
            var wc = new DecompressableWebClient();

            if (Settings.Default.useProxy)
            {
                try
                {
                    var proxy = new WebProxy(Settings.Default.proxyAddress);

                    if (Settings.Default.proxyAuth)
                    {
                        proxy.Credentials = new NetworkCredential(Settings.Default.proxyUser, Settings.Default.proxyPass);
                        proxy.UseDefaultCredentials = false;
                        proxy.BypassProxyOnLocal = false;
                    }

                    wc.Proxy = proxy;
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessageToFile("Unexpeced error setting proxy: " + ex.Message, LogLevel.Error);
                }
            }

            return wc;
        }

        public static async Task<bool> UploadLogToPastebinAsync()
        {
            Settings.Default.logUrl = "";
            Settings.Default.Save();

            var data = new NameValueCollection
            {
                ["api_paste_name"] = $"RWC_Log_{DateTime.Now}.log",
                ["api_paste_expire_date"] = "N",
                ["api_paste_code"] = File.ReadAllText($@"{Settings.Default.AppDataPath}\Logs\RWC.log"),
                ["api_dev_key"] = "017c00e3a11ee8c70499c1f4b6b933f0",
                ["api_option"] = "paste"
            };

            try
            {
                byte[] bytes;

                using (var wc = CreateWebClient())
                {
                    bytes = await wc.UploadValuesTaskAsync("https://pastebin.com/api/api_post.php", data).ConfigureAwait(false);
                }

                var response = System.Text.Encoding.UTF8.GetString(bytes);

                if (response.StartsWith("Bad API request"))
                {
                    Logger.Instance.LogMessageToFile("Failed to upload log to Pastebin: " + response, LogLevel.Information);
                    return false;
                }
                else
                {
                    Logger.Instance.LogMessageToFile("Logfile successfully uploaded to Pastebin: " + response, LogLevel.Information);
                    Settings.Default.logUrl = response;
                    Settings.Default.Save();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Error uploading logfile to Pastebin: " + ex.Message, LogLevel.Error);
                return false;
            }
        }

        //======================================================================
        // Check that the selected wallpaper URL is for an image
        //======================================================================
        public static async Task<bool> ValidateImageAsync(RedditLink redditLink)
        {
            try
            {
                if (redditLink.Url.Contains("deviantart"))
                    return true;
                else
                {
                    Logger.Instance.LogMessageToFile("Checking to ensure the chosen wallpaper URL is for an image.", LogLevel.Information);

                    var imageCheck = (HttpWebRequest)WebRequest.Create(redditLink.Url);
                    imageCheck.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    imageCheck.Timeout = 5000;
                    imageCheck.Method = "HEAD";
                    imageCheck.MaximumAutomaticRedirections = 1;

                    using (var imageResponse = await imageCheck.GetResponseAsync().ConfigureAwait(false))
                    {
                        // If anything other than OK, assume that image has been deleted
                        if (imageResponse.ContentType.StartsWith("image/"))
                        {
                            imageCheck.Abort();
                            Logger.Instance.LogMessageToFile("The chosen URL is for an image.", LogLevel.Information);
                            
                            redditLink.Url = imageResponse.ResponseUri.AbsoluteUri;
                            
                            return true;
                        }
                        else
                        {
                            imageCheck.Abort();
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile(ex.Message, LogLevel.Error);

                return false;
            }
        }

        //======================================================================
        // If the URL is an Imgur link, check the Wallpaper is still available
        //======================================================================
        public static async Task<bool> ValidateImgurImageAsync(string url)
        {
            try
            {
                if (url.Contains("imgur"))
                {
                    Logger.Instance.LogMessageToFile("Checking to ensure the chosen wallpaper is still available on Imgur.", LogLevel.Information);

                    // A request for a deleted image on Imgur will return status code 302 & redirect to http://i.imgur.com/removed.png returning status code 200
                    var imgurRequest = (HttpWebRequest)WebRequest.Create(url);
                    imgurRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    imgurRequest.Timeout = 5000;
                    imgurRequest.Method = "HEAD";
                    imgurRequest.AllowAutoRedirect = false;

                    using (var imgurResponse = (HttpWebResponse)await imgurRequest.GetResponseAsync().ConfigureAwait(false))
                    {
                        if (imgurResponse.StatusCode == HttpStatusCode.OK)
                        {
                            imgurRequest.Abort();
                            Logger.Instance.LogMessageToFile("The chosen wallpaper is still available on Imgur.", LogLevel.Information);
                            return true;
                        }
                        else
                        {
                            imgurRequest.Abort();
                            return false;
                        }
                    }
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        //======================================================================
        // Save current wallpaper
        //======================================================================
        public static bool SaveCurrentWallpaper(string fileName)
        {
            try
            {
                var parsedFileName = StripInvalidCharactersFromFileName(fileName);

                if (parsedFileName != fileName)
                    Logger.Instance.LogMessageToFile($"Removed illegal characters from post title: {parsedFileName}", LogLevel.Information);

                var fullFilePath = $@"{Settings.Default.defaultSaveLocation}\{parsedFileName}";

                if (!File.Exists(fullFilePath))
                    File.Copy(Settings.Default.currentWallpaperFile, fullFilePath);
                else
                    Logger.Instance.LogMessageToFile($"Not auto saving {parsedFileName} because it already exists.", LogLevel.Warning);

                return true;
            }
            catch (Exception Ex)
            {
                Logger.Instance.LogMessageToFile($"Error Saving Wallpaper: {Ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public static string StripInvalidCharactersFromFileName(string fileName)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c.ToString(), "_");
            }

            return fileName;
        }

        //======================================================================
        // Generate thumbnail of the wallpaper and convert to base64 to store in db
        //======================================================================
        public static async Task<string> GetThumbnailAsync(string URL)
        {
            try
            {
                using (var wc = CreateWebClient())
                {
                    try
                    {
                        var bytes = await wc.DownloadDataTaskAsync(URL).ConfigureAwait(false);

                        return ConvertImageBytesToBase64String(bytes);
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.LogMessageToFile("Unexpected error generating wallpaper thumbnail: " + ex.Message, LogLevel.Warning);
                        return "";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Unexpected error generating wallpaper thumbnail: " + ex.Message, LogLevel.Warning);
                return "";
            }
        }

        public static string ConvertImageBytesToBase64String(byte[] wallpaperBytes)
        {
            var wallpaperStream = new MemoryStream(wallpaperBytes);
            var bitmapWidth = 150;
            var bitmapHeight = 70;

            using (var wallpaperBitmap = new Bitmap(wallpaperStream))
            using (var thumbnailBitmap = new Bitmap(bitmapWidth, bitmapHeight))
            using (var graphics = Graphics.FromImage(thumbnailBitmap))
            using (var thumbnailStream = new MemoryStream())
            {
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(wallpaperBitmap, new Rectangle(0, 0, bitmapWidth, bitmapHeight));

                ImageCodecInfo jpegCodec = null;
                foreach (var codec in ImageCodecInfo.GetImageDecoders())
                {
                    if (codec.FormatID == ImageFormat.Jpeg.Guid)
                    {
                        jpegCodec = codec;
                        break;
                    }
                }

                if (jpegCodec == null)
                    throw new Exception("Unable to find Jpeg decoder");

                var encoderParameters = new EncoderParameters();
                encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, 100L);

                thumbnailBitmap.Save(thumbnailStream, jpegCodec, encoderParameters);

                return Convert.ToBase64String(thumbnailStream.ToArray());
            }
        }

        public static Task StartSTATask(Action action)
        {
            var source = new TaskCompletionSource<object>();

            var thread = new Thread(() =>
            {
                try
                {
                    action();

                    source.SetResult(null);
                }
                catch (Exception ex)
                {
                    source.SetException(ex);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return source.Task;
        }

        public static async Task<string> GetImgurJsonStringAsync(Uri uri)
        {
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);
            httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Accept = "*/*";
            httpWebRequest.Method = "GET";
            httpWebRequest.Headers.Add("Authorization", "Client-ID 355f2ab533c2ac7");

            using (var httpResponse = (HttpWebResponse)await httpWebRequest.GetResponseAsync().ConfigureAwait(false))
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
            {
                return await streamReader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        //======================================================================
        // Remove all thumbnails
        //======================================================================
        public static void RemoveThumbnailCache()
        {
            try
            {
                Logger.Instance.LogMessageToFile("Removing thumbnail cache", LogLevel.Information);

                var dir = new DirectoryInfo(Settings.Default.thumbnailCache);

                foreach (var file in dir.EnumerateFiles("*.jpg"))
                {
                    file.Delete();
                }

                Settings.Default.rebuildThumbCache = false;
                Settings.Default.Save();

                Logger.Instance.LogMessageToFile("Thumbnail cache erased.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Error rebuilding thumbnail cache: " + ex.Message, LogLevel.Warning);
            }
        }

        //======================================================================
        // Set up a thumbnail cache
        //======================================================================
        public static void SetupThumbnailCache()
        {
            string thumbnailCachePath;
            if (Settings.Default.thumbnailCache.Any())
                thumbnailCachePath = Settings.Default.thumbnailCache;
            else
                thumbnailCachePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Reddit Wallpaper Changer\ThumbnailCache";

            Directory.CreateDirectory(thumbnailCachePath);

            Settings.Default.thumbnailCache = thumbnailCachePath;
            Settings.Default.Save();
        }

        //======================================================================
        // Log startup info
        //======================================================================
        public static void LogSettings(string changeTimeType, int screenCount)
        {
            Logger.Instance.LogMessageToFile("Auto Start: " + Settings.Default.autoStart, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Start In Tray: " + Settings.Default.startInTray, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Proxy Enabled: " + Settings.Default.useProxy, LogLevel.Information);

            if (Settings.Default.useProxy)
            {
                Logger.Instance.LogMessageToFile("Proxy Address:" + Settings.Default.proxyAddress, LogLevel.Information);
                Logger.Instance.LogMessageToFile("Proxy Authentication: " + Settings.Default.proxyAuth, LogLevel.Information);
            }

            Logger.Instance.LogMessageToFile("AppData Directory: " + Settings.Default.AppDataPath, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Thumbnail Cache: " + Settings.Default.thumbnailCache, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Automatically check for updates: " + Settings.Default.autoUpdateCheck, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Save location for wallpapers: " + Settings.Default.defaultSaveLocation, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Auto Save Favourite Wallpapers: " + Settings.Default.autoSaveFaves, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Auto Save All Wallpapers: " + Settings.Default.autoSave, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Wallpaper Grab Type: " + Settings.Default.wallpaperGrabType, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Selected Subreddits: " + Settings.Default.subredditsUsed, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Wallpaper Fade Effect: " + Settings.Default.wallpaperFade, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Search Query: " + Settings.Default.searchQuery, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Change wallpaper every " + Settings.Default.changeTimeValue + " " + changeTimeType, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Number of detected displays: " + screenCount, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Wallpaper Position: " + Settings.Default.wallpaperStyle, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Validate wallpaper size: " + Settings.Default.fitWallpaper, LogLevel.Information);
            Logger.Instance.LogMessageToFile("Wallpaper Info Popup: " + Settings.Default.wallpaperInfoPopup, LogLevel.Information);
        }

        //======================================================================
        // Set folder path for saving wallpapers
        //======================================================================
        public static void SetupSavedWallpaperLocation()
        {
            if (Settings.Default.defaultSaveLocation.Any()) return;

            var savedWallpaperPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) + @"\Saved Wallpapers";

            Directory.CreateDirectory(savedWallpaperPath);

            Settings.Default.defaultSaveLocation = savedWallpaperPath;
            Settings.Default.Save();
        }
    }
}
