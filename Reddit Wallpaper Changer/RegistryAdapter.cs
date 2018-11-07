using Microsoft.Win32;
using System;
using System.Reflection;

namespace Reddit_Wallpaper_Changer
{
    public static class RegistryAdapter
    {
        //======================================================================
        // Configure run on startup 
        //======================================================================
        public static void SetAutoStart(bool shouldAutoStart)
        {
            try
            {
                var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);

                if (shouldAutoStart)
                {
                    //Surround path with " " to make sure that there are no problems
                    //if path contains spaces.
                    key.SetValue("RWC", "\"" + Assembly.GetEntryAssembly().Location + "\"");
                }
                else
                    key.DeleteValue("RWC");

                key.Close();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile("Error setting RWC to load on startup: " + ex.Message, LogLevel.Error);
            }
        }

        public static void SetWallpaperStyle(WallpaperStyle wallpaperStyle, TileWallpaper tileWallpaper)
        {
            var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);

            key.SetValue(@"WallpaperStyle", ((int)wallpaperStyle).ToString());
            key.SetValue(@"TileWallpaper", ((int)tileWallpaper).ToString());
        }
    }
}
