using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer.Forms
{
    public partial class PopupInfo : Form
    {
        private const double FadeAmount = 0.01;
        private const int FadeInterval = 5;

        private Timer fade = new Timer();
        private Timer timer = new Timer();

        public string Title { get; set; }
        public string Threadid { get; set; }

        protected override bool ShowWithoutActivation => true;

        public PopupInfo(string threadid, string title)
        {
            InitializeComponent();

            Threadid = threadid;
            Title = title;
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

                fade?.Dispose();
                timer?.Dispose();
            }

            base.Dispose(disposing);
        }

        //======================================================================
        // Load form by triggering a Fade In
        //======================================================================
        private void PopupInfo_Load(object sender, EventArgs e)
        {
            txtWallpaperTitle.Text = Title;
            lnkWallpaper.Text = "http://www.reddit.com/" + Threadid;

            if (Settings.Default.currentWallpaperFile.Any())
            {
                try
                {
                    imgWallpaper.BackgroundImage = new Bitmap(Settings.Default.currentWallpaperFile);
                    imgWallpaper.BackgroundImageLayout = ImageLayout.Stretch;
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessageToFile("Error creating wallpaper bitmap for popup window: " + ex.Message, LogLevel.Error);
                }
            }
            else
                Logger.Instance.LogMessageToFile("No current wallpaper file found", LogLevel.Warning);
            
            fade.Interval = FadeInterval;  
            fade.Tick += FadeIn;
            fade.Start();
            
            timer.Interval = 3000;
            timer.Tick += Timer_Tick;
        }

        //======================================================================
        // Fade In
        //======================================================================
        void FadeIn(object sender, EventArgs e)
        {
            if (Opacity >= 1)
            {
                fade.Stop();
                timer.Start();
            }
            else
                Opacity += FadeAmount;
        }

        //======================================================================
        // Close once tick has run
        //======================================================================
        void Timer_Tick(object sender, EventArgs e)
        {
            timer.Stop();

            fade.Tick -= FadeIn;
            fade.Tick += FadeOut;
            fade.Start();
        }

        //======================================================================
        // Fade Out
        //======================================================================
        void FadeOut(object sender, EventArgs e)
        {
            if (Opacity <= 0) 
            {
                fade.Stop();
                Close();   
            }
            else
                Opacity -= FadeAmount;
        }

        private void PopupInfo_MouseEnter(object sender, EventArgs e)
        {
            fade.Stop();
            timer.Stop();

            Opacity = 1;
        }

        private void PopupInfo_MouseLeave(object sender, EventArgs e)
        {
            fade.Tick -= FadeIn;
            fade.Tick -= FadeOut;

            timer.Start();
        }
    }
}
