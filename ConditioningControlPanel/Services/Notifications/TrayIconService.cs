using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Manages the system tray icon for minimizing to tray
/// </summary>
    public class TrayIconService : IDisposable
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        private NotifyIcon? _notifyIcon;
        private readonly Window _mainWindow;
        private bool _isDisposed;
        private bool _hasShownFirstMinimizeNotification;
        // WPF forbids Show() after a Window is closed. A second app instance can signal us to show the
        // window (App's show-signal handler) AFTER the MainWindow has been closed — MainWindowRef is then
        // a stale, non-null reference — which threw InvalidOperationException. Track closure to no-op.
        private bool _windowClosed;

        public event Action? OnShowRequested;
        public event Action? OnExitRequested;
        public event Action? OnWakeBambiRequested;
    public TrayIconService(Window mainWindow)
    {
        _mainWindow = mainWindow;
        _mainWindow.Closed += (_, _) => _windowClosed = true;
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "Conditioning Control Panel",
                Visible = false
            };

            Icon? icon = null;

            // First try to load from embedded resource
            try
            {
                var resourceUri = new Uri("pack://application:,,,/Resources/app.ico", UriKind.Absolute);
                var streamInfo = System.Windows.Application.GetResourceStream(resourceUri);
                if (streamInfo != null)
                {
                    icon = new Icon(streamInfo.Stream);
                    App.Logger?.Debug("Loaded tray icon from embedded resource");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Could not load embedded icon: {Error}", ex.Message);
            }

            // Fallback: try to load icon from file paths
            if (icon == null)
            {
                var iconPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico"),
                };

                foreach (var path in iconPaths)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            icon = new Icon(path);
                            App.Logger?.Debug("Loaded tray icon from file: {Path}", path);
                            break;
                        }
                        catch { }
                    }
                }
            }

            // Fallback to system icon if custom icon not found
            _notifyIcon.Icon = icon ?? SystemIcons.Application;

            // Create context menu
            var contextMenu = new ContextMenuStrip();

            var showItem = new ToolStripMenuItem("Show Dashboard");
            showItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(showItem);

            var wakeLabel = App.Mods?.IsBambiMode == true ? "Wake Bambi Up!" : "Wake Up!";
            var wakeBambiItem = new ToolStripMenuItem(wakeLabel);
            wakeBambiItem.Click += (s, e) => OnWakeBambiRequested?.Invoke();
            contextMenu.Items.Add(wakeBambiItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => OnExitRequested?.Invoke();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
            // Single left-click restores too — users expect it, and "clicking the tray
            // icon does nothing" reads as the app being gone (#475). Right-click still
            // opens the context menu; a double-click just calls ShowWindow twice (no-op).
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowWindow();
            };

            App.Logger?.Debug("Tray icon initialized");
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to initialize tray icon: {Error}", ex.Message);
        }
    }

    public void Show()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
        }
    }

    public void Hide()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
        }
    }

    public void MinimizeToTray()
    {
        _mainWindow.Hide();
        Show();

        // Only show the balloon tip notification on first minimize
        // This prevents annoying repeated notifications (which can stay visible indefinitely on some systems)
        if (!_hasShownFirstMinimizeNotification)
        {
            _hasShownFirstMinimizeNotification = true;
            _notifyIcon?.ShowBalloonTip(2000, "Conditioning Control Panel",
                "Running in background. Click the tray icon to restore.", ToolTipIcon.Info);
        }
    }

    public void ShowWindow()
    {
        // The MainWindow has been closed (app shutting down / tray-only with a dead window) — re-showing
        // it would throw. No-op: there's no live window to bring forward.
        if (_windowClosed)
        {
            App.Logger?.Debug("TrayIconService.ShowWindow ignored — MainWindow already closed");
            return;
        }

        // Dismiss any active balloon tip by hiding the icon (this clears balloon notifications)
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        EnsureOnScreen();
        var windowHandle = new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle;
        SetForegroundWindow(windowHandle);
        _mainWindow.Activate();
        // Hide() is now redundant since we already set Visible = false above
        OnShowRequested?.Invoke();
    }

    /// <summary>
    /// MinimizeToTray uses Hide(), so the window keeps its last Left/Top. If that spot
    /// is no longer on any live screen (monitor unplugged / display change while hidden)
    /// the restored window is invisible and the tray icon looks broken (#475).
    /// </summary>
    private void EnsureOnScreen()
    {
        try
        {
            var screens = Screen.AllScreens;
            if (screens == null || screens.Length == 0) return;

            var source = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle);
            var toDevice = source?.CompositionTarget?.TransformToDevice;
            double sx = toDevice?.M11 ?? 1.0, sy = toDevice?.M22 ?? 1.0;

            var rect = new Rectangle(
                (int)(_mainWindow.Left * sx), (int)(_mainWindow.Top * sy),
                (int)(_mainWindow.ActualWidth * sx), (int)(_mainWindow.ActualHeight * sy));

            foreach (var screen in screens)
            {
                var visible = Rectangle.Intersect(screen.WorkingArea, rect);
                if (visible.Width >= 60 && visible.Height >= 30) return; // enough to grab the title bar
            }

            var wa = (Screen.PrimaryScreen ?? screens[0]).WorkingArea;
            _mainWindow.Left = wa.Left / sx + (wa.Width / sx - _mainWindow.ActualWidth) / 2.0;
            _mainWindow.Top = wa.Top / sy + (wa.Height / sy - _mainWindow.ActualHeight) / 2.0;
            App.Logger?.Information("Tray restore: window was off-screen, re-centered on primary");
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("TrayIconService.EnsureOnScreen failed: {Error}", ex.Message);
        }
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, message, icon);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
