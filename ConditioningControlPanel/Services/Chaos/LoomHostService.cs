using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Hosts THE LOOM as a main-app window: the enhanced spiral creator, open to
/// everyone from the Spiral Overlay feature card (crafting it in DtRH remains
/// the in-game way in). A stripped-down sibling of <see cref="DtrhHostService"/>:
/// one windowed <see cref="ChaosWebViewHost"/> pointed at
/// <c>Resources/web/dtrh/loom.html</c>, speaking only the loom bridge subset
/// (loom-save / loom-delete / loom-reveal / sfx out of the page,
/// loom-result / loom-list in).
/// File authority stays in <see cref="DtrhLoomStore"/> - saves land in the CCP
/// Spirals library, so the overlay and the SpiralFeatureControl pick them up
/// exactly like spirals woven inside the game.
/// </summary>
internal static class LoomHostService
{
    private static ChaosWebViewHost? _host;

    /// <summary>True while the Loom window is open.</summary>
    public static bool IsActive => _host != null;

    /// <summary>Open the Loom window (idempotent - refocuses if already open).</summary>
    public static void Launch()
    {
        if (_host != null) { _host.FocusWeb(); return; }
        try
        {
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Resources", "web");
            // A missing folder would make WebView2 SKIP the ccp.spirals mapping
            // entirely (same rule as the game host), so create it BEFORE Launch.
            try { Directory.CreateDirectory(DtrhLoomStore.SpiralsFolder); }
            catch (Exception ex) { App.Logger?.Debug("LoomHost: spirals dir create failed: {E}", ex.Message); }

            _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
            {
                StartUrl = "https://ccp.game/dtrh/loom.html",
                PrimaryHost = "ccp.game",
                Mappings = new List<(string, string, CoreWebView2HostResourceAccessKind)>
                {
                    ("ccp.game", webRoot, CoreWebView2HostResourceAccessKind.Deny),
                    // The saved-spiral GIFs (rack thumbnails). Allow: same policy the
                    // game host uses for this mapping.
                    ("ccp.spirals", DtrhLoomStore.SpiralsFolder, CoreWebView2HostResourceAccessKind.Allow),
                },
                // Own browser profile: the game's WebView2 state stays untouched.
                UserDataFolderName = "browser_data_loom",
                InputEnabled = true,
                StartFullscreen = false,
                WindowTitle = "The Loom",
                LogTag = "LoomHost",
                OnReady = PostLoomList,
                OnMessage = OnPageMessage,
                OnProcessFailed = _ => Close(),
            });
            _host.Show();
            // A plain titled window: the user closes it with X, not a page exit
            // protocol - release the singleton when that happens.
            if (_host.Window != null)
                _host.Window.Closed += (_, _) => Close();
            App.Logger?.Information("LoomHostService: launched");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "LoomHostService.Launch failed");
            Close();
        }
    }

    /// <summary>Tear the window down (idempotent; also the ProcessFailed path).</summary>
    public static void Close()
    {
        var h = _host;
        _host = null;
        try { h?.Dispose(); }
        catch (Exception ex) { App.Logger?.Debug("LoomHost.Close: {E}", ex.Message); }
    }

    private static void OnPageMessage(JObject o)
    {
        switch ((string?)o["type"])
        {
            case "loom-save":
            {
                var (ok, slug, error) = DtrhLoomStore.Save(
                    (string?)o["name"], (string?)o["gifBase64"], o["params"], (bool?)o["overwrite"] ?? false);
                _host?.Post(new { type = "loom-result", op = "save", ok, slug, error });
                if (ok) PostLoomList();
                break;
            }
            case "loom-delete":
            {
                var slug = (string?)o["slug"];
                var (ok, error) = DtrhLoomStore.Delete(slug);
                _host?.Post(new { type = "loom-result", op = "delete", ok, slug, error });
                if (ok) PostLoomList();
                break;
            }
            case "loom-reveal":
            {
                // 📂 on a rack tile: show the saved gif in Explorer. Path comes from
                // the slug-whitelisted store, never from the page.
                var path = DtrhLoomStore.GifPathFor((string?)o["slug"]);
                if (path != null)
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
                    catch (Exception ex) { App.Logger?.Debug("LoomHost: reveal failed: {E}", ex.Message); }
                }
                break;
            }
            case "sfx":
            {
                var name = (string?)o["name"];
                var scale = (float?)o["scale"] ?? 0.45f;
                if (!string.IsNullOrEmpty(name)) ChaosSfx.Play(name, scale);
                break;
            }
        }
    }

    /// <summary>Post the saved-spiral library (page-ready + after save/delete).</summary>
    private static void PostLoomList()
    {
        try
        {
            _host?.Post(new
            {
                type = "loom-list",
                spirals = DtrhLoomStore.List().Select(s => new
                {
                    slug = s.Slug,
                    url = $"https://ccp.spirals/loom_{s.Slug}.gif",
                    @params = TryParseJson(s.ParamsJson),
                }),
            });
        }
        catch (Exception ex) { App.Logger?.Debug("LoomHost.PostLoomList: {E}", ex.Message); }
    }

    private static JObject? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JObject.Parse(json); }
        catch { return null; }
    }
}
