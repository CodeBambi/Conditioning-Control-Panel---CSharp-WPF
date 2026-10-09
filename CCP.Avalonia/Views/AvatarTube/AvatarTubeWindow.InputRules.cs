// WPF AvatarTubeWindow.ChatInput.cs Window_PreviewMouseDown / ImgAvatar_MouseRightButtonDown / HideInputPanel,
// and the click-through WPF got for free from AllowsTransparency (a layered window: the OS passes a click on a
// fully transparent pixel to the window below). The port's tube is a composited transparent window, so without
// this its empty 768 px box swallowed every click on the part of the panel it overlaps (the nav rail when
// docked left) and the chat box, once open, never closed: "the prompt field stays on and I can't click anywhere".
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private const uint WmNcHitTest = 0x0084;
        private static readonly IntPtr HtTransparent = new(-1);

        private void AttachTubeInputRules()
        {
            // WPF Window_PreviewMouseDown: a click anywhere in the tube outside the input panel closes it.
            AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (!_inputPanel.IsVisible) return;
                if (e.Source is Visual v && (ReferenceEquals(v, _inputPanel) || _inputPanel.IsVisualAncestorOf(v))) return;
                HideInputPanel();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            // WPF ImgAvatar_MouseRightButtonDown: right-click on her closes it too.
            _avatarBorder.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(_avatarBorder).Properties.IsRightButtonPressed) HideInputPanel();
            }, RoutingStrategies.Tunnel, handledEventsToo: true);

            if (OperatingSystem.IsWindows())
            {
                try { Win32Properties.AddWndProcHookCallback(this, ClickThroughHook); }
                catch (Exception ex) { Log.Warning("AvatarTube click-through hook unavailable: {E}", ex.Message); }
            }
        }

        /// <summary>WPF HideInputPanel.</summary>
        internal void HideInputPanel()
        {
            if (_inputPanel.IsVisible) _inputPanel.IsVisible = false;
        }

        /// <summary>WM_NCHITTEST on an empty, fully transparent spot answers HTTRANSPARENT, so Windows hands the
        /// click to the window below on this thread (main, when she hangs over its edge), as a layered WPF window did.</summary>
        private IntPtr ClickThroughHook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmNcHitTest) return IntPtr.Zero;
            try
            {
                long lp = lParam.ToInt64();
                var screen = new PixelPoint((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                var hit = this.InputHitTest(this.PointToClient(screen));
                if (IsClearHit(hit))
                {
                    handled = true;
                    return HtTransparent;
                }
            }
            catch (Exception ex) { Log.Debug("AvatarTube hit test: {E}", ex.Message); }
            return IntPtr.Zero;
        }

        /// <summary>True when the topmost hit is only empty layout (the window's own transparent fill, or a
        /// panel/border/presenter painting nothing): what a layered window would have let through.</summary>
        internal bool IsClearHit(IInputElement? hit) => hit switch
        {
            null => true,
            Window w => IsClear(w.Background),
            Panel p => IsClear(p.Background),
            Border b => IsClear(b.Background) && (IsClear(b.BorderBrush) || b.BorderThickness == default),
            ContentPresenter cp => IsClear(cp.Background) && (IsClear(cp.BorderBrush) || cp.BorderThickness == default),
            _ => false,
        };

        private static bool IsClear(IBrush? brush) =>
            brush is null || brush.Opacity <= 0 || brush is ISolidColorBrush { Color.A: 0 };
    }
}
