using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// OS notifications - this head's TrayIconService.ShowNotification (WPF balloon tip,
/// ConditioningControlPanel/Services/Notifications/TrayIconService.cs:239). Linux:
/// org.freedesktop.Notifications Notify / ActionInvoked / NotificationClosed, raw over
/// Tmds.DBus.Protocol like <see cref="PortalPanicShortcut"/>. No server (or not Linux): the in-app
/// toast when the window is visible, otherwise dropped and logged once.
/// ponytail: Windows has no Avalonia toast API, so it always takes the fallback; add a WinRT
/// toast if balloons matter there.
/// </summary>
internal static class OsNotifications
{
    internal enum Route { Os, Toast, Drop }

    private const string Dest = "org.freedesktop.Notifications", ObjPath = "/org/freedesktop/Notifications";
    private static Task<DBusConnection>? _bus;
    private static readonly Dictionary<uint, Action> _clicks = new();
    private static bool _droppedLogged;

    /// <summary>Test seam: when set, every notification goes here instead of D-Bus and counts as not delivered
    /// (so <see cref="Decide"/> still picks toast/drop). Tests assert "a notification was requested" with it.</summary>
    internal static Action<string, string>? Sink;

    /// <summary>Notify calls that actually reached for the session bus (test seam: a sandbox keeps it at 0).</summary>
    internal static int BusAttempts;

    /// <summary>The fallback decision, pure so it is testable headless.</summary>
    internal static Route Decide(bool osDelivered, bool windowVisible) =>
        osDelivered ? Route.Os : windowVisible ? Route.Toast : Route.Drop;

    /// <summary>Fire and forget. <paramref name="onClick"/> runs on the UI thread when the user
    /// clicks the OS notification (the toast fallback, like a WPF balloon, has no click).</summary>
    internal static void Show(string title, string body, Action? onClick = null) => _ = ShowAsync(title, body, onClick);

    internal static async Task ShowAsync(string title, string body, Action? onClick)
    {
        uint id = 0;
        if (Sink is { } sink) sink(title, body);
        else if (OperatingSystem.IsLinux())
        {
            try { id = await NotifyAsync(title, body, onClick != null); }
            catch (Exception ex) { Log.Debug("OS notification failed: {Error}", ex.Message); }
        }
        if (id != 0 && onClick != null) lock (_clicks) _clicks[id] = onClick;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var visible = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.IsVisible == true;
            switch (Decide(id != 0, visible))
            {
                case Route.Toast:
                    App.Notifications.Show($"{title}: {body}");
                    break;
                case Route.Drop when !_droppedLogged:
                    _droppedLogged = true;
                    Log.Information("No notification server and window hidden; dropping OS notifications (logged once)");
                    break;
            }
        });
    }

    // One cached task: concurrent first calls share one connection; a failed connect is retried next time.
    private static Task<DBusConnection> BusAsync()
    {
        lock (_clicks)
        {
            if (_bus is { IsFaulted: false, IsCanceled: false }) return _bus;
            return _bus = ConnectAsync();
        }
    }

    private static async Task<DBusConnection> ConnectAsync()
    {
        var bus = new DBusConnection(DBusAddress.Session!);
        try
        {
        await bus.ConnectAsync();
        await bus.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = Dest, Member = "ActionInvoked", Path = ObjPath },
            (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadUInt32(), r.ReadString()); },
            (Exception? ex, (uint Id, string Key) a, object? _, object? _) =>
            {
                Action? click;
                lock (_clicks) _clicks.Remove(a.Id, out click);
                if (ex == null && click != null) Dispatcher.UIThread.Post(click);
            },
            null, null, false, ObserverFlags.None);
        await bus.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = Dest, Member = "NotificationClosed", Path = ObjPath },
            (Message m, object? _) => m.GetBodyReader().ReadUInt32(),
            (Exception? ex, uint id, object? _, object? _) => { lock (_clicks) _clicks.Remove(id); },
            null, null, false, ObserverFlags.None);
        }
        catch { bus.Dispose(); throw; }
        return bus;
    }

    /// <summary>Notify; returns the server's id (0 never comes back from a real server).
    /// A CCP_USERDATA_DIR sandbox (<see cref="SandboxNet.Active"/>: tests, kc, render-all) never reaches the user's
    /// desktop: it returns 0 (not delivered) unless <paramref name="live"/> - only `--notify-check`, the deliberate probe.</summary>
    internal static async Task<uint> NotifyAsync(string title, string body, bool clickable, bool live = false)
    {
        if (SandboxNet.Active && !live) return 0;
        Interlocked.Increment(ref BusAttempts);
        var bus = await BusAsync();
        var w = bus.GetMessageWriter();
        w.WriteMethodCallHeader(destination: Dest, path: ObjPath, @interface: Dest, member: "Notify", signature: "susssasa{sv}i");
        w.WriteString("Conditioning Control Panel");
        w.WriteUInt32(0);
        w.WriteString(IconPath());
        w.WriteString(title);
        w.WriteString(body);
        w.WriteArray(clickable ? new[] { "default", title } : Array.Empty<string>());
        w.WriteDictionary(new Dictionary<string, VariantValue> { ["transient"] = true });   // no history, like a balloon
        w.WriteInt32(-1);
        return await bus.CallMethodAsync(w.CreateMessage(), (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null);
    }

    private static string? _icon;
    /// <summary>app.ico saved once as a per-user PNG (XDG_RUNTIME_DIR, else temp) so the server can
    /// attribute the notification to CCP; "" (server default) if Avalonia's asset loader is not up.</summary>
    private static string IconPath()
    {
        if (_icon != null) return _icon;
        try
        {
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            var path = Path.Combine(string.IsNullOrEmpty(dir) ? Path.GetTempPath() : dir, "ccp-notify-icon.png");
            using (var src = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/app.ico")))
            using (var bmp = new global::Avalonia.Media.Imaging.Bitmap(src)) bmp.Save(path);
            return _icon = path;
        }
        catch { return _icon = ""; }
    }

    /// <summary>`--notify-check`: Notify, print the id, CloseNotification it. Exit 0 = live round trip.</summary>
    internal static async Task<int> CheckAsync()
    {
        var id = await NotifyAsync("Conditioning Control Panel", "notify-check", false, live: true);
        Console.WriteLine($"Notify returned id {id}");
        if (id == 0) return 1;
        var bus = await BusAsync();
        var w = bus.GetMessageWriter();
        w.WriteMethodCallHeader(destination: Dest, path: ObjPath, @interface: Dest, member: "CloseNotification", signature: "u");
        w.WriteUInt32(id);
        await bus.CallMethodAsync(w.CreateMessage());
        Console.WriteLine("CloseNotification ok");
        return 0;
    }
}
