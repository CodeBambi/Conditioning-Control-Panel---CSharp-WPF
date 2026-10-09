// PORTED from WPF 7.1.5 AvatarTube/AvatarTubeWindow.Windowing.cs: ApplyTubeArtFlip / TubeFlipAxis
// (:1034-1078), the mirrored left inset UpdatePosition hands TubeDockPlacement (:866-871), and
// ApplyNativeOwner (:621-630), the Win32 z-order pairing that keeps an attached tube above main.

using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private const double DesignCanvasHeight = 1080;   // the XAML's 780x1080 design grid

        private bool _tubeArtFlipped;

        /// <summary>True while the tube art is mirrored (attached on main's RIGHT).</summary>
        internal bool TubeArtFlipped => _tubeArtFlipped;

        /// <summary>
        /// WPF ApplyTubeArtFlip: docked on main's RIGHT, the tube ART is mirrored so its cables plug
        /// into main, as they do on the left. The axis is HER centre (the avatar box), not the art's:
        /// a mod tube can carry its glass off-centre (Circe's does). Only the frame image flips; the
        /// avatar sprite, labels, arrows and bubble keep reading normally.
        /// </summary>
        private void ApplyTubeArtFlip(bool flip)
        {
            if (!flip)
            {
                if (_tubeArtFlipped || _imgTubeFrame.RenderTransform != null)
                    _imgTubeFrame.RenderTransform = null;
                if (_tubeArtFlipped) { _tubeArtFlipped = false; ApplySpeechBubblePlacement(); }
                return;
            }

            double cx = TubeFlipAxisDesign() - _imgTubeFrame.Bounds.X;   // frame's own coordinates
            var origin = new RelativePoint(cx, 0, RelativeUnit.Absolute);
            if (_tubeArtFlipped && _imgTubeFrame.RenderTransform is ScaleTransform { ScaleX: -1 }
                && _imgTubeFrame.RenderTransformOrigin == origin) return;
            bool changed = !_tubeArtFlipped;
            _tubeArtFlipped = true;
            _imgTubeFrame.RenderTransformOrigin = origin;
            _imgTubeFrame.RenderTransform = new ScaleTransform(-1, 1);
            if (changed) ApplySpeechBubblePlacement();
        }

        /// <summary>WPF TubeFlipAxis: her centre on the 780-wide design grid. AvatarBorder is centred
        /// in its margin slot, so its centre is (L + width - R) / 2 whatever its own width is.</summary>
        private double TubeFlipAxisDesign()
        {
            var m = _avatarBorder.Margin;
            return _avatarBorder.HorizontalAlignment == HorizontalAlignment.Center
                ? (m.Left + DesignWidth - m.Right) / 2
                : _avatarBorder.Bounds.Width > 0 ? _avatarBorder.Bounds.Center.X : DesignWidth / 2;
        }

        /// <summary>The art's left inset on a RIGHT dock, in window units at scale 1 (the units of
        /// TubeArtLeftPadding / TubeArtRightPadding): the painted right edge reflected about her
        /// centre, 2c - (W - rightInset). The Viewbox fits the 780x1080 grid into 780x1020, so the
        /// grid is scaled by k and centred horizontally.</summary>
        internal double MirroredLeftInsetUnits()
        {
            double k = Math.Min(1.0, DesignHeight / DesignCanvasHeight);
            double axis = (DesignWidth - DesignWidth * k) / 2 + TubeFlipAxisDesign() * k;
            return Math.Max(0, 2 * axis - (DesignWidth - TubeArtRightPadding));
        }

        // ---------------- Win32 native owner (WPF ApplyNativeOwner) ----------------

        private const int GwlHwndParent = -8;
        private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpFrameChanged = 0x20;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>
        /// WPF ApplyNativeOwner: attached, the tube is natively OWNED by main, so Windows keeps it
        /// directly above main and raises / minimises the pair as a group; other windows can cover
        /// both. Without it (the port had only the X11 restack) clicking main left her under
        /// whatever app sat on that side of the screen: she read as gone. Raw GWL_HWNDPARENT, not
        /// Window.Owner, exactly as WPF (the managed owner couples visibility).
        /// </summary>
        private void ApplyNativeOwner(bool owned)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var me = TryGetPlatformHandle();
                if (me is not { HandleDescriptor: "HWND" } || me.Handle == IntPtr.Zero) return;
                var owner = IntPtr.Zero;
                if (owned)
                {
                    var p = _parentWindow?.TryGetPlatformHandle();
                    if (p is not { HandleDescriptor: "HWND" } || p.Handle == IntPtr.Zero) return;
                    owner = p.Handle;
                }
                SetWindowLongPtr(me.Handle, GwlHwndParent, owner);
                SetWindowPos(me.Handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
                Log.Information("AvatarTube native owner {State}", owned ? "set to main" : "cleared");
            }
            catch (Exception ex) { Log.Warning("AvatarTube native owner failed: {Error}", ex.Message); }
        }
    }
}
