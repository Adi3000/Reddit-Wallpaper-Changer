using Microsoft.Win32;
using Reddit_Wallpaper_Changer.DTOs;
using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer.Forms
{
    public sealed partial class RWC : Form
    {
        private readonly CancellationTokenSource _closeCts = new CancellationTokenSource();
        private readonly Dictionary<EventHandler, HotKey> _hotkeys = new Dictionary<EventHandler, HotKey>();
        private readonly Database _database;
        private readonly MainThreadMarshaller _mainThreadMarshaller;
        private readonly WallpaperChanger _wallpaperChanger;
        private readonly TabSelector _tabSelector;

        private readonly string _currentVersion;

        private ToolTip _toolTip;

        private int _currentMouseOverRow;
        private bool _enabledOnSleep;
        private bool _showedTray;

        public string CurrentThread { private get; set; }

        public RWC()
        {
            InitializeComponent();

            _currentVersion = Assembly.GetEntryAssembly().GetName().Version.ToString().Trim();

            string appDataFolderPath;
            if (Settings.Default.AppDataPath.Any())
                appDataFolderPath = Settings.Default.AppDataPath;
            else
                appDataFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Reddit Wallpaper Changer";

            Directory.CreateDirectory(appDataFolderPath);
            Settings.Default.AppDataPath = appDataFolderPath;
            Settings.Default.Save();

            _database = new Database(appDataFolderPath);
            _tabSelector = new TabSelector(configurePanel, configureButton);
            _mainThreadMarshaller = new MainThreadMarshaller(this, SynchronizationContext.Current);
            _wallpaperChanger = new WallpaperChanger(_mainThreadMarshaller, _database);

            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();

                _toolTip?.Dispose();

                UnregisterHotkeys();

                _closeCts.Cancel();
            }

            base.Dispose(disposing);
        }

        #region Public Methods

        public void OpenPopupInfoWindow(RedditLink redditLink)
        {
            var formx = 300;
            var formy = 90;

            var screenx = Screen.PrimaryScreen.Bounds.Width;
            var screeny = Screen.PrimaryScreen.Bounds.Height;

            var popupx = screenx - formx - 50;
            var popupy = screeny - formy - 50;

            var point = new Point(popupx, popupy);

            new PopupInfo(redditLink.ThreadId, redditLink.Title)
            {
                Location = point
            }
            .Show();
        }

        public void AddImageToHistory(RedditImage image)
            => ControlHelpers.InsertImageInHistoryDataGrid(historyDataGrid, image);

        public void UpdateStatus(string text) => statuslabel1.Text = text;

        public void DisableChangeWallpaperTimer() => changeWallpaperTimer.Enabled = false;

        public void RestartBreakBetweenChangeTimer() => breakBetweenChange.Enabled = true;

        public void RestartChangeWallpaperTimer() => changeWallpaperTimer.Enabled = true;

        public void ShowNoResultsBalloonTip(int retryCount)
        {
            var details = new BalloonTipDetails(ToolTipIcon.Info, "Reddit Wallpaper Changer", $"No results after {retryCount} attempts. Disabling Reddit Wallpaper Changer.", 700);
            ControlHelpers.ShowBalloonTip(taskIcon, details);
        }

        #endregion

        #region Event handlers

        // TODO refactor
        //======================================================================
        // Form load code
        //======================================================================
        private async void RWC_Load(object sender, EventArgs e)
        {
            SetToolTips();

            // Copy user settings from previous application version if necessary (part of the upgrade process)
            if (Settings.Default.updateSettings)
            {
                Settings.Default.Upgrade();
                Settings.Default.updateSettings = false;
                Settings.Default.Save();
            }

            Logger.Instance.LogMessageToFile("===================================================================================================================", LogLevel.Information);

            var random = new Random();
            var db = random.Next(0, 1000);
            if (db == 500) { SuperSecret.DickButt(); }

            Logger.Instance.LogMessageToFile("Reddit Wallpaper Changer Version " + Assembly.GetEntryAssembly().GetName().Version.ToString(), LogLevel.Information);
            Logger.Instance.LogMessageToFile("RWC is starting.", LogLevel.Information);
            Logger.Instance.LogMessageToFile("RWC Interface Loaded.", LogLevel.Information);

            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;

            FormClosing += RWC_FormClosing;
            Size = new Size(466, 531);

            statuslabel1.Text = "RWC Setup Initating.";

            taskIcon.Visible = true;

            if (Settings.Default.rebuildThumbCache)
                HelperMethods.RemoveThumbnailCache();

            HelperMethods.SetupSavedWallpaperLocation();

            txtSavePath.Text = Settings.Default.defaultSaveLocation;
            chkAutoSave.Checked = Settings.Default.autoSave;

            HelperMethods.SetupThumbnailCache();

            SetupProxySettings();
            SetupButtons();
            SetupPanels();
            SetupOthers();
            SetupHotkeys();

            ControlHelpers.SetSubredditTypeLabel(label5, subredditTextBox.Text);
            HelperMethods.LogSettings(changeTimeType.Text, Screen.AllScreens.Length);

            _database.InitialiseDatabase();

            if (!Settings.Default.dbMigrated)
                await _database.MigrateOldBlacklistAsync();

            _database.BuildThumbnailCache();

            ControlHelpers.PopulateHistory(historyDataGrid, _database);
            ControlHelpers.PopulateFavourites(favouritesDataGrid, _database);
            ControlHelpers.PopulateBlacklist(blacklistDataGrid, _database);

            statuslabel1.Text = "RWC Setup Initiated.";

            checkInternetTimer.Enabled = true;

            if (!chkStartInTray.Checked) ShowForm();
        }

        //======================================================================
        // Code for if the computer sleeps or wakes up
        //======================================================================
        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            Logger.Instance.LogMessageToFile("Device suspended. Going to sleep.", LogLevel.Information);

            if (e.Mode == PowerModes.Suspend)
            {
                if (wallpaperChangeTimer.Enabled)
                {
                    _enabledOnSleep = true;
                    wallpaperChangeTimer.Enabled = false;
                }
            }
            else if (e.Mode == PowerModes.Resume)
            {
                Logger.Instance.LogMessageToFile("Device resumed. Back in action!", LogLevel.Information);

                if (_enabledOnSleep)
                    wallpaperChangeTimer.Enabled = true;
            }
        }

        //======================================================================
        // Config button clicked
        //======================================================================
        private void ConfigureButton_Click(object sender, EventArgs e) 
            => _tabSelector.Select(configurePanel, configureButton);

        //======================================================================
        // Open the About panel
        //======================================================================
        private void AboutButton_Click(object sender, EventArgs e) 
            => _tabSelector.Select(aboutPanel, aboutButton);

        //======================================================================
        // History button click
        //======================================================================
        private void HistoryButton_Click(object sender, EventArgs e) 
            => _tabSelector.Select(historyPanel, historyButton);

        //======================================================================
        // Open the Blacklisted panel
        //======================================================================
        private void BlacklistButton_Click(object sender, EventArgs e) 
            => _tabSelector.Select(blacklistPanel, blacklistButton);

        //======================================================================
        // Open the Favourites panel
        //======================================================================
        private void FavouritesButton_Click(object sender, EventArgs e) 
            => _tabSelector.Select(favouritesPanel, favouritesButton);

        //======================================================================
        // Monitor button click
        //======================================================================
        private void MonitorButton_Click(object sender, EventArgs e)
        {
            if (monitorPanel.Visible) return;

            _tabSelector.Select(monitorPanel, monitorButton);

            ControlHelpers.UpdateMonitorPanelScreens(monitorLayoutPanel);

            comboType.Text = Settings.Default.wallpaperStyle;

            ControlHelpers.SetExample(picStyles, comboType.Text);
        }

        //======================================================================
        // Go to Ugleh's Reddit page
        //======================================================================
        private void RedditLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) 
            => Process.Start("http://www.reddit.com/user/Ugleh/").Dispose();

        // TODO refactor
        //======================================================================
        // Check for updates to the software
        //======================================================================
        private async void BtnUpdate_Click(object sender, EventArgs e)
        {
            Logger.Instance.LogMessageToFile("Manual check for updates initiated.", LogLevel.Information);

            btnUpdate.Enabled = false;
            btnUpdate.Text = "Checking....";

            using (var wc = HelperMethods.CreateWebClient())
            {
                try
                {
                    var latestVersion = await wc.DownloadStringTaskAsync("https://raw.githubusercontent.com/qwertydog/Reddit-Wallpaper-Changer/master/version");

                    if (!latestVersion.ToString().Contains(_currentVersion))
                    {
                        Logger.Instance.LogMessageToFile($"Current Version: {_currentVersion}. Latest version: {latestVersion}", LogLevel.Information);

                        var choice = MessageBox.Show($"You are running version {_currentVersion}.\r\n\r\nDownload version {latestVersion.Split(new[] { '\r', '\n' }).FirstOrDefault()} now?", 
                            "Update Available!", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                        if (choice == DialogResult.Yes)
                            UpdateAndClose(latestVersion);
                        else if (choice == DialogResult.No)
                        {
                            btnUpdate.Enabled = true;
                            btnUpdate.Text = "Check for Updates";
                            return;
                        }
                    }
                    else
                    {
                        Logger.Instance.LogMessageToFile("Reddit Wallpaper Changer is up to date (" + _currentVersion + ")", LogLevel.Information);

                        var details = new BalloonTipDetails(ToolTipIcon.Info, "Reddit Wallpaper Changer", "RWC is up to date! :)", 700);
                        ControlHelpers.ShowBalloonTip(taskIcon, details);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessageToFile("Error checking for updates: " + ex.Message, LogLevel.Error);

                    var details = new BalloonTipDetails(ToolTipIcon.Error, "Reddit Wallpaper Changer", "Error checking for updates! :(", 700);
                    ControlHelpers.ShowBalloonTip(taskIcon, details);
                }
            }

            btnUpdate.Text = "Check For Updates";
            btnUpdate.Enabled = true;
        }

        private void UpdateAndClose(string latestVersion)
        {
            var updateForm = new Update(latestVersion);

            updateForm.FormClosing += (s, ev) => { changeWallpaperTimer.Enabled = true; };
            updateForm.OnUpdated += (s, ev) => Exit();
            updateForm.Show();
        }

        //======================================================================
        // Open the RWC Subreddit
        //======================================================================
        private void BtnSubreddit_Click(object sender, EventArgs e) 
            => Process.Start("http://www.reddit.com/r/rwallpaperchanger/").Dispose();

        //======================================================================
        // Save all settings
        //======================================================================
        private void SaveButton_Click(object sender, EventArgs e)
        {
            Logger.Instance.LogMessageToFile("Settings successfully saved.", LogLevel.Information);
            Logger.Instance.LogMessageToFile("New settings...", LogLevel.Information);

            SaveData();

            statuslabel1.Text = "Settings Saved!";
        }

        //======================================================================
        // Start the timer for regular wallpaper changing
        ////======================================================================
        private void WallpaperChangeTimer_Tick(object sender, EventArgs e) 
            => changeWallpaperTimer.Enabled = true;

        //======================================================================
        // Closing the form
        //======================================================================
        private void RWC_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;

            Visible = false;

            if (!_showedTray)
            {
                _showedTray = true;
                ControlHelpers.ShowTrayMsg(taskIcon);
            }
        }

        private void ShowForm()
        {
            if (!this.ShowInTaskbar) this.ShowInTaskbar = true;
            if (this.WindowState == FormWindowState.Minimized) this.WindowState = FormWindowState.Normal;

            this.Visible = true;
        }

        //======================================================================
        // Restore from system tray
        //======================================================================
        private void TaskIcon_MouseDoubleClick(object sender, MouseEventArgs e) { ShowForm(); }

        //======================================================================
        // Settings selected from the menu
        //======================================================================
        private void SettingsToolStripMenuItem_Click(object sender, EventArgs e) { ShowForm(); }

        //======================================================================
        // Exit selected form the menu
        //======================================================================
        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Logger.Instance.LogMessageToFile("Exiting Reddit Wallpaper Changer.", LogLevel.Information);

            wallpaperChangeTimer.Enabled = false;
            changeWallpaperTimer.Enabled = false;

            Logger.Instance.LogMessageToFile("Reddit Wallpaper Changer is shutting down.", LogLevel.Information);

            Exit();
        }

        //======================================================================
        // Running selected from the menu
        //======================================================================
        private void StatusMenuItem1_Click(object sender, EventArgs e)
        {
            statusMenuItem1.Checked = !statusMenuItem1.Checked;
            wallpaperChangeTimer.Enabled = statusMenuItem1.Checked;

            if (statusMenuItem1.Checked)
            {
                statusMenuItem1.ForeColor = Color.ForestGreen;
                statusMenuItem1.Text = "Running";
                Logger.Instance.LogMessageToFile("Running.", LogLevel.Information);
            }
            else
            {
                statusMenuItem1.ForeColor = Color.Red;
                statusMenuItem1.Text = "Not Running";
                Logger.Instance.LogMessageToFile("Not Running.", LogLevel.Information);
            }
        }

        private void CurrentThreadMenuItem1_Click(object sender, EventArgs e)
        {
            if (CurrentThread != null)
                Process.Start(CurrentThread).Dispose();
        }

        //======================================================================
        // Change wallpaper selected from the menu
        //======================================================================
        private void ChangeWallpaper_Click(object sender, EventArgs e)
        {
            wallpaperChangeTimer.Enabled = false;
            wallpaperChangeTimer.Enabled = true;
            changeWallpaperTimer.Enabled = true;
        }

        private void DisableNotificationsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.disableNotifications = !Settings.Default.disableNotifications;
            Settings.Default.Save();

            HelperMethods.LogSettings(changeTimeType.Text, Screen.AllScreens.Length);

            chkNotifications.Checked = Settings.Default.disableNotifications;
        }

        //======================================================================
        // Startup time for update check
        //======================================================================
        private async void StartupTimer_Tick(object sender, EventArgs e)
        {
            startupTimer.Enabled = false;

            using (var wc = HelperMethods.CreateWebClient())
            {
                try
                {
                    if (Settings.Default.autoUpdateCheck)
                    {
                        var latestVersion = await wc.DownloadStringTaskAsync("https://raw.githubusercontent.com/qwertydog/Reddit-Wallpaper-Changer/master/version");

                        if (!latestVersion.Contains(_currentVersion))
                            UpdateAndClose(latestVersion);
                        else
                            changeWallpaperTimer.Enabled = true;
                    }
                    else
                        changeWallpaperTimer.Enabled = true;
                }
                catch (Exception ex)
                {
                    var details = new BalloonTipDetails(ToolTipIcon.Error, "Reddit Wallpaper Changer!", "Error checking for updates.", 750);
                    ControlHelpers.ShowBalloonTip(taskIcon, details);

                    Logger.Instance.LogMessageToFile("Error checking for updates: " + ex.Message, LogLevel.Information);
                }
            }
        }

        //======================================================================
        // Trigger wallpaper change
        //======================================================================
        private async void ChangeWallpaperTimer_Tick(object sender, EventArgs e)
        {
            changeWallpaperTimer.Enabled = false;

            await Task.Run(() => _wallpaperChanger.SearchForWallpaperAsync(_closeCts.Token))
                .ConfigureAwait(false);
        }

        //======================================================================
        // Open thread from blacklist selection click
        //======================================================================
        private void BlacklistDataGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
                Process.Start("http://reddit.com/" + blacklistDataGrid.Rows[e.RowIndex].Cells[2].Value.ToString()).Dispose();
        }

        //======================================================================
        // Open thread from history selection click
        //======================================================================
        private void HistoryDataGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
                Process.Start("http://reddit.com/" + historyDataGrid.Rows[e.RowIndex].Cells[2].Value.ToString()).Dispose();
        }

        //======================================================================
        // Open thread from favourites selection click
        //======================================================================
        private void FavouritesDataGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
                Process.Start("http://reddit.com/" + favouritesDataGrid.Rows[e.RowIndex].Cells[2].Value.ToString()).Dispose();
        }

        //======================================================================
        // Save current wallpaper locally
        //======================================================================
        private void SaveCurrentWallpaper_Click(object sender, EventArgs e)
        {
            if (HelperMethods.SaveCurrentWallpaper(Settings.Default.currentWallpaperName))
            {
                ControlHelpers.ShowWallpaperSavedBalloonTip(taskIcon);
                statuslabel1.Text = "Wallpaper saved!";
            }
            else
            {
                ControlHelpers.ShowWallpaperAlreadyExistsBalloonTip(taskIcon);
                statuslabel1.Text = "Wallpaper already saved!";
            }
        }

        //======================================================================
        // Test internet connection
        //======================================================================
        private void CheckInternetTimer_Tick(object sender, EventArgs e)
        {
            noticeLabel.Text = "Checking Internet Connection...";

            if (NetworkInterface.GetIsNetworkAvailable())
            {
                checkInternetTimer.Enabled = false;
                ControlHelpers.UpdateTimer(wallpaperChangeTimer);
                startupTimer.Enabled = true;
                Logger.Instance.LogMessageToFile("Internet is working.", LogLevel.Information);
            }
            else
            {
                statuslabel1.Text = "Network Unavailable. Rechecking.";
                Logger.Instance.LogMessageToFile("Network Unavaliable. Rechecking.", LogLevel.Warning);
            }
        }

        //======================================================================
        // Change subreddit text box
        //======================================================================
        private void SubredditTextBox_TextChanged(object sender, EventArgs e) 
            => ControlHelpers.SetSubredditTypeLabel(label5, subredditTextBox.Text);

        //======================================================================
        // Show the search wizard form
        //======================================================================
        private void SearchWizardButton_Click(object sender, EventArgs e)
        {
            var searchWizard = new SearchWizard(searchQuery.Text);

            searchWizard.Closing += (s, ev) => { searchQuery.Text = searchWizard.SearchText; };
            searchWizard.Show();
        }

        //======================================================================
        // One second break after setting a wallpaper. Once passed, this method is trigered
        //======================================================================
        private void BreakBetweenChange_Tick(object sender, EventArgs e)
        {
            breakBetweenChange.Enabled = false;
            changeWallpaperTimer.Enabled = true;
        }

        //======================================================================
        // History grid mouse click
        //======================================================================
        private void HistoryDataGrid_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _currentMouseOverRow = historyDataGrid.HitTest(e.X, e.Y).RowIndex;

                if (_currentMouseOverRow >= 0)
                    historyMenuStrip.Show(historyDataGrid, new Point(e.X, e.Y));
                else
                    contextMenuStrip.Show(historyDataGrid, new Point(e.X, e.Y));
            }
        }

        //======================================================================
        // Blacklist grid mouse click
        //======================================================================
        private void BlacklistDataGrid_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _currentMouseOverRow = blacklistDataGrid.HitTest(e.X, e.Y).RowIndex;

                if (_currentMouseOverRow >= 0)
                    blacklistMenuStrip.Show(blacklistDataGrid, new Point(e.X, e.Y));
                else
                    contextMenuStrip.Show(blacklistDataGrid, new Point(e.X, e.Y));
            }
        }

        //======================================================================
        // History grid mouse click
        //======================================================================
        private void FavouritesDataGrid_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _currentMouseOverRow = favouritesDataGrid.HitTest(e.X, e.Y).RowIndex;

                if (_currentMouseOverRow >= 0)
                    favouritesMenuStrip.Show(favouritesDataGrid, new Point(e.X, e.Y));
                else
                    contextMenuStrip.Show(favouritesDataGrid, new Point(e.X, e.Y));
            }
        }

        //======================================================================
        // Truly random searching
        //======================================================================
        private void WallpaperGrabType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (wallpaperGrabType.Text.Equals("Truly Random"))
            {
                label2.Visible = false;
                searchQuery.Visible = false;
                label9.Visible = true;
            }
            else if (!label2.Visible)
            { 
                label2.Visible = true;
                searchQuery.Visible = true;
                label9.Visible = false;
            }
        }

        //======================================================================
        // Code for enabeling/disabeling proxy credentials
        //======================================================================
        private void ChkAuth_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAuth.Checked)
            {
                txtUser.Enabled = true;
                txtUser.Text = Settings.Default.proxyUser;
                txtPass.Enabled = true;
                txtPass.Text = Settings.Default.proxyPass;
            }
            else
            {
                txtUser.Enabled = false;
                txtUser.Text = "";
                txtPass.Enabled = false;
                txtPass.Text = "";
            }
        }

        //======================================================================
        // Code for enabeling/disabeling proxy
        //======================================================================
        private void ChkProxy_CheckedChanged(object sender, EventArgs e)
        {
            if (chkProxy.Checked)
            {
                txtProxyServer.Enabled = true;
                txtProxyServer.Text = Settings.Default.proxyAddress;
                chkAuth.Enabled = true;
            }
            else
            {
                txtProxyServer.Enabled = false;
                txtProxyServer.Text = "";
                chkAuth.Enabled = false;
                chkAuth.Checked = false;
                txtUser.Enabled = false;
                txtUser.Text = "";
                txtPass.Enabled = false;
                txtPass.Text = "";
            }
        }

        //======================================================================
        // Open qwertydog profile page on Reddit
        //======================================================================
        private void QwertydogLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) 
            => Process.Start("http://www.reddit.com/user/qwertydog123/").Dispose();

        //======================================================================
        // Set default location for manually saved wallpapers
        //======================================================================
        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var folderBrowser = new FolderBrowserDialog
            {
                Description = "Select a location to save wallpapers:"
            })
            {
                if (folderBrowser.ShowDialog() == DialogResult.OK)
                    txtSavePath.Text = folderBrowser.SelectedPath;
            } 
        }

        //======================================================================
        // Click on favourite menu
        //======================================================================
        private async void FavCurrentWallpaper_Click(object sender, EventArgs e)
        {
            var redditLink = new RedditLink
            (
                Settings.Default.url,
                Settings.Default.threadTitle,
                Settings.Default.threadID
            );

            await _database.FaveWallpaperAsync(redditLink);

            var details = new BalloonTipDetails(ToolTipIcon.Info, "Wallpaper Favourited!", "Wallpaper added to favourites!", 750);
            ControlHelpers.ShowBalloonTip(taskIcon, details);

            ControlHelpers.PopulateFavourites(favouritesDataGrid, _database);

            if (Settings.Default.autoSaveFaves)
                await ControlHelpers.SaveLinkAsync(statuslabel1, taskIcon, redditLink).ConfigureAwait(false);
        }

        //======================================================================
        // Blacklist the current wallpaper
        //======================================================================
        private async void BlacklistCurrentWallpaper_Click(object sender, EventArgs e)
        {
            var imageDetails = new RedditLink
            (
                Settings.Default.url,
                Settings.Default.threadTitle,
                Settings.Default.threadID
            );

            var blacklistSuccessful = await _database.BlacklistWallpaperAsync(imageDetails);

            BalloonTipDetails details;

            if (blacklistSuccessful)
                details = new BalloonTipDetails(ToolTipIcon.Info, "Wallpaper Blacklisted!", "The wallpaper has been blacklisted! Finding a new wallpaper...", 750);
            else 
                details = new BalloonTipDetails(ToolTipIcon.Info, "Error Blacklisting!", "There was an error blacklisting the wallpaper!", 750);

            ControlHelpers.ShowBalloonTip(taskIcon, details);
                
            wallpaperChangeTimer.Enabled = false;
            wallpaperChangeTimer.Enabled = true;
            changeWallpaperTimer.Enabled = true;

            ControlHelpers.PopulateBlacklist(blacklistDataGrid, _database);
        }

        //======================================================================
        // Blacklist wallpaper from History panel
        //======================================================================
        private async void BlacklistWallpapertoolStripMenuItem_Click(object sender, EventArgs e)
        {
            var redditLink = ControlHelpers.CreateRedditLinkFromGrid(historyDataGrid, _currentMouseOverRow);

            var blacklistSuccessful = await _database.BlacklistWallpaperAsync(redditLink);

            BalloonTipDetails details;
            if (blacklistSuccessful)
                details = new BalloonTipDetails(ToolTipIcon.Info, "Wallpaper Blacklisted!", "The wallpaper has been blacklisted!", 750);
            else
                details = new BalloonTipDetails(ToolTipIcon.Error, "Error!", "There was an error adding the wallpaper to your blacklist!", 750);

            ControlHelpers.ShowBalloonTip(taskIcon, details);

            if (redditLink.Url.ToString() == Settings.Default.currentWallpaperUrl)
            {
                wallpaperChangeTimer.Enabled = false;
                wallpaperChangeTimer.Enabled = true;
                changeWallpaperTimer.Enabled = true;
            }

            ControlHelpers.PopulateBlacklist(blacklistDataGrid, _database);
        }

        //======================================================================
        // Favourite wallpaper from the History Panel
        //======================================================================
        private async void FavouriteThisWallpaperToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var redditLink = ControlHelpers.CreateRedditLinkFromGrid(historyDataGrid, _currentMouseOverRow);

            var favouriteSuccessful = await _database.FaveWallpaperAsync(redditLink);

            BalloonTipDetails details;

            if (favouriteSuccessful)
                details = new BalloonTipDetails(ToolTipIcon.Info, "Wallpaper Favourited!", "Wallpaper added to favourites!", 750);
            else
                details = new BalloonTipDetails(ToolTipIcon.Error, "Error!", "There was an error adding the Wallpaper to your favourites!", 750);

            ControlHelpers.ShowBalloonTip(taskIcon, details);

            ControlHelpers.PopulateFavourites(favouritesDataGrid, _database);

            if (Settings.Default.autoSaveFaves)
                await ControlHelpers.SaveLinkAsync(statuslabel1, taskIcon, redditLink).ConfigureAwait(false);
        }

        //======================================================================
        // Set wallpaper from selected history entry
        //======================================================================
        private async void UseThisWallpapertoolStripMenuItem_Click(object sender, EventArgs e)
        {
            Logger.Instance.LogMessageToFile("Setting a historical wallpaper (bypassing 'use once' check).", LogLevel.Information);
            Settings.Default.manualOverride = true;
            Settings.Default.Save();

            var redditLink = ControlHelpers.CreateRedditLinkFromGrid(historyDataGrid, _currentMouseOverRow);

            await Task.Run(async () =>
            {
                if (!await _wallpaperChanger.SetWallpaperAsync(redditLink).ConfigureAwait(false))
                    await _wallpaperChanger.SearchForWallpaperAsync(_closeCts.Token).ConfigureAwait(false);
            })
            .ConfigureAwait(false);
        }

        //======================================================================
        // Set wallpaper from selected favourites entry
        //======================================================================
        private async void UseFaveMenu_Click(object sender, EventArgs e)
        {
            Logger.Instance.LogMessageToFile("Setting a favourite wallpaper (bypassing 'use once' check).", LogLevel.Information);
            Settings.Default.manualOverride = true;
            Settings.Default.Save();

            var redditLink = ControlHelpers.CreateRedditLinkFromGrid(favouritesDataGrid, _currentMouseOverRow);

            await Task.Run(async () =>
            {
                if (!await _wallpaperChanger.SetWallpaperAsync(redditLink).ConfigureAwait(false))
                    await _wallpaperChanger.SearchForWallpaperAsync(_closeCts.Token).ConfigureAwait(false);
            })
            .ConfigureAwait(false);
        }

        //======================================================================
        // Remove a previously blacklisted wallpaper
        //======================================================================
        private void UnblacklistWallpaper_Click(object sender, EventArgs e)
        {
            try
            {
                var url = blacklistDataGrid.Rows[_currentMouseOverRow].Cells[3].Value.ToString();

                _database.RemoveFromBlacklist(url);

                ControlHelpers.PopulateBlacklist(blacklistDataGrid, _database);
            }
            catch(Exception ex)
            {
                Logger.Instance.LogMessageToFile("Unexpected error removing wallpaper from blacklist: " + ex.Message, LogLevel.Warning);
            }
        }

        //======================================================================
        // Remove a previously favourited wallpaper
        //======================================================================
        private void RemoveFaveMenu_Click(object sender, EventArgs e)
        {
            try
            {
                var url = favouritesDataGrid.Rows[_currentMouseOverRow].Cells[3].Value.ToString();

                _database.RemoveFromFavourites(url);

                ControlHelpers.PopulateFavourites(favouritesDataGrid, _database);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Unexpected error removing wallpaper from favourites: " + ex.Message, LogLevel.Warning);
            }
        }

        //======================================================================
        // Remove wallpaper from history
        //======================================================================
        private void RemoveThisWallpaperFromHistoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                var url = historyDataGrid.Rows[_currentMouseOverRow].Cells[3].Value.ToString();

                _database.RemoveFromHistory(url);

                ControlHelpers.PopulateHistory(historyDataGrid, _database);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Unexpected error removing wallpaper from history: " + ex.Message, LogLevel.Warning);
            }
        }

        //======================================================================
        // Open the bug form on GitHub
        //======================================================================
        private void BtnBug_Click(object sender, EventArgs e) 
            => Process.Start("https://github.com/qwertydog/Reddit-Wallpaper-Changer/issues/new").Dispose();

        //======================================================================
        // Open the log form
        //======================================================================
        private void BtnLog_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(Settings.Default.AppDataPath + @"\Logs\RWC.log").Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unexpected error opening Log file: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Logger.Instance.LogMessageToFile("Unexpected error opening Log file: " + ex.Message, LogLevel.Warning);
            }
        }

        //======================================================================
        // Donation button
        //======================================================================
        private void BtnDonate_Click(object sender, EventArgs e) 
            => Process.Start("https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=9CCQRAXVRSTZ4").Dispose();

        //======================================================================
        // Import settings
        //======================================================================
        private void BtnImport_Click(object sender, EventArgs e)
        {
            try
            {
                using (var openFile = new OpenFileDialog { Filter = "XML File (*.xml)|*.xml", FileName = "RWC Settings.xml" })
                {
                    var result = openFile.ShowDialog();
                    if (result != DialogResult.OK) return;

                    HelperMethods.ImportSettings(openFile.FileName);
                }

                MessageBox.Show("Settings file imported successfully.\r\n\r\n" +
                                "Note: If a proxy is specified that requires authentication, you must manually enter the credentials.\r\n\r\n" +
                                "RWC will now restart so the new settings take effect.", "Settings Imported", MessageBoxButtons.OK, MessageBoxIcon.Information);

                Process.Start(Assembly.GetEntryAssembly().Location).Dispose();

                Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unexpected error importing settings from XML: " + ex.Message, "Error Importing!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        //======================================================================
        // Export settings
        //======================================================================
        private void BtnExport_Click(object sender, EventArgs e)
        {
            using (var saveFile = new SaveFileDialog { Filter = "XML File (*.xml)|*.xml", FileName = "RWC Settings.xml" })
            {
                var result = saveFile.ShowDialog();
                if (result != DialogResult.OK) return;

                try
                {
                    HelperMethods.ExportSettings(saveFile.FileName);
                    MessageBox.Show("Your settings have been exported successfully!", "Settings Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unexpected error exporting settings to XML: " + ex.Message, "Error Exporting!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        //======================================================================
        // Save Wallpaper Layout Type
        //======================================================================
        private void BtnMonitorSave_Click(object sender, EventArgs e)
        {
            var wallpaperStyle = WallpaperStyle.Default;
            var tileWallpaper = TileWallpaper.None;

            switch (comboType.Text)
            {
                case "Fill":
                    wallpaperStyle = WallpaperStyle.Fill;
                    break;
                case "Fit":
                    wallpaperStyle = WallpaperStyle.Fit;
                    break;
                case "Span":
                    wallpaperStyle = WallpaperStyle.Span;
                    break;
                case "Stretch":
                    wallpaperStyle = WallpaperStyle.Stretch;
                    break;
                case "Tile":
                    tileWallpaper = TileWallpaper.Tile;
                    break;
                default: // Center
                    break;
            }

            Settings.Default.wallpaperStyle = comboType.Text;
            Settings.Default.Save();

            RegistryAdapter.SetWallpaperStyle(wallpaperStyle, tileWallpaper);

            ControlHelpers.UpdateMonitorPanelScreens(monitorLayoutPanel);

            comboType.Text = Settings.Default.wallpaperStyle;
            ControlHelpers.SetExample(picStyles, comboType.Text);

            MessageBox.Show($"Wallpaper style successfully changed to: {comboType.Text}", "Saved!",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        //======================================================================
        // Populate info box on chosen style
        //======================================================================
        private void ComboType_SelectedIndexChanged(object sender, EventArgs e) 
            => ControlHelpers.SetExample(picStyles, comboType.Text);

        //======================================================================
        // Open the wallpaper style info window
        //======================================================================
        private void BtnWallpaperHelp_Click(object sender, EventArgs e) 
            => new WallpaperTypes().Show();

        //======================================================================
        // Upload log file to Pastebin
        //======================================================================
        private async void BtnUpload_Click(object sender, EventArgs e)
        {
            btnUpload.Enabled = false;
            btnUpload.Text = "Uploading...";
            
            var logUrl = await HelperMethods.UploadLogToPastebinAsync();

            btnUpload.Text = "Upload Log";
            btnUpload.Enabled = true;

            if (!string.IsNullOrWhiteSpace(logUrl))
            {
                Clipboard.SetText(logUrl);

                MessageBox.Show("Your logfile has been uploaded to Pastebin successfully.\r\n" +
                    "The URL to the Paste has been copied to your clipboard.", "Upload successful!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
                MessageBox.Show("The upload of your logfile to Pastebin failed. Check the log for details, or upload your log manually.", 
                    "Upload failed!", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        //======================================================================
        // Check for more than 7 days
        //======================================================================
        private void ChangeTimeType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (changeTimeType.Text == "Days" && changeTimeValue.Value >= 8)
            {
                MessageBox.Show("Sorry, but upper limit for wallpaper changes is 7 Days!", "Too many days!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                changeTimeValue.Value = 7;
            }
        }

        //======================================================================
        // Override auto save faves if auto save all is enabled
        //======================================================================
        private void ChkAutoSave_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoSave.Checked)
            {
                chkAutoSaveFaves.Enabled = chkAutoSaveFaves.Checked = false;

                Settings.Default.autoSaveFaves = false;
                Settings.Default.Save();
            }
            else
                chkAutoSaveFaves.Enabled = true;
        }

        //======================================================================
        // Manually save selected favourite wallpaper
        //======================================================================
        private async void SaveThisWallpaperToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var redditLink = ControlHelpers.CreateRedditLinkFromGrid(favouritesDataGrid, _currentMouseOverRow);

            await ControlHelpers.SaveLinkAsync(statuslabel1, taskIcon, redditLink).ConfigureAwait(false);
        }

        //======================================================================
        // Manually save selected historical wallpaper
        //======================================================================
        private async void SaveThisWallpaperToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            var redditLink = ControlHelpers.CreateRedditLinkFromGrid(historyDataGrid, _currentMouseOverRow);

            await ControlHelpers.SaveLinkAsync(statuslabel1, taskIcon, redditLink).ConfigureAwait(false);
        }

        //======================================================================
        // Delete all wallpaper history from the database
        //======================================================================
        private void BtnClearHistory_Click(object sender, EventArgs e)
        {
            var choice = MessageBox.Show("Are you sure you want to delete ALL wallpaper history?\r\n" + 
                "It's recommended that you take a backup first!\r\n\r\nTHIS ACTION CANNOT BE UNDONE!", "Clear History?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (choice == DialogResult.Yes && _database.WipeTable("history"))
            {
                ControlHelpers.PopulateHistory(historyDataGrid, _database);
                MessageBox.Show("All historical wallpaper data has been deleted!", 
                    "History Deleted!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        //======================================================================
        // Delete all favourite wallpapers from the database 
        //======================================================================
        private void BtnClearFavourites_Click(object sender, EventArgs e)
        {
            var choice = MessageBox.Show("Are you sure you want to remove all favourite wallpapers?\r\n" +
                "It's recommended that you take a backup first!\r\n\r\nTHIS ACTION CANNOT BE UNDONE!", "Clear Favourites?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (choice == DialogResult.Yes && _database.WipeTable("favourites"))
            {
                ControlHelpers.PopulateFavourites(favouritesDataGrid, _database);
                MessageBox.Show("All wallpapers have been deleted from your favourites!", "Favourites Deleted!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        //======================================================================
        // Delete all blacklisted wallpapers from the database 
        //======================================================================
        private void BtnClearBlacklisted_Click(object sender, EventArgs e)
        {
            DialogResult choice = MessageBox.Show("Are you sure you want to remove all blacklisted wallpapers?\r\n" +
                "It's recommended that you take a backup first!\r\n\r\nTHIS ACTION CANNOT BE UNDONE!", "Clear Blacklist?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (choice == DialogResult.Yes && _database.WipeTable("blacklist"))
            {
                ControlHelpers.PopulateBlacklist(blacklistDataGrid, _database);
                MessageBox.Show("All wallpaper have been deleted from your blacklist!", "Blacklist Deleted!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        //======================================================================
        // Backup SQLite database
        //======================================================================
        private void BtnBackup_Click(object sender, EventArgs e)
        {
            using (var folderBrowser = new FolderBrowserDialog
            {
                Description = "Select a location to backup the database:"
            })
            {
                if (folderBrowser.ShowDialog() == DialogResult.OK)
                {
                    Logger.Instance.LogMessageToFile("Database backup process started.", LogLevel.Information);

                    if (_database.BackupDatabase(folderBrowser.SelectedPath))
                    {
                        MessageBox.Show("Your database has been successfully backed up.", "Backup Successful!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Logger.Instance.LogMessageToFile("The backup process has completed successfully.", LogLevel.Information);
                    }
                    else
                    {
                        MessageBox.Show("There was an error backing up your database.", "Backup Failed!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Logger.Instance.LogMessageToFile("The backup process has failed.", LogLevel.Error);
                    }
                }
            }
        }

        //======================================================================
        // Restore SQLite backup
        //======================================================================
        private void BtnRestore_Click(object sender, EventArgs e)
        {
            using (var fileBrowser = new OpenFileDialog
            {
                Filter = "SQLite Database (*.sqlite)|*.sqlite",
                Multiselect = false
            })
            {
                if (fileBrowser.ShowDialog() == DialogResult.OK)
                {
                    Logger.Instance.LogMessageToFile("Database restore process has been started.", LogLevel.Information);

                    if (_database.RestoreDatabase(fileBrowser.FileName))
                    {
                        ControlHelpers.PopulateHistory(historyDataGrid, _database);
                        ControlHelpers.PopulateFavourites(favouritesDataGrid, _database);
                        ControlHelpers.PopulateBlacklist(blacklistDataGrid, _database);

                        MessageBox.Show("Your database has been successfully restored.", "Restore Successful!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Logger.Instance.LogMessageToFile("The restore process has completed successfully.", LogLevel.Information);
                    }
                    else
                    {
                        MessageBox.Show("There was an error restoring your database.", "Restore Failed!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Logger.Instance.LogMessageToFile("The restore process has failed.", LogLevel.Error);
                    }
                }
            }
        }

        //======================================================================
        // Rebuild cache
        //======================================================================
        private void BtnRebuildThumbnails_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("This will remove all wallpaper thumbnails and recreate them.\r\n\r\r" +
                "Reddit Wallpaper Will be restarted to complete this process. Continue?", 
                "Rebuild Thumbnails?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No) return;

            try
            {
                Logger.Instance.LogMessageToFile("Restarting Reddit Wallpaper Changer to clear thumbnail cache.", LogLevel.Information);
                Settings.Default.rebuildThumbCache = true;
                Settings.Default.Save();

                Process.Start(Assembly.GetEntryAssembly().Location).Dispose();

                Exit();
            }
            catch(Exception ex)
            {
                Logger.Instance.LogMessageToFile("Error restarting RWC: " + ex.Message, LogLevel.Warning);
            }
        }

        private void ContextMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            disableNotificationsToolStripMenuItem.Text = Settings.Default.disableNotifications ?
                "Enable Notifications" :
                "Disable Notifications";
        }

        #endregion

        #region Helper methods

        private void Exit()
        {
            FormClosing -= RWC_FormClosing;

            if (taskIcon != null)
            {
                taskIcon.Visible = false;
                taskIcon.Icon = null;
                taskIcon.Dispose();
                taskIcon = null;
            }

            Close();
        }

        // TODO refactor
        //======================================================================
        // Save button code
        //======================================================================
        private void SaveData()
        {
            if (changeTimeType.Text == "Days" && changeTimeValue.Value >= 8)
            {
                MessageBox.Show("Sorry, but upper limit for wallpaper changes is 7 Days!", "Too many days!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool updateTimerBool = false;

            if (Settings.Default.autoStart != chkAutoStart.Checked)
                RegistryAdapter.SetAutoStart(chkAutoStart.Checked);

            Settings.Default.startInTray = chkStartInTray.Checked;
            Settings.Default.includeNsfw = chkIncludeNsfw.Checked;
            Settings.Default.autoStart = chkAutoStart.Checked;
            Settings.Default.wallpaperGrabType = wallpaperGrabType.SelectedIndex;
            Settings.Default.subredditsUsed = subredditTextBox.Text;
            Settings.Default.searchQuery = searchQuery.Text;

            if ((Settings.Default.changeTimeValue != (int)changeTimeValue.Value) || (Settings.Default.changeTimeType != changeTimeType.SelectedIndex))
                updateTimerBool = true;

            Settings.Default.changeTimeValue = (int)changeTimeValue.Value;
            Settings.Default.changeTimeType = changeTimeType.SelectedIndex;
            Settings.Default.useProxy = chkProxy.Checked;
            Settings.Default.proxyAddress = txtProxyServer.Text;
            Settings.Default.proxyAuth = chkAuth.Checked;
            Settings.Default.proxyUser = txtUser.Text;
            Settings.Default.proxyPass = txtPass.Text;
            Settings.Default.defaultSaveLocation = txtSavePath.Text;
            Settings.Default.autoSave = chkAutoSave.Checked;
            Settings.Default.disableNotifications = chkNotifications.Checked;
            Settings.Default.fitWallpaper = chkFitWallpaper.Checked;
            Settings.Default.suppressDuplicates = chkSuppressDuplicates.Checked;
            Settings.Default.wallpaperInfoPopup = chkWallpaperInfoPopup.Checked;
            Settings.Default.wallpaperFade = chkFade.Checked;
            Settings.Default.autoUpdateCheck = chkUpdates.Checked;
            Settings.Default.autoSaveFaves = chkAutoSaveFaves.Checked;
            Settings.Default.useHotkeys = chkUseHotkeys.Checked;
            Settings.Default.changeWallpaperHotkey = txtChangeHotkey.Text;
            Settings.Default.saveWallpaperHotkey = txtSaveHotkey.Text;
            Settings.Default.favouriteWallpaperHotkey = txtFavouriteHotkey.Text;
            Settings.Default.blacklistWallpaperHotkey = txtBlacklistHotkey.Text;

            Settings.Default.Save();

            HelperMethods.LogSettings(changeTimeType.Text, Screen.AllScreens.Length);

            if (updateTimerBool)
                ControlHelpers.UpdateTimer(wallpaperChangeTimer);

            SetupProxySettings();
        }

        private void SetToolTips()
        {
            _toolTip = new ToolTip
            {
                AutoPopDelay = 7500,
                InitialDelay = 1000,
                ReshowDelay = 500,
                ShowAlways = true,
                ToolTipTitle = "RWC",
                ToolTipIcon = ToolTipIcon.Info
            };

            _toolTip.SetToolTip(chkAutoStart, "Run Reddit Wallpaper Changer when your computer starts.");
            _toolTip.SetToolTip(chkStartInTray, "Start Reddit Wallpaper Changer minimised.");
            _toolTip.SetToolTip(chkProxy, "Configure a proxy server for Reddit Wallpaper Changer to use.");
            _toolTip.SetToolTip(chkAuth, "Enable if your proxy server requires authentication.");
            _toolTip.SetToolTip(btnBrowse, "Sellect the downlaod destination for saved wallpapers.");
            _toolTip.SetToolTip(btnSave, "Saves your settings.");
            _toolTip.SetToolTip(btnWizard, "Open the Search wizard.");
            _toolTip.SetToolTip(wallpaperGrabType, "Choose how you want to find a wallpaper.");
            _toolTip.SetToolTip(changeTimeValue, "Choose how oftern to change your wallpaper.");
            _toolTip.SetToolTip(subredditTextBox, "Enter the subs to scrape for wallpaper (eg, wallpaper, earthporn etc).\r\nMultiple subs can be provided and separated with a +.");
            _toolTip.SetToolTip(chkAutoSave, "Enable this to automatically save all wallpapers to the below directory.");
            _toolTip.SetToolTip(chkFade, "Enable this for a faded wallpaper transition using Active Desktop.\r\nDisable this option if you experience any issues when the wallpaper changes.");
            _toolTip.SetToolTip(chkNotifications, "Disables all RWC System Tray/Notification Centre notifications.");
            _toolTip.SetToolTip(chkFitWallpaper, "Enable this option to ensure that wallpapers matching your resolution are applied.\r\n\r\n" +
                "NOTE: If you have multiple screens, it will validate wallpaper sizes against the ENTIRE desktop area and not just your primary display (eg, 3840x1080 for two 1980x1080 displays).\r\n" +
                "Best suited to single monitors, or dual monitors with matching resolutions. If you experience a lack of wallpapers, try disabling this option.");
            _toolTip.SetToolTip(chkSuppressDuplicates, "Disable this option if you don't mind the occasional repeating wallpaper in the same session.");
            _toolTip.SetToolTip(chkWallpaperInfoPopup, "Displays a mini wallpaper info popup at the bottom right of your primary display for 5 seconds.\r\n" +
                "Note: The 'Disable Notifications' option suppresses this popup.");
            _toolTip.SetToolTip(chkAutoSaveFaves, "Enable this option to automatically save Favourite wallpapers to the below directory.");
            _toolTip.SetToolTip(btnClearHistory, "This will erase ALL historical information from the History panel.");
            _toolTip.SetToolTip(btnClearFavourites, "This will erase ALL wallpaper information from your Favourites.");
            _toolTip.SetToolTip(btnClearBlacklisted, "This will erase ALL wallpaper information from your Blacklist.");
            _toolTip.SetToolTip(btnBackup, "Backup Reddit Wallpaper Changer's database.");
            _toolTip.SetToolTip(btnRestore, "Restore a previous backup.");
            _toolTip.SetToolTip(btnRebuildThumbnails, "This will wipe the current thumbnail cache and recreate it.");
            _toolTip.SetToolTip(chkUpdates, "Enable or disable automatic update checks.\r\nA manual check for updates can be done in the 'About' panel.");

            _toolTip.SetToolTip(chkUseHotkeys, "NOTE: These hotkeys will override other system-wide hotkeys");
            _toolTip.SetToolTip(txtChangeHotkey, "Hotkey used to change the current wallpaper.");
            _toolTip.SetToolTip(txtFavouriteHotkey, "Hotkey used to favourite the current wallpaper.");
            _toolTip.SetToolTip(txtBlacklistHotkey, "Hotkey used to blacklist the current wallpaper.");
            _toolTip.SetToolTip(txtSaveHotkey, "Hotkey used to save the current wallpaper.");

            // Monitors
            _toolTip.SetToolTip(btnWallpaperHelp, "Show info on the different wallpaper styles.");

            // About
            _toolTip.SetToolTip(btnSubreddit, "Having issues? You can get support by posting on the Reddit Wallpaper Changer Subreddit.");
            _toolTip.SetToolTip(btnBug, "Spotted a bug? Open a ticket on GitHub by clicking here!");
            _toolTip.SetToolTip(btnDonate, "Reddit Wallpaper Changer is maintained by one guy in his own time!\r\nIf you'd like to say 'thanks' by getting him a beer, click here! :)");
            _toolTip.SetToolTip(btnUpdate, "Click here to manually check for updates.");
            _toolTip.SetToolTip(btnLog, "Click here to open the RWC log file in your default text editor.");
            _toolTip.SetToolTip(btnImport, "Import custom settings from an XML file.");
            _toolTip.SetToolTip(btnExport, "Export your current settings into an XML file.");
            _toolTip.SetToolTip(btnUpload, "Having issues? Click here to automatically upload your log file to Pastebin!");
        }

        //======================================================================
        // Set proxy settings if configured
        //======================================================================
        private void SetupProxySettings()
        {
            if (Settings.Default.useProxy)
            {
                chkProxy.Checked = true;
                txtProxyServer.Enabled = true;
                txtProxyServer.Text = Settings.Default.proxyAddress;

                if (Settings.Default.proxyAuth)
                {
                    chkAuth.Enabled = true;
                    chkAuth.Checked = true;
                    txtUser.Enabled = true;
                    txtUser.Text = Settings.Default.proxyUser;
                    txtPass.Enabled = true;
                    txtPass.Text = Settings.Default.proxyPass;
                }
            }
        }

        //======================================================================
        // Set up other aspects of the application
        //======================================================================
        private void SetupOthers()
        {
            if (Settings.Default.UpgradeRequired)
            {
                Settings.Default.Upgrade();
                Settings.Default.UpgradeRequired = false;
                Settings.Default.Save();
            }

            wallpaperGrabType.SelectedIndex = Settings.Default.wallpaperGrabType;
            subredditTextBox.Text = Settings.Default.subredditsUsed;
            searchQuery.Text = Settings.Default.searchQuery;
            changeTimeValue.Value = Settings.Default.changeTimeValue;
            changeTimeType.SelectedIndex = Settings.Default.changeTimeType;
            chkStartInTray.Checked = Settings.Default.startInTray;
            chkIncludeNsfw.Checked = Settings.Default.includeNsfw;
            chkAutoStart.Checked = Settings.Default.autoStart;
            chkFade.Checked = Settings.Default.wallpaperFade;
            chkNotifications.Checked = Settings.Default.disableNotifications;
            chkFitWallpaper.Checked = Settings.Default.fitWallpaper;
            chkSuppressDuplicates.Checked = Settings.Default.suppressDuplicates;
            chkWallpaperInfoPopup.Checked = Settings.Default.wallpaperInfoPopup;
            chkUpdates.Checked = Settings.Default.autoUpdateCheck;
            chkAutoSaveFaves.Checked = Settings.Default.autoSaveFaves;

            lblVersion.Text = "Current Version: " + _currentVersion;
        }

        private void SetupHotkeys()
        {
            chkUseHotkeys.Checked = Settings.Default.useHotkeys;
            txtChangeHotkey.Text = Settings.Default.changeWallpaperHotkey;
            txtSaveHotkey.Text = Settings.Default.saveWallpaperHotkey;
            txtFavouriteHotkey.Text = Settings.Default.favouriteWallpaperHotkey;
            txtBlacklistHotkey.Text = Settings.Default.blacklistWallpaperHotkey;

            if (chkUseHotkeys.Checked)
                RegisterHotkeys();
        }

        private void RegisterHotkeys()
        {
            RegisterHotkey(txtChangeHotkey.Text, ChangeWallpaper_Click);
            RegisterHotkey(txtSaveHotkey.Text, SaveCurrentWallpaper_Click);
            RegisterHotkey(txtFavouriteHotkey.Text, FavCurrentWallpaper_Click);
            RegisterHotkey(txtBlacklistHotkey.Text, BlacklistCurrentWallpaper_Click);
        }

        private void RegisterHotkey(string text, EventHandler eventHandler)
        {
            if (_hotkeys.TryGetValue(eventHandler, out HotKey value))
                value?.Dispose();

            if (!string.IsNullOrWhiteSpace(text)
                && HotKey.TryParse(text, out HotKey hotKey))
            {
                hotKey.HotKeyPressed += eventHandler;

                _hotkeys[eventHandler] = hotKey;
            }
            else
            {
                _hotkeys.Remove(eventHandler);
            }
        }

        //======================================================================
        // Setup the five panels
        //======================================================================
        private void SetupPanels()
        {
            var w = 450;
            var h = 405;
            aboutPanel.Size = new Size(w, h);
            configurePanel.Size = new Size(w, h);
            monitorPanel.Size = new Size(w, h);
            historyPanel.Size = new Size(w, h);
            blacklistPanel.Size = new Size(w, h);
            favouritesPanel.Size = new Size(w, h);

            var x = 0;
            var y = 65;
            aboutPanel.Location = new Point(x, y);
            configurePanel.Location = new Point(x, y);
            monitorPanel.Location = new Point(x, y);
            historyPanel.Location = new Point(x, y);
            blacklistPanel.Location = new Point(x, y);
            favouritesPanel.Location = new Point(x, y);
        }

        //======================================================================
        // Setup the main buttons
        //======================================================================
        private void SetupButtons()
        {
            aboutButton.BackColor = Color.White;
            aboutButton.FlatAppearance.BorderColor = Color.White;
            aboutButton.FlatAppearance.MouseDownBackColor = Color.White;
            aboutButton.FlatAppearance.MouseOverBackColor = Color.White;

            historyButton.BackColor = Color.White;
            historyButton.FlatAppearance.BorderColor = Color.White;
            historyButton.FlatAppearance.MouseDownBackColor = Color.White;
            historyButton.FlatAppearance.MouseOverBackColor = Color.White;

            monitorButton.BackColor = Color.White;
            monitorButton.FlatAppearance.BorderColor = Color.White;
            monitorButton.FlatAppearance.MouseDownBackColor = Color.White;
            monitorButton.FlatAppearance.MouseOverBackColor = Color.White;

            blacklistButton.BackColor = Color.White;
            blacklistButton.FlatAppearance.BorderColor = Color.White;
            blacklistButton.FlatAppearance.MouseDownBackColor = Color.White;
            blacklistButton.FlatAppearance.MouseOverBackColor = Color.White;

            favouritesButton.BackColor = Color.White;
            favouritesButton.FlatAppearance.BorderColor = Color.White;
            favouritesButton.FlatAppearance.MouseDownBackColor = Color.White;
            favouritesButton.FlatAppearance.MouseOverBackColor = Color.White;
        }

        #endregion

        private void txtHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            var textBox = (TextBox)sender;

            var clearKeys = new[] { Keys.Delete, Keys.Back };

            if (!clearKeys.Contains(e.KeyData))
            {
                var rawKey = e.KeyData ^ e.Modifiers;
                if (rawKey != Keys.ControlKey && rawKey != Keys.ShiftKey && rawKey != Keys.Menu)
                {
                    var text = "";

                    if (e.Modifiers.HasFlag(Keys.Control))
                        text += "Ctrl+";

                    if (e.Modifiers.HasFlag(Keys.Shift))
                        text += "Shift+";

                    if (e.Modifiers.HasFlag(Keys.Alt))
                        text += "Alt+";

                    text += rawKey.ToString();

                    textBox.Text = text;

                    if (textBox == txtChangeHotkey)
                        RegisterHotkey(txtChangeHotkey.Text, ChangeWallpaper_Click);
                    else if (textBox == txtSaveHotkey)
                        RegisterHotkey(txtSaveHotkey.Text, SaveCurrentWallpaper_Click);
                    else if (textBox == txtFavouriteHotkey)
                        RegisterHotkey(txtFavouriteHotkey.Text, FavCurrentWallpaper_Click);
                    else if (textBox == txtBlacklistHotkey)
                        RegisterHotkey(txtBlacklistHotkey.Text, BlacklistCurrentWallpaper_Click);
                }
            }
            else
            {
                textBox.Text = "";

                if (textBox == txtChangeHotkey)
                    UnregisterHotkey(ChangeWallpaper_Click);
                else if (textBox == txtSaveHotkey)
                    UnregisterHotkey(SaveCurrentWallpaper_Click);
                else if (textBox == txtFavouriteHotkey)
                    UnregisterHotkey(FavCurrentWallpaper_Click);
                else if (textBox == txtBlacklistHotkey)
                    UnregisterHotkey(BlacklistCurrentWallpaper_Click);
            }

            e.SuppressKeyPress = true;
        }

        private void chkUseHotkeys_CheckedChanged(object sender, EventArgs e)
        {
            if (chkUseHotkeys.Checked)
                RegisterHotkeys();
            else
                UnregisterHotkeys();

            grpHotkeys.Enabled = chkUseHotkeys.Checked;
        }

        private void UnregisterHotkeys()
        {
            UnregisterHotkey(ChangeWallpaper_Click);
            UnregisterHotkey(SaveCurrentWallpaper_Click);
            UnregisterHotkey(FavCurrentWallpaper_Click);
            UnregisterHotkey(BlacklistCurrentWallpaper_Click);
        }

        private void UnregisterHotkey(EventHandler eventHandler)
        {
            if (_hotkeys.TryGetValue(eventHandler, out HotKey value))
            {
                value?.Dispose();
                _hotkeys.Remove(eventHandler);
            }
        }
    }
}