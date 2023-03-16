using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Reddit_Wallpaper_Changer
{
    public class ActiveDesktop : IDisposable
    {
        private const uint SPIF_UPDATEINIFILE = 0x1;
        private const int SPIF_SENDWININICHANGE = 0x02;
        private const uint SPI_SETDESKWALLPAPER = 20;

        [StructLayout(LayoutKind.Sequential)]
        private struct WALLPAPEROPT
        {
            public static readonly int SizeOf = Marshal.SizeOf(typeof(WALLPAPEROPT));
            public WallPaperStyle dwStyle;
        }

        private enum WallPaperStyle : int
        {
            WPSTYLE_CENTER = 0,
            WPSTYLE_TILE = 1,
            WPSTYLE_STRETCH = 2,
            WPSTYLE_KEEPASPECT = 3,
            WPSTYLE_CROPTOFIT = 4,
            WPSTYLE_SPAN = 5,
            WPSTYLE_MAX = 5
        }

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

        [StructLayout(LayoutKind.Sequential)]
        private struct COMPONENTSOPT
        {
            public static readonly int SizeOf = Marshal.SizeOf(typeof(COMPONENTSOPT));
            public int dwSize;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fEnableComponents;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fActiveDesktop;
        }

        [Flags]
        private enum CompItemState : int
        {
            NORMAL = 0x00000001,
            FULLSCREEN = 00000002,
            SPLIT = 0x00000004,
            VALIDSIZESTATEBITS = NORMAL | SPLIT | FULLSCREEN,
            VALIDSTATEBITS = NORMAL | SPLIT | FULLSCREEN | unchecked((int)0x80000000) | 0x40000000
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COMPSTATEINFO
        {
            public static readonly int SizeOf = Marshal.SizeOf(typeof(COMPSTATEINFO));
            public int dwSize;
            public int iLeft;
            public int iTop;
            public int dwWidth;
            public int dwHeight;
            public CompItemState dwItemState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COMPPOS
        {
            public const int COMPONENT_TOP = 0x3FFFFFFF;
            public const int COMPONENT_DEFAULT_LEFT = 0xFFFF;
            public const int COMPONENT_DEFAULT_TOP = 0xFFFF;
            public static readonly int SizeOf = Marshal.SizeOf(typeof(COMPPOS));

            public int dwSize;
            public int iLeft;
            public int iTop;
            public int dwWidth;
            public int dwHeight;
            public int izIndex;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fCanResize;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fCanResizeX;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fCanResizeY;
            public int iPreferredLeftPercent;
            public int iPreferredTopPercent;
        }

        private enum CompType : int
        {
            HTMLDOC = 0,
            PICTURE = 1,
            WEBSITE = 2,
            CONTROL = 3,
            CFHTML = 4
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
        private struct COMPONENT
        {
            private const int INTERNET_MAX_URL_LENGTH = 2084;
            public const int IS_NORMAL = 1;
            public const int IS_FULLSCREEN = 2;
            public const int IS_SPLIT = 4;
            public static readonly int SizeOf = Marshal.SizeOf(typeof(COMPONENT));

            public int dwSize;
            public int dwID;
            public CompType iComponentType;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fChecked;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fDirty;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fNoScroll;
            public COMPPOS cpPos;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string wszFriendlyName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = INTERNET_MAX_URL_LENGTH)]
            public string wszSource;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = INTERNET_MAX_URL_LENGTH)]
            public string wszSubscribedURL;

            public int dwCurItemState;
            public COMPSTATEINFO csiOriginal;
            public COMPSTATEINFO csiRestored;
        }

        private enum DtiAddUI : int
        {
            DEFAULT = 0x00000000,
            DISPSUBWIZARD = 0x00000001,
            POSITIONITEM = 0x00000002,
        }

        [Flags]
        private enum ComponentModify : int
        {
            TYPE = 0x00000001,
            CHECKED = 0x00000002,
            DIRTY = 0x00000004,
            NOSCROLL = 0x00000008,
            POS_LEFT = 0x00000010,
            POS_TOP = 0x00000020,
            SIZE_WIDTH = 0x00000040,
            SIZE_HEIGHT = 0x00000080,
            POS_ZINDEX = 0x00000100,
            SOURCE = 0x00000200,
            FRIENDLYNAME = 0x00000400,
            SUBSCRIBEDURL = 0x00000800,
            ORIGINAL_CSI = 0x00001000,
            RESTORED_CSI = 0x00002000,
            CURITEMSTATE = 0x00004000,
            ALL = TYPE | CHECKED | DIRTY | NOSCROLL | POS_LEFT | SIZE_WIDTH |
                SIZE_HEIGHT | POS_ZINDEX | SOURCE |
                FRIENDLYNAME | POS_TOP | SUBSCRIBEDURL | ORIGINAL_CSI |
                RESTORED_CSI | CURITEMSTATE
        }

        [Flags]
        private enum AddURL : int
        {
            SILENT = 0x0001
        }

        [ComImport]
        [Guid("F490EB00-1240-11D1-9888-006097DEACF9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActiveDesktop
        {
            [PreserveSig]
            int ApplyChanges(AD_Apply dwFlags);
            [PreserveSig]
            int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszWallpaper, int cchWallpaper, int dwReserved);
            [PreserveSig]
            int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string pwszWallpaper, int dwReserved);
            [PreserveSig]
            int GetWallpaperOptions(ref WALLPAPEROPT pwpo, int dwReserved);
            [PreserveSig]
            int SetWallpaperOptions(ref WALLPAPEROPT pwpo, int dwReserved);
            [PreserveSig]
            int GetPattern([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszPattern, int cchPattern, int dwReserved);
            [PreserveSig]
            int SetPattern([MarshalAs(UnmanagedType.LPWStr)] string pwszPattern, int dwReserved);
            [PreserveSig]
            int GetDesktopItemOptions(ref COMPONENTSOPT pco, int dwReserved);
            [PreserveSig]
            int SetDesktopItemOptions(ref COMPONENTSOPT pco, int dwReserved);
            [PreserveSig]
            int AddDesktopItem(ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int AddDesktopItemWithUI(IntPtr hwnd, ref COMPONENT pcomp, DtiAddUI dwFlags);
            [PreserveSig]
            int ModifyDesktopItem(ref COMPONENT pcomp, ComponentModify dwFlags);
            [PreserveSig]
            int RemoveDesktopItem(ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int GetDesktopItemCount(out int lpiCount, int dwReserved);
            [PreserveSig]
            int GetDesktopItem(int nComponent, ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int GetDesktopItemByID(IntPtr dwID, ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int GenerateDesktopItemHtml([MarshalAs(UnmanagedType.LPWStr)] string pwszFileName, ref COMPONENT pcomp, int dwReserved);
            [PreserveSig]
            int AddUrl(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszSource, ref COMPONENT pcomp, AddURL dwFlags);
            [PreserveSig]
            int GetDesktopItemBySource([MarshalAs(UnmanagedType.LPWStr)] string pwszSource, ref COMPONENT pcomp, int dwReserved);
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
            var hWnd = NativeMethods.FindWindow("Progman", IntPtr.Zero);

            NativeMethods.SendMessageTimeout(hWnd, 0x52c, IntPtr.Zero,
                IntPtr.Zero, 0, 500, out IntPtr _);

            _activeDesktop.SetWallpaper(wallpaperFile, 0);

            const AD_Apply flags = AD_Apply.ALL | AD_Apply.FORCE | AD_Apply.BUFFERED_REFRESH;
            _activeDesktop.ApplyChanges(flags);
        }

        private static void SetWallpaper(string wallpaperFile) 
            => NativeMethods.SystemParametersInfo(SPI_SETDESKWALLPAPER, 0,
                wallpaperFile, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);

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

            if (Settings.Default.setOnAllVirtualDesktops)
                VirtualDesktopManager.SetWallpaperOnAllVirtualDesktops(wallpaperFile);
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}