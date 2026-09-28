using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The OAuth loopback callback shared by Patreon (47832), Discord (47833) and SubscribeStar (47834):
    /// binds <c>http://localhost:{port}/callback/</c> (the exact string the proxy and the provider apps
    /// know), waits for one callback, serves the browser page, checks the CSRF state, and trades the
    /// code at the proxy. Opening the browser stays with the head. One flow per instance; dispose to stop.
    /// </summary>
    public sealed class LoopbackOAuth : IDisposable
    {
        private readonly int _port;
        private readonly HttpListener? _http;
        private readonly List<TcpListener> _tcp = new();

        public string CallbackUrl { get; }

        /// <summary>CSRF state for this flow (hex, URL-safe).</summary>
        public string State { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        public LoopbackOAuth(int port) : this(port, OperatingSystem.IsWindows()) { }

        /// <summary>
        /// Windows keeps http.sys's HttpListener, which serves "localhost" on both loopbacks. The managed
        /// HttpListener used everywhere else binds only the first address "localhost" resolves to (::1 on
        /// Linux, verified), so a browser that picks 127.0.0.1 is refused: there both loopbacks are
        /// served by plain sockets instead. Binds now, so a busy port fails before the browser opens.
        /// </summary>
        internal LoopbackOAuth(int port, bool useHttpListener)
        {
            _port = port;
            CallbackUrl = $"http://localhost:{port}/callback/";
            try
            {
                if (useHttpListener)
                {
                    _http = new HttpListener();
                    _http.Prefixes.Add(CallbackUrl);
                    _http.Start();
                    return;
                }
                Bind(IPAddress.Loopback);
                try { Bind(IPAddress.IPv6Loopback); }
                catch (SocketException e) when (e.SocketErrorCode != SocketError.AddressAlreadyInUse) { } // no IPv6 on this host
            }
            catch (Exception ex) when (ex is HttpListenerException or SocketException)
            {
                Dispose();
                throw new IOException(
                    $"Could not listen on {CallbackUrl} for the sign-in callback. Is another program using port {port}? ({ex.Message})", ex);
            }
        }

        private void Bind(IPAddress address)
        {
            var listener = new TcpListener(address, _port);
            listener.Start();
            _tcp.Add(listener);
        }

        public void Dispose()
        {
            try { _http?.Stop(); _http?.Close(); } catch { }
            foreach (var l in _tcp) try { l.Stop(); } catch { }
        }

        /// <summary>
        /// Waits for the callback, answers the browser (<paramref name="successHtml"/>, or
        /// <paramref name="failureHtml"/> when the query carries <c>error</c>), then checks the state. Returns the callback query; the caller reads
        /// <c>code</c> / <c>error</c>. Cancelling <paramref name="ct"/> ends the wait as a timeout, as WPF did.
        /// </summary>
        public async Task<NameValueCollection> WaitAsync(TimeSpan timeout, string timeoutMessage, string successHtml, string failureHtml, CancellationToken ct)
        {
            var getContextTask = NextCallbackAsync();
            // Observe any future fault: disposing the listener while the wait is pending faults it.
            _ = getContextTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            var timeoutTask = Task.Delay(timeout, ct);

            if (await Task.WhenAny(getContextTask, timeoutTask) == timeoutTask)
                throw new TimeoutException(timeoutMessage);

            var (query, respond) = await getContextTask;
            await respond(string.IsNullOrEmpty(query["error"]) ? successHtml : failureHtml);

            if (!SecureCompare(State, query["state"] ?? ""))
                throw new SecurityException("OAuth state mismatch - possible CSRF attack");

            return query;
        }

        private async Task<(NameValueCollection, Func<string, Task>)> NextCallbackAsync()
        {
            if (_http != null)
            {
                var context = await _http.GetContextAsync();
                return (context.Request.QueryString, async html =>
                {
                    var response = context.Response;
                    var buffer = Encoding.UTF8.GetBytes(html);
                    response.ContentType = "text/html";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer);
                    response.Close();
                });
            }

            var callback = new TaskCompletionSource<(NameValueCollection, Func<string, Task>)>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var listener in _tcp)
                _ = AcceptLoopAsync(listener, callback);
            return await callback.Task;
        }

        private async Task AcceptLoopAsync(TcpListener listener, TaskCompletionSource<(NameValueCollection, Func<string, Task>)> callback)
        {
            try
            {
                while (!callback.Task.IsCompleted)
                    _ = HandleAsync(await listener.AcceptTcpClientAsync(), callback); // concurrent: a silent pre-connect must not block
            }
            catch (Exception ex) { callback.TrySetException(ex); } // listener stopped
        }

        /// <summary>
        /// Minimal HTTP/1.1 GET, matching what HttpListener did for this prefix: wrong Host is 400,
        /// a path outside /callback/ is 404, and neither ends the wait.
        /// </summary>
        private async Task HandleAsync(TcpClient client, TaskCompletionSource<(NameValueCollection, Func<string, Task>)> callback)
        {
            try
            {
                var stream = client.GetStream();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var target = (await reader.ReadLineAsync(cts.Token))?.Split(' ') is [_, var t, ..] ? t : "";
                string? host = null;
                int lines = 0, chars = target.Length;
                for (string? line; !string.IsNullOrEmpty(line = await reader.ReadLineAsync(cts.Token)) && ++lines <= 100 && (chars += line.Length) <= 8192;)
                    if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) host = line[5..].Trim();

                var path = target.Split('?')[0];
                string? status = null;
                if (lines > 100 || chars > 8192) status = "400 Bad Request"; // header cap
                else if (!string.Equals(host, $"localhost:{_port}", StringComparison.OrdinalIgnoreCase)) status = "400 Bad Request";
                else if (!path.Equals("/callback", StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith("/callback/", StringComparison.OrdinalIgnoreCase)) status = "404 Not Found";
                if (status != null)
                {
                    try { await WriteAsync(stream, status, ""); } finally { client.Dispose(); }
                    return;
                }

                var query = HttpUtility.ParseQueryString(new Uri("http://localhost" + target).Query);
                if (!callback.TrySetResult((query, async html => { try { await WriteAsync(stream, "200 OK", html); } finally { client.Dispose(); } })))
                    client.Dispose();
            }
            catch (Exception) { client.Dispose(); } // a broken connection is not the callback; keep waiting
        }

        private static async Task WriteAsync(Stream stream, string status, string html)
        {
            var body = Encoding.UTF8.GetBytes(html);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: text/html\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(body);
        }

        /// <summary>What the browser flow hands back for the code exchange.</summary>
        public sealed record Callback(string Code, string CallbackUrl, string State, string? Verifier);

        /// <summary>WPF's fixed ports: Patreon 47832, Discord 47833, SubscribeStar 47834.</summary>
        public static int Port(string provider) => provider switch { "discord" => 47833, "substar" => 47834, _ => 47832 };

        /// <summary>The proxy authorize URL exactly as WPF built it. SubscribeStar carries no redirect_uri
        /// (the proxy uses its registered https one) but a PKCE S256 challenge.</summary>
        public static string AuthorizeUrl(string provider, string callbackUrl, string state, string? challenge) => provider == "substar"
            ? $"{ProviderSubscription.ProxyBaseUrl}/substar/authorize?state={state}&code_challenge={challenge}&code_challenge_method=S256"
            : $"{ProviderSubscription.ProxyBaseUrl}/{provider}/authorize?redirect_uri={Uri.EscapeDataString(callbackUrl)}&state={state}";

        /// <summary>PKCE (RFC 7636): 32 random bytes, base64url (was ChasterClient.NewVerifier).</summary>
        public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

        /// <summary>The S256 challenge the consent page is opened with.</summary>
        public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>
        /// The whole browser half of a provider sign-in, as WPF's Start*Login ran it: bind, open the
        /// authorize URL (<paramref name="openBrowser"/> is the head's), wait 5 minutes, check state and
        /// <c>error</c>/<c>code</c>. Throws WPF's messages; the caller trades the code.
        /// </summary>
        public static Task<Callback> SignInAsync(string provider, Action<string> openBrowser, CancellationToken ct) =>
            SignInAsync(provider, openBrowser, ct, Port(provider), OperatingSystem.IsWindows(), TimeSpan.FromMinutes(5));

        internal static async Task<Callback> SignInAsync(string provider, Action<string> openBrowser, CancellationToken ct,
            int port, bool useHttpListener, TimeSpan timeout)
        {
            using var listener = new LoopbackOAuth(port, useHttpListener);
            var verifier = provider == "substar" ? NewVerifier() : null;
            Serilog.Log.Information("Started {Provider} OAuth callback listener on {Url}", provider, listener.CallbackUrl);
            openBrowser(AuthorizeUrl(provider, listener.CallbackUrl, listener.State, verifier == null ? null : Challenge(verifier)));

            var discord = provider == "discord";
            var query = await listener.WaitAsync(timeout,
                discord ? "Discord login timed out. Please try again." : "OAuth login timed out. Please try again.",
                discord ? DiscordSuccessHtml : SuccessHtml, discord ? DiscordFailureHtml : FailureHtml, ct);
            var code = query["code"];
            var error = query["error"];

            if (!string.IsNullOrEmpty(error))
                throw new Exception(provider switch
                {
                    "substar" => $"SubscribeStar authorization failed: {error}",
                    "discord" => $"Discord authorization failed: {query["error_description"] ?? "Unknown error"}",
                    _ => $"Patreon authorization failed: {query["error_description"] ?? "Unknown error"}",
                });
            if (string.IsNullOrEmpty(code))
                throw new Exception(provider == "substar" ? "SubscribeStar sign-in returned no code. Please try again." : "No authorization code received");

            return new Callback(code, listener.CallbackUrl, listener.State, verifier);
        }

        /// <summary>Constant-time compare (was SecurityHelper.SecureCompare in the WPF head).</summary>
        private static bool SecureCompare(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

        /// <summary>
        /// POSTs <paramref name="body"/> to the proxy's token endpoint (<c>/patreon/token</c>,
        /// <c>/discord/token</c>, <c>/substar/token</c>). Throws on a non-success status or an error body.
        /// </summary>
        public static async Task<PatreonTokenResponse> ExchangeAsync(HttpClient http, string path, object body)
        {
            var response = await http.PostAsJsonAsync(path, body);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                throw new Exception($"Token exchange failed: {response.StatusCode} - {errorText}");
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<PatreonTokenResponse>();

            if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
                throw new Exception($"Token exchange failed: {tokenResponse?.ErrorDescription ?? "Unknown error"}");

            return tokenResponse;
        }

        // Browser pages, byte-for-byte as WPF served them. Patreon and SubscribeStar share the plain pair.

        public const string SuccessHtml =
            @"<!DOCTYPE html>
<html>
<head>
    <title>Login Successful</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               display: flex; justify-content: center; align-items: center;
               height: 100vh; margin: 0; background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%); }
        .container { text-align: center; color: white; }
        h1 { color: #ff69b4; }
        p { color: #888; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Login Successful!</h1>
        <p>You can close this window and return to the application.</p>
    </div>
</body>
</html>";

        public const string FailureHtml =
            @"<!DOCTYPE html>
<html>
<head>
    <title>Login Failed</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               display: flex; justify-content: center; align-items: center;
               height: 100vh; margin: 0; background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%); }
        .container { text-align: center; color: white; }
        h1 { color: #ff4444; }
        p { color: #888; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Login Failed</h1>
        <p>Please try again from the application.</p>
    </div>
</body>
</html>";

        public const string DiscordSuccessHtml =
            @"<!DOCTYPE html>
<html>
<head>
    <title>Discord Login Successful</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               display: flex; justify-content: center; align-items: center;
               height: 100vh; margin: 0; background: linear-gradient(135deg, #5865F2 0%, #1a1a2e 100%); }
        .container { text-align: center; color: white; }
        h1 { color: #5865F2; background: white; padding: 10px 20px; border-radius: 8px; }
        p { color: #ccc; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Discord Connected!</h1>
        <p>You can close this window and return to the application.</p>
    </div>
</body>
</html>";

        public const string DiscordFailureHtml =
            @"<!DOCTYPE html>
<html>
<head>
    <title>Discord Login Failed</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               display: flex; justify-content: center; align-items: center;
               height: 100vh; margin: 0; background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%); }
        .container { text-align: center; color: white; }
        h1 { color: #ff4444; }
        p { color: #888; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Login Failed</h1>
        <p>Please try again from the application.</p>
    </div>
</body>
</html>";
    }
}
