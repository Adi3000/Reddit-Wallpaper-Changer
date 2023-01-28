using Reddit_Wallpaper_Changer.DTOs;
using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer
{
    public static class ControlHelpers
    {
        public static async Task SaveLinkAsync(ToolStripStatusLabel statusLabel, NotifyIcon taskIcon, RedditLink redditLink)
        {
            string statusText;

            if (await redditLink.SaveImage())
            {
                ShowWallpaperSavedBalloonTip(taskIcon);
                statusText = "Wallpaper saved!";
            }
            else
            {
                ShowWallpaperAlreadyExistsBalloonTip(taskIcon);
                statusText = "Wallpaper already saved!";
            }

            statusLabel.Text = statusText;
        }

        public static RedditLink CreateRedditLinkFromGrid(DataGridView dataGrid, int currentRow)
        {
            var row = dataGrid.Rows[currentRow];

            return new RedditLink
            (
                row.Cells[3].Value.ToString(), // url
                row.Cells[1].Value.ToString(), // title
                row.Cells[2].Value.ToString()  // threadId
            );
        }

        //======================================================================
        // Show system tray message
        //======================================================================
        public static void ShowTrayMsg(NotifyIcon taskIcon)
        {
            var details = new BalloonTipDetails(ToolTipIcon.Info, "Reddit Wallpaper Changer", 
                "Down here if you need me!", 700);

            ShowBalloonTip(taskIcon, details);
        }

        //======================================================================
        // Update timer 
        //======================================================================
        public static void UpdateTimer(Timer wallpaperChangeTimer)
        {
            wallpaperChangeTimer.Enabled = false;

            switch ((ChangeTimeType)Settings.Default.changeTimeType)
            {
                case ChangeTimeType.Minutes:
                    wallpaperChangeTimer.Interval = (int)TimeSpan.FromMinutes(Settings.Default.changeTimeValue).TotalMilliseconds;
                    break;
                case ChangeTimeType.Hours:
                    wallpaperChangeTimer.Interval = (int)TimeSpan.FromHours(Settings.Default.changeTimeValue).TotalMilliseconds;
                    break;
                default:
                    wallpaperChangeTimer.Interval = (int)TimeSpan.FromDays(Settings.Default.changeTimeValue).TotalMilliseconds;
                    break;
            }

            wallpaperChangeTimer.Enabled = true;
        }

        public static void ShowBalloonTip(NotifyIcon notifyIcon, BalloonTipDetails details)
        {
            if (Settings.Default.disableNotifications) return;

            notifyIcon.BalloonTipIcon = details.ToolTipIcon;
            notifyIcon.BalloonTipTitle = details.Title;
            notifyIcon.BalloonTipText = details.Text;
            notifyIcon.ShowBalloonTip(details.Duration);
        }

        public static void ShowWallpaperSavedBalloonTip(NotifyIcon taskIcon)
        {
            var details = new BalloonTipDetails(ToolTipIcon.Info, "Wallpaper Saved!", 
                $"Wallpaper saved to {Settings.Default.defaultSaveLocation}", 750);

            ShowBalloonTip(taskIcon, details);
        }

        public static void ShowWallpaperAlreadyExistsBalloonTip(NotifyIcon taskIcon)
        {
            var details = new BalloonTipDetails(ToolTipIcon.Info, "Already Saved!", 
                "No need to save this wallpaper as it already exists in your wallpapers folder! :)", 
                750);

            ShowBalloonTip(taskIcon, details);
        }

        //======================================================================
        // Add a button for each attached monitor 
        //======================================================================
        public static void UpdateMonitorPanelScreens(TableLayoutPanel monitorPanel)
        {
            // Create list of controls and then remove them all
            var controlsToRemove = new List<Control>();

            foreach (Control item in monitorPanel.Controls.OfType<PictureBox>())
            {
                controlsToRemove.Add(item);
            }

            foreach (Control item in monitorPanel.Controls.OfType<Label>())
            {
                controlsToRemove.Add(item);
            }

            foreach (Control item in controlsToRemove)
            {
                monitorPanel.Controls.Remove(item);
                item.Dispose();
            }

            // Get number of attached monitors
            var screens = Screen.AllScreens.Length;

            // Auto add a table to nest the monitor images and labels
            monitorPanel.Refresh();
            monitorPanel.ColumnStyles.Clear();
            monitorPanel.RowStyles.Clear();
            monitorPanel.ColumnCount = screens;
            monitorPanel.RowCount = 2;
            monitorPanel.AutoSize = true;

            var z = 0;
            foreach (var screen in Screen.AllScreens.OrderBy(i => i.Bounds.X))
            {
                monitorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / screens));

                var monitorImg = Resources.display_enabled;
                var x = 100;
                var y = 75;

                if (screens >= 4)
                {
                    monitorImg = Resources.display_enabled_small;
                    x = 64;
                    y = 64;
                }

                var monitor = new PictureBox
                {
                    Name = "MonitorPic" + z,
                    Size = new Size(x, y),
                    BackgroundImageLayout = ImageLayout.Stretch,
                    BackgroundImage = monitorImg,
                    Anchor = AnchorStyles.None,
                };

                var resolution = new Label
                {
                    Name = "MonitorLabel" + z,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9),
                    ForeColor = Color.Black,
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Text = "DISPLAY " + z + "\r\n" + screen.Bounds.Width + "x" + screen.Bounds.Height,
                    Anchor = AnchorStyles.None,
                };

                monitorPanel.Controls.Add(monitor, z, 0);
                monitorPanel.Controls.Add(resolution, z, 1);

                z++;
            }
        }

        //======================================================================
        // Set the example wallpaper image
        //======================================================================
        public static void SetExample(PictureBox stylePictureBox, string style)
        {
            switch (style)
            {
                case "Fill":
                    stylePictureBox.Image = Resources.fill;
                    break;
                case "Fit":
                    stylePictureBox.Image = Resources.fit;
                    break;
                case "Span":
                    stylePictureBox.Image = Resources.span;
                    break;
                case "Stretch":
                    stylePictureBox.Image = Resources.stretch;
                    break;
                case "Tile":
                    stylePictureBox.Image = Resources.tile;
                    break;
                case "Center":
                    stylePictureBox.Image = Resources.centre;
                    break;
            }
        }

        public static ScreenDimensions GetScreenDimensions() 
            => new ScreenDimensions(SystemInformation.VirtualScreen.Width, 
                SystemInformation.VirtualScreen.Height);

        //======================================================================
        // Populate the History panel
        //======================================================================
        public static bool PopulateHistory(DataGridView historyGrid, Database database)
        {
            historyGrid.Rows.Clear();

            Logger.Instance.LogMessageToFile("Refreshing History panel.", LogLevel.Information);

            try
            {
                foreach (var item in database.GetFromHistory())
                {
                    AddImageToHistoryDataGrid(historyGrid, item);
                }

                Logger.Instance.LogMessageToFile("History panel reloaded.", LogLevel.Information);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Error populating history panel: " + ex.Message, LogLevel.Warning);
                return false;
            }
        }

        public static void InsertImageInHistoryDataGrid(DataGridView historyDataGrid, RedditImage image)
        {
            historyDataGrid.Rows.Insert(0, 1);

            UpdateRow(historyDataGrid, image, 0);
        }

        public static void AddImageToHistoryDataGrid(DataGridView historyDataGrid, RedditImage image)
        {
            UpdateRow(historyDataGrid, image, historyDataGrid.Rows.Add());
        }

        private static void UpdateRow(DataGridView historyDataGrid, RedditImage image, int index)
        {
            Bitmap bitmap;
            if (File.Exists(Settings.Default.thumbnailCache + @"\" + image.ThreadId + ".jpg"))
                bitmap = GetBitmap(image.ThreadId);
            else
                bitmap = Resources.null_thumb;

            historyDataGrid.Rows[index].SetValues(bitmap, image.Title, image.ThreadId, image.Url, image.Date);
        }

        //======================================================================
        // Populate the blacklisted history panel
        //======================================================================
        public static void PopulateBlacklist(DataGridView blacklistGrid, Database database)
        {
            Logger.Instance.LogMessageToFile("Refreshing blacklisted panel.", LogLevel.Information);

            blacklistGrid.Rows.Clear();

            try
            {
                foreach (var item in database.GetFromBlacklist())
                {
                    var index = blacklistGrid.Rows.Add();
                    var row = blacklistGrid.Rows[index];

                    UpdateDataGridRowValues(row, item);
                }

                Logger.Instance.LogMessageToFile("Blacklisted wallpapers loaded.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Error populating blacklist panel: " + ex.Message, LogLevel.Warning);
            }
        }

        //======================================================================
        // Populate the Favourites panel
        //======================================================================
        public static void PopulateFavourites(DataGridView favouritesGrid, Database database)
        {
            Logger.Instance.LogMessageToFile("Refreshing Favourites panel.", LogLevel.Information);

            favouritesGrid.Rows.Clear();

            try
            {
                foreach (var item in database.GetFromFavourites())
                {
                    var index = favouritesGrid.Rows.Add();
                    var row = favouritesGrid.Rows[index];

                    UpdateDataGridRowValues(row, item);
                }

                Logger.Instance.LogMessageToFile("Favourite wallpapers loaded.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Error populating favourites panel: " + ex.Message, LogLevel.Warning);
            }
        }

        public static void UpdateDataGridRowValues(DataGridViewRow row, RedditImage redditImage)
        {
            var bitmap = GetBitmap(redditImage.ThreadId);

            row.SetValues(bitmap, redditImage.Title, redditImage.ThreadId, redditImage.Url, redditImage.Date);
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Bitmap> BitmapCache = new Dictionary<string, Bitmap>();

        private static Bitmap GetBitmap(string threadId)
        {
            lock (CacheLock)
            {
                if (!BitmapCache.TryGetValue(threadId, out var result))
                {
                    using (var fs = new FileStream($@"{Settings.Default.thumbnailCache}\{threadId}.jpg", FileMode.Open, FileAccess.Read))
                    using (var tempImage = Image.FromStream(fs))
                    {
                        result = new Bitmap(tempImage);
                    }

                    BitmapCache[threadId] = result;
                }

                return result;
            }
        }

        public static void SetSubredditTypeLabel(Label label, string subredditText)
        {
            //Change Label if it is a Multi Reddit.
            if (subredditText.Contains("/m/"))
            {
                label.Text = "MultiReddit";
                label.ForeColor = Color.Red;
            }
            else
            {
                label.Text = "Subreddit(s):";
                label.ForeColor = Color.Black;
            }
        }
    }
}
