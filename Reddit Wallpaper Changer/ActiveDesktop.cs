using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Reddit_Wallpaper_Changer
{
    public class ActiveDesktop : IDisposable
    {
        private const uint SPIF_UPDATEINIFILE = 0x1;
        private const int SPIF_SENDWININICHANGE = 0x02;
        private const uint SPI_SETDESKWALLPAPER = 20;

        [Flags]
        private enum AD_Apply : int
        {
            SAVE = 0x00000001,
            HTMLGEN = 0x00000002,
            REFRESH = 0x00000004,
            ALL = SAVE | HTMLGEN | REFRESH,
            FORCE = 0x00000008,
            BUFFERED_REFRESH = 0x00000010,
            DYNAMICREFRESH = 0x00000020
        }

        [ComImport]
        [Guid("F490EB00-1240-11D1-9888-006097DEACF9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActiveDesktop
        {
            [PreserveSig]
            int ApplyChanges(AD_Apply dwFlags);
            [PreserveSig]
            int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string pwszWallpaper, int dwReserved);
        }

        private static readonly Type 
            _activeDesktopType = Type.GetTypeFromCLSID(new Guid("{75048700-EF1F-11D0-9888-006097DEACF9}"));

        private readonly IActiveDesktop _activeDesktop;

        public ActiveDesktop()
        {
            _activeDesktop = (IActiveDesktop)Activator.CreateInstance(_activeDesktopType);
        }

        private void SetWallpaperWithFade(string wallpaperFile)
        {
            NativeMethods.SendMessageTimeout(NativeMethods.FindWindow("Progman", IntPtr.Zero), 
                0x52c, IntPtr.Zero, IntPtr.Zero, 0, 500, out IntPtr _);

            _activeDesktop.SetWallpaper(wallpaperFile, 0);
            _activeDesktop.ApplyChanges(AD_Apply.ALL | AD_Apply.FORCE | AD_Apply.BUFFERED_REFRESH);
        }

        private static void SetWallpaper(string wallpaperFile) 
            => NativeMethods.SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperFile, 
                SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);

        public static async Task SetWallpaperAsync(string wallpaperFile)
        {
            if (Settings.Default.wallpaperFade)
            {
                Logger.Instance.LogMessageToFile("Applying wallpaper using Active Desktop.", LogLevel.Information);

                await HelperMethods.StartSTATask(() =>
                {
                    using (var activeDesktop = new ActiveDesktop())
                    {
                        activeDesktop.SetWallpaperWithFade(wallpaperFile);
                    }
                })
                .ConfigureAwait(false);
            }
            else
            {
                Logger.Instance.LogMessageToFile("Applying wallpaper using standard process.", LogLevel.Information);

                SetWallpaper(wallpaperFile);
            }
        }

        #region IDisposable Support

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // dispose managed state (managed objects).

                Marshal.ReleaseComObject(_activeDesktop);
            }

            // free unmanaged resources (unmanaged objects) and override a finalizer below.
            // set large fields to null.

            _disposed = true;
        }

        public void Dispose() => Dispose(true);

        #endregion
    }
}
