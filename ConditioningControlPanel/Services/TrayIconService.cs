using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Manages the system tray icon for minimizing to tray
/// </summary>
public class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly Window _mainWindow;
    private bool _isDisposed;

    public event Action? OnShowRequested;
    public event Action? OnExitRequested;

    public TrayIconService(Window mainWindow)
    {
        _mainWindow = mainWindow;
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

            // Try to load icon from multiple paths
            var iconPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico"),
            };

            Icon? icon = null;
            foreach (var path in iconPaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        icon = new Icon(path);
                        break;
                    }
                    catch { }
                }
            }

            // Fallback to system icon if custom icon not found
            _notifyIcon.Icon = icon ?? SystemIcons.Application;

            // Create context menu
            var contextMenu = new ContextMenuStrip();
            
            var showItem = new ToolStripMenuItem("Show Dashboard");
            showItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(showItem);
            
            contextMenu.Items.Add(new ToolStripSeparator());
            
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => OnExitRequested?.Invoke();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();

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
        _notifyIcon?.ShowBalloonTip(2000, "Conditioning Control Panel", 
            "Running in background. Double-click to restore.", ToolTipIcon.Info);
    }

    public void ShowWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        Hide();
        OnShowRequested?.Invoke();
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
