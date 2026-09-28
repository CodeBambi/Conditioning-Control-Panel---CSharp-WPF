using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The panic key for NATIVE Wayland windows: org.freedesktop.portal.GlobalShortcuts (CreateSession,
/// BindShortcuts, Activated), raw over Tmds.DBus.Protocol like the tray probe. X11 focus is
/// <see cref="X11PanicKey"/>'s job; this covers the rest.
///
/// <para><b>Unlike the X11 listener this CONSUMES the key desktop-wide</b> - it is a compositor
/// grab, and the compositor (KDE: kglobalaccel) may show the user a confirmation dialog and lets
/// them rebind or refuse it. WHEN to bind is therefore the caller's policy
/// (docs/avalonia-decisions.md, panic key row); this class only binds and unbinds.</para>
/// </summary>
internal static class PortalPanicShortcut
{
    private const string Portal = "org.freedesktop.portal.Desktop", DesktopPath = "/org/freedesktop/portal/desktop";
    private const string Iface = "org.freedesktop.portal.GlobalShortcuts", Id = "panic";

    private static string? _session;
    private static IDisposable? _activated;
    private static DBusConnection? _bus;

    internal static bool IsBound => !string.IsNullOrEmpty(_session);

    /// <summary>Opens a portal session and binds <paramref name="key"/> (a stored Key name) as the
    /// "panic" shortcut. <paramref name="onActivated"/> runs on a D-Bus thread. Returns the trigger
    /// the compositor actually assigned (the user may have changed it; "" if it did not say), or
    /// null when the portal is absent, the user refused, or anything failed - never throws.</summary>
    internal static async Task<string?> BindAsync(string key, string description, Action onActivated)
    {
        if (!OperatingSystem.IsLinux() || _session != null) return null;
        _session = "";   // claimed: a second caller while this one waits gets null, not a second session
        try
        {
            // Own connection, not DBusConnection.Session: the shared one autoconnects, and UniqueName
            // (needed for the request path) throws on autoconnect connections. Dropping it also
            // makes the portal close the session, so no grab can outlive this connection.
            var bus = _bus = new DBusConnection(DBusAddress.Session!);
            await bus.ConnectAsync();

            var (code, results) = await RequestAsync(bus, "CreateSession", "a{sv}", (ref MessageWriter w, string token) =>
                w.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["handle_token"] = token,
                    ["session_handle_token"] = "ccp_panic",
                }));
            if (code != 0 || !results.TryGetValue("session_handle", out var handle)) { await UnbindAsync(); return Fail($"CreateSession answered {code}"); }
            // The spec types session_handle as a string holding an object path; read either.
            var session = handle.Type == VariantValueType.ObjectPath ? handle.GetObjectPathAsString() : handle.GetString();
            _session = session;   // from here on every failure path closes it

            _activated = await bus.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = Iface, Member = "Activated", Path = DesktopPath },
                (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPathAsString(), r.ReadString()); },
                (Exception? ex, (string Session, string Id) a, object? _, object? _) =>
                {
                    if (ex == null && a.Session == session && a.Id == Id) onActivated();
                },
                null, null, false, ObserverFlags.None);

            (code, results) = await RequestAsync(bus, "BindShortcuts", "oa(sa{sv})sa{sv}", (ref MessageWriter w, string token) =>
            {
                w.WriteObjectPath(session);
                var shortcuts = w.WriteArrayStart(DBusType.Struct);
                w.WriteStructureStart();
                w.WriteString(Id);
                w.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["description"] = description,
                    ["preferred_trigger"] = X11PanicKey.XKeyName(key),
                });
                w.WriteArrayEnd(shortcuts);
                w.WriteString("");   // parent_window: none
                w.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token });
            });
            if (code != 0) { await UnbindAsync(); return Fail($"BindShortcuts answered {code} (refused or cancelled)"); }
            var trigger = TriggerOf(results);
            Log.Information("Panic key: bound '{Key}' through the GlobalShortcuts portal, trigger '{Trigger}'", key, trigger);
            return trigger;
        }
        catch (Exception ex)
        {
            await UnbindAsync();
            return Fail(ex is TimeoutException ? "no answer in 30 s (an unanswered dialog counts as refused)" : ex.Message);
        }

        static string? Fail(string why)
        {
            Log.Warning("Panic key: GlobalShortcuts portal unavailable ({Why}); native Wayland windows rely on the tray", why);
            return null;
        }
    }

    /// <summary>`--portal-check`: CreateSession, expect Response 0 with a session_handle, then
    /// Session.Close - the round trip BindAsync depends on, without binding anything.</summary>
    internal static async Task<int> CheckAsync()
    {
        using var bus = new DBusConnection(DBusAddress.Session!);
        await bus.ConnectAsync();
        Console.WriteLine($"connected as {bus.UniqueName}");
        var (code, results) = await RequestAsync(bus, "CreateSession", "a{sv}", (ref MessageWriter w, string token) =>
            w.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = token,
                ["session_handle_token"] = "ccp_check",
            }));
        var ok = code == 0 && results.TryGetValue("session_handle", out var handle);
        Console.WriteLine($"CreateSession response {code}, session_handle {(ok ? results["session_handle"].ToString() : "(none)")}");
        if (!ok) return 1;
        var h = results["session_handle"];
        var session = h.Type == VariantValueType.ObjectPath ? h.GetObjectPathAsString() : h.GetString();
        await bus.CallMethodAsync(CallHeader(bus, session, "org.freedesktop.portal.Session", "Close", null));
        Console.WriteLine("Session.Close ok");
        return 0;
    }

    /// <summary>trigger_description of our shortcut in a BindShortcuts result (a(sa{sv})).</summary>
    private static string TriggerOf(Dictionary<string, VariantValue> results)
    {
        if (!results.TryGetValue("shortcuts", out var list)) return "";
        for (var i = 0; i < list.Count; i++)
        {
            var shortcut = list.GetItem(i);
            if (shortcut.GetItem(0).GetString() != Id) continue;
            var props = shortcut.GetItem(1);
            for (var j = 0; j < props.Count; j++)
            {
                var e = props.GetDictionaryEntry(j);
                if (e.Key.GetString() != "trigger_description") continue;
                var v = e.Value.Type == VariantValueType.Variant ? e.Value.GetVariantValue() : e.Value;
                return v.GetString();
            }
        }
        return "";
    }

    /// <summary>Closes the portal session, which releases the compositor grab.</summary>
    internal static async Task UnbindAsync()
    {
        var session = _session;
        _session = null;
        if (session == "") session = null;
        _activated?.Dispose();
        _activated = null;
        var bus = _bus;
        _bus = null;
        if (bus == null) return;
        try
        {
            if (session != null)
                await bus.CallMethodAsync(CallHeader(bus, session, "org.freedesktop.portal.Session", "Close", null));
        }
        catch (Exception ex) { Log.Warning(ex, "Panic key: closing the portal session failed"); }
        bus.Dispose();
    }

    /// <summary>One portal Request round trip: subscribe to the Response on the predicted request
    /// path BEFORE calling (the portal may answer before the call returns), then call and await.</summary>
    private static async Task<(uint Code, Dictionary<string, VariantValue> Results)> RequestAsync(
        DBusConnection bus, string member, string signature, BodyWriter writeBody)
    {
        var token = "ccp" + Guid.NewGuid().ToString("N");
        var sender = bus.UniqueName!.TrimStart(':').Replace('.', '_');
        var answer = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = await bus.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Request", Member = "Response",
                            Path = $"{DesktopPath}/request/{sender}/{token}" },
            (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadUInt32(), r.ReadDictionaryOfStringToVariantValue()); },
            (Exception? ex, (uint, Dictionary<string, VariantValue>) v, object? _, object? _) =>
            {
                if (ex != null) answer.TrySetException(ex); else answer.TrySetResult(v);
            },
            null, null, false, ObserverFlags.None);

        await bus.CallMethodAsync(CallHeader(bus, DesktopPath, Iface, member, signature, (ref MessageWriter w) => writeBody(ref w, token)));
        // KDE may show the user a dialog for BindShortcuts; effects wait at most this long for it.
        return await answer.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    // MessageWriter is a STRUCT: handing it to an Action<MessageWriter> writes the body into a copy,
    // and the original then serialises a header whose body length/offset do not match what was
    // written. dbus-broker answers "invalid body" by disconnecting ("Connection closed by peer").
    // By-ref delegates keep one writer.
    private delegate void BodyWriter(ref MessageWriter w, string token);
    private delegate void BodyWriterNoToken(ref MessageWriter w);

    private static MessageBuffer CallHeader(DBusConnection bus, string path, string iface, string member, string? signature,
        BodyWriterNoToken? body = null)
    {
        var w = bus.GetMessageWriter();
        w.WriteMethodCallHeader(destination: Portal, path: path, @interface: iface, member: member, signature: signature);
        body?.Invoke(ref w);
        return w.CreateMessage();
    }
}
