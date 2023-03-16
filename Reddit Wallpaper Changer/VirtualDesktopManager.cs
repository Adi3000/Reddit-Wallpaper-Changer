using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Runtime.InteropServices;

namespace Reddit_Wallpaper_Changer
{
    public static class VirtualDesktopManager
    {
        [ComImport]
        [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [return: MarshalAs(UnmanagedType.IUnknown)]
            object QueryService(in Guid guidService, in Guid riid);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
        private interface IObjectArray
        {
            uint GetCount();

            [return: MarshalAs(UnmanagedType.Interface)]
            object GetAt(uint iIndex, in Guid riid);
        }

        [ComImport]
        [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopManager
        {
            bool IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow);
            Guid GetWindowDesktopId(IntPtr topLevelWindow);
            void MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("372e1d3b-38d3-42e4-a15b-8ab2b178f513")]
        private interface IApplicationView
        {
            void Proc3();
            void Proc4();
            void Proc5();
            void SetFocus();
            void SwitchTo();
            void TryInvokeBack(IntPtr callback);
            IntPtr GetThumbnailWindow();
            IntPtr GetMonitor();
            int GetVisibility();
            void SetCloak(ApplicationViewCloakType cloakType, int unknown);
            IntPtr GetPosition(in Guid guid, out IntPtr position);
            void SetPosition(in IntPtr position);
            void InsertAfterWindow(IntPtr hwnd);
            Rect GetExtendedFramePosition();
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetAppUserModelId();
            void SetAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id);
            bool IsEqualByAppUserModelId(string id);
            uint GetViewState();
            void SetViewState(uint state);
            int GetNeediness();
            ulong GetLastActivationTimestamp();
            void SetLastActivationTimestamp(ulong timestamp);
            Guid GetVirtualDesktopId();
            void SetVirtualDesktopId(in Guid guid);
            int GetShowInSwitchers();
            void SetShowInSwitchers(int flag);
            int GetScaleFactor();
            bool CanReceiveInput();
            ApplicationViewCompatibilityPolicy GetCompatibilityPolicyType();
            void SetCompatibilityPolicyType(ApplicationViewCompatibilityPolicy flags);
            IntPtr GetPositionPriority();
            void SetPositionPriority(IntPtr priority);
            void GetSizeConstraints(IntPtr monitor, out Size size1, out Size size2);
            void GetSizeConstraintsForDpi(uint uint1, out Size size1, out Size size2);
            void SetSizeConstraintsForDpi(ref uint uint1, in Size size1, in Size size2);
            int QuerySizeConstraintsFromApp();
            void OnMinSizePreferencesUpdated(IntPtr hwnd);
            void ApplyOperation(IntPtr operation);
            bool IsTray();
            bool IsInHighZOrderBand();
            bool IsSplashScreenPresented();
            void Flash();
            IApplicationView GetRootSwitchableOwner();
            IObjectArray EnumerateOwnershipTree();
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetEnterpriseId();
            bool IsMirrored();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Size
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private enum ApplicationViewCloakType
        {
            AVCT_NONE = 0,
            AVCT_DEFAULT = 1,
            AVCT_VIRTUAL_DESKTOP = 2
        }

        private enum ApplicationViewCompatibilityPolicy
        {
            AVCP_NONE = 0,
            AVCP_SMALL_SCREEN = 1,
            AVCP_TABLET_SMALL_SCREEN = 2,
            AVCP_VERY_SMALL_SCREEN = 3,
            AVCP_HIGH_SCALE_FACTOR = 4
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("536d3495-b208-4cc9-ae26-de8111275bf8")]
        private interface IVirtualDesktop
        {
            bool IsViewVisible(IApplicationView view);
            Guid GetId();
            IntPtr Unknown1();
            [return: MarshalAs(UnmanagedType.HString)]
            string GetName();
            [return: MarshalAs(UnmanagedType.HString)]
            string GetWallpaperPath();
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("b2f925b9-5a0f-4d2e-9f4d-2b1507593c10")]
        private interface IVirtualDesktopManagerInternal
        {
            int GetCount(IntPtr hWndOrMon);
            void MoveViewToDesktop(IApplicationView pView, IVirtualDesktop desktop);
            bool CanViewMoveDesktops(IApplicationView pView);
            IVirtualDesktop GetCurrentDesktop(IntPtr hWndOrMon);
            IObjectArray GetAllCurrentDesktops();
            IObjectArray GetDesktops(IntPtr hWndOrMon);
            IVirtualDesktop GetAdjacentDesktop(IVirtualDesktop pDesktopReference, int uDirection);
            void SwitchDesktop(IntPtr hWndOrMon, IVirtualDesktop desktop);
            IVirtualDesktop CreateDesktop(IntPtr hWndOrMon);
            void MoveDesktop(IVirtualDesktop desktop, IntPtr hWndOrMon, int nIndex);
            void RemoveDesktop(IVirtualDesktop pRemove, IVirtualDesktop pFallbackDesktop);
            IVirtualDesktop FindDesktop(in Guid desktopId);
            void GetDesktopSwitchIncludeExcludeViews(IVirtualDesktop desktop, out IObjectArray o1, out IObjectArray o2);
            void SetDesktopName(IVirtualDesktop desktop, [MarshalAs(UnmanagedType.HString)] string name);
            void SetDesktopWallpaper(IVirtualDesktop desktop, [MarshalAs(UnmanagedType.HString)] string path);
            void UpdateWallpaperPathForAllDesktops([MarshalAs(UnmanagedType.HString)] string path);
            void CopyDesktopState(IApplicationView pView0, IApplicationView pView1);
            bool GetDesktopIsPerMonitor();
            void SetDesktopIsPerMonitor(bool state);
        }

        private static readonly Guid ImmersiveShellCLSID = new Guid("c2f03a33-21f5-47fa-b4bb-156362a2f239");
        private static readonly Guid VirtualDesktopManagerInternalCLSID = new Guid("c5e0cdca-7b6e-41b2-9fc4-d93975cc467b");

        private static readonly IVirtualDesktopManagerInternal _virtualDesktopManagerInternal;

        private static bool _available;

        static VirtualDesktopManager()
        {
            try
            {
                var type = Type.GetTypeFromCLSID(ImmersiveShellCLSID);
                var shell = (IServiceProvider)Activator.CreateInstance(type);
                var guid = typeof(IVirtualDesktopManagerInternal).GUID;
                _virtualDesktopManagerInternal = (IVirtualDesktopManagerInternal)shell.QueryService(VirtualDesktopManagerInternalCLSID, guid);

                _available = true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unable to create virtual desktop manager, {ex.Message}", LogLevel.Warning);
                _available = false;
            }
        }

        public static void SetWallpaperOnAllVirtualDesktops(string wallpaperFile)
        {
            try
            {
                if (_available)
                    _virtualDesktopManagerInternal?.UpdateWallpaperPathForAllDesktops(wallpaperFile);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessageToFile($"Unable to update wallpapers on all virtual desktops, {ex.Message}", LogLevel.Warning);
                _available = false;
            }
        }
    }
}
