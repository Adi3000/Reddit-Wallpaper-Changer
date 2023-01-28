using Reddit_Wallpaper_Changer.Forms;
using Reddit_Wallpaper_Changer.Properties;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Reddit_Wallpaper_Changer
{
    static class Program
    {
        private readonly static Mutex mutex = new Mutex(false, "RedditWallpaperChanger_byUgleh");

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(2), false))
            {
                if (MessageBox.Show("Run another instance of RWC?", "Reddit Wallpaper Changer", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    RunApplication();

                return;
            }

            try
            {
                RunApplication();
            }
            finally
            {
                // I find this more explicit
                mutex.ReleaseMutex(); 
            } 
        }

        private static void RunApplication()
        {
            try
            {
                var appDataFolderPath = SetAppDataPath();

                var database = new Database(appDataFolderPath);

                var wallpaperChanger = new WallpaperChanger(database);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var mainForm = new RWC(database, wallpaperChanger);

                wallpaperChanger.UiMarshaller = new MainThreadMarshaller(mainForm, SynchronizationContext.Current);

                Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Unhandled Exception: " + ex.Message, LogLevel.Error);
            }
            finally
            {
                Logger.Instance.Close();
            }
        }

        private static string SetAppDataPath()
        {
            string appDataFolderPath;
            if (Settings.Default.AppDataPath.Any())
                appDataFolderPath = Settings.Default.AppDataPath;
            else
                appDataFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Reddit Wallpaper Changer";

            Directory.CreateDirectory(appDataFolderPath);
            Settings.Default.AppDataPath = appDataFolderPath;
            Settings.Default.Save();

            return appDataFolderPath;
        }
    }
}
