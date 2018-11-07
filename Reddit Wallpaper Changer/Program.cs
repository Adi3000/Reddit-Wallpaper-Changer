using Reddit_Wallpaper_Changer.Forms;
using System;
using System.Threading;
using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer
{
    static class Program
    {
        private static Mutex mutex = new Mutex(false, "RedditWallpaperChanger_byUgleh");

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(2), false))
            {
                if (MessageBox.Show("Run another instance of RWC?", "Reddit Wallpaper Changer", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    RunMessageLoop();

                return;
            }

            try
            {
                RunMessageLoop();
            }
            finally
            {
                // I find this more explicit
                mutex.ReleaseMutex(); 
            } 
        }

        private static void RunMessageLoop()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new RWC());
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Unhandled Exception: " + ex.Message, LogLevel.Error);
            }
            finally
            {
                Logger.Instance.CloseAsync().GetAwaiter().GetResult();
            }
        }
    }
}
