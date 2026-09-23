using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Chaster;

public enum LinkOutcome { Linked, Denied, TimedOut, Cancelled, Failed }

/// <summary>
/// The link flow: open the browser at the proxy's /chaster/authorize with a PKCE challenge, wait
/// on a loopback listener for the one-time code, check the state, then trade the code and the
/// verifier for tokens through the proxy (which adds the client secret). The proxy never keeps a
/// token: a stash keyed by a state the STARTER chose let anyone who sent a stranger a consent
/// link collect that stranger's lock (fixed 2026-09-23). The secret never comes near this machine.
/// </summary>
public sealed partial class ChasterService
{
    /// <summary>Patreon 47832, Discord 47833, SubscribeStar 47834. Must match the proxy's bridge.</summary>
    public const int LoopbackPort = 47835;
    private static readonly TimeSpan LinkTimeout = TimeSpan.FromMinutes(5);

    private CancellationTokenSource? _linkCts;
    private volatile bool _linkCancelled;

    public bool IsLinking => _linkCts != null;

    public void CancelLink()
    {
        _linkCancelled = true;
        try { _linkCts?.Cancel(); }
        catch (ObjectDisposedException) { } // swallow: the flow already finished and cleaned up
    }

    /// <param name="openBrowser">Opens the consent page. The app passes BrowserLauncher, which
    /// falls back to a copy-the-link prompt on machines with no default browser.</param>
    public async Task<LinkOutcome> LinkAsync(Action<string> openBrowser)
    {
        var cts = new CancellationTokenSource(LinkTimeout);
        // One flow at a time, decided atomically: two listeners cannot share the port anyway.
        if (Interlocked.CompareExchange(ref _linkCts, cts, null) != null)
        {
            cts.Dispose();
            return LinkOutcome.Failed;
        }
        _linkCancelled = false;
        var state = ChasterClient.NewState();
        var verifier = ChasterClient.NewVerifier();
        using var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add($"http://localhost:{LoopbackPort}/callback/");
            listener.Start();
            openBrowser(ChasterClient.AuthorizeUrl(state, ChasterClient.Challenge(verifier)));

            string? error, code;
            while (true)
            {
                var contextTask = listener.GetContextAsync();
                // Observe a fault from a listener torn down under the wait, so it never surfaces
                // as an unobserved task exception on the finalizer thread.
                _ = contextTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                var done = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
                if (done != contextTask) return _linkCancelled ? LinkOutcome.Cancelled : LinkOutcome.TimedOut;

                var context = await contextTask.ConfigureAwait(false);
                var query = context.Request.QueryString;
                error = query["error"];
                code = query["code"];
                var stateOk = SecurityHelper.SecureCompare(state, query["state"] ?? "");
                await RespondAsync(context, stateOk && string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(code)).ConfigureAwait(false);
                // A knock with the wrong state is a stale tab from an earlier try, or some other
                // program on this machine. It gets the "not linked" page and the wait goes on;
                // it must not be able to end a real attempt.
                if (stateOk) break;
            }

            if (!string.IsNullOrEmpty(error)) return error == "denied" ? LinkOutcome.Denied : LinkOutcome.Failed;
            if (string.IsNullOrEmpty(code)) return LinkOutcome.Failed;

            var tokens = await _client.ExchangeAsync(code, verifier, cts.Token).ConfigureAwait(false);
            if (!tokens.Ok || string.IsNullOrEmpty(tokens.Value!.RefreshToken)) return LinkOutcome.Failed;

            StoreTokens(tokens.Value, null);
            // Being away only counts from the day the account was linked.
            lock (_gate) { _tab.LastSeenDay = CircesTab.DayKey(_localNow()); SaveTab(); }
            App.Logger?.Information("[Chaster] linked (offline token: {Offline})", tokens.Value.RefreshExpiresIn == 0);
            ForgetProfile();
            LinkChanged?.Invoke();
            _ = EnsureProfileAsync();
            return LinkOutcome.Linked;
        }
        catch (OperationCanceledException)
        {
            return LinkOutcome.Cancelled;
        }
        catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
        {
            // Port taken, or the listener was refused. Nothing to clean up but the flag.
            Diag.Swallowed(ex, "chaster link listener failed");
            return LinkOutcome.Failed;
        }
        finally
        {
            Interlocked.Exchange(ref _linkCts, null);
            cts.Dispose();
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, bool ok)
    {
        var title = ok ? "Linked" : "Not linked";
        var line = ok ? "You can close this tab and go back to the app." : "Nothing was linked. Close this tab and try again from the app.";
        var html = "<!DOCTYPE html><html><head><meta charset='utf-8'><title>" + title + "</title><style>"
            + "body{font-family:'Segoe UI',sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:#1a1a2e;color:#fff;text-align:center}"
            + "h1{color:" + (ok ? "#ff69b4" : "#ff4444") + "}p{color:#888}</style></head><body><div><h1>" + title + "</h1><p>" + line + "</p></div></body></html>";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or System.IO.IOException)
        {
            Diag.Swallowed(ex, "browser closed before the link page was written");
        }
    }
}
