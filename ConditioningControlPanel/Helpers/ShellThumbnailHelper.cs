using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// Helper class to extract thumbnails using Windows Shell API.
    /// This provides the same thumbnails that Windows Explorer shows.
    /// </summary>
    public static class ShellThumbnailHelper
    {
        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;

            public SIZE(int cx, int cy)
            {
                this.cx = cx;
                this.cy = cy;
            }
        }

        [Flags]
        private enum SIIGBF
        {
            SIIGBF_RESIZETOFIT = 0x00000000,
            SIIGBF_BIGGERSIZEOK = 0x00000001,
            SIIGBF_MEMORYONLY = 0x00000002,
            SIIGBF_ICONONLY = 0x00000004,
            SIIGBF_THUMBNAILONLY = 0x00000008,
            SIIGBF_INCACHEONLY = 0x00000010,
            SIIGBF_CROPTOSQUARE = 0x00000020,
            SIIGBF_WIDETHUMBNAILS = 0x00000040,
            SIIGBF_ICONBACKGROUND = 0x00000080,
            SIIGBF_SCALEUP = 0x00000100,
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        private static readonly Guid IShellItemImageFactoryGuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        /// <summary>
        /// Gets a thumbnail for a file using Windows Shell API.
        /// This works for images, videos, and other file types that have shell handlers.
        /// </summary>
        /// <param name="filePath">Full path to the file</param>
        /// <param name="width">Desired thumbnail width</param>
        /// <param name="height">Desired thumbnail height</param>
        /// <returns>BitmapSource thumbnail or null if failed</returns>
        public static BitmapSource? GetThumbnail(string filePath, int width = 100, int height = 100)
        {
            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                SHCreateItemFromParsingName(filePath, IntPtr.Zero, IShellItemImageFactoryGuid, out var factory);

                var size = new SIZE(width, height);
                var hr = factory.GetImage(size, SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_THUMBNAILONLY, out hBitmap);

                if (hr != 0 || hBitmap == IntPtr.Zero)
                {
                    // Try without THUMBNAILONLY flag (falls back to icon for some files)
                    hr = factory.GetImage(size, SIIGBF.SIIGBF_BIGGERSIZEOK, out hBitmap);
                    if (hr != 0 || hBitmap == IntPtr.Zero)
                        return null;
                }

                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                source.Freeze(); // Make it cross-thread safe
                return source;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
            }
        }
    }
}
