using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// The Takeaway shelf's drawer (WPF Services/JustDrop/JustDropOrdersService.cs): the server's
    /// list of delivered Just Drop orders, read live over the device-token door and kept nowhere
    /// locally. Signed out is not an error, it is a user with no drawer. Cached for 90 seconds.
    /// </summary>
    internal static class JustDropOrdersService
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(90);

        private static readonly SemaphoreSlim _gate = new(1, 1);
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

        private static IReadOnlyList<Order> _cached = Array.Empty<Order>();
        private static DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

        /// <summary>Test seam: stands in for the network read. Returns the response body, or null for a refusal.</summary>
        internal static Func<string, string, CancellationToken, Task<string?>>? FetchOverride;

        /// <summary>One delivered order.</summary>
        internal sealed class Order
        {
            public string Id { get; init; } = "";
            public string Code { get; init; } = "";
            public string Name { get; init; } = "";
            public string SizeId { get; init; } = "";
            public int Items { get; init; }
            public int Pm { get; init; }
            public DateTimeOffset At { get; init; }
            public bool PaidOut { get; init; }

            /// <summary>The size's length in minutes, 0 for a size nothing knows.</summary>
            public int Minutes => SizeId switch
            {
                "S" => 5,
                "M" => 15,
                "L" => 30,
                "XXL" => 60,
                _ => 0,
            };
        }

        /// <summary>Raised when the drawer may have changed (a shop or replay window closed).</summary>
        public static event Action? DrawerChanged;

        /// <summary>Forget the cached answer.</summary>
        public static void Invalidate() => _cachedAt = DateTimeOffset.MinValue;

        /// <summary>WPF JustDropHostService.cs:309: invalidate BEFORE the shelf refreshes, then tell it.</summary>
        public static void NoteDrawerChanged()
        {
            Invalidate();
            try { DrawerChanged?.Invoke(); }
            catch (Exception ex) { Log.Debug("JustDrop orders: shelf refresh failed: {E}", ex.Message); }
        }

        public static async Task<IReadOnlyList<Order>> FetchAsync(CancellationToken ct = default)
        {
            if (_cachedAt > DateTimeOffset.UtcNow - CacheFor) return _cached;

            var token = SafeAuthToken();
            var unifiedId = SafeUnifiedId();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(unifiedId))
                return Array.Empty<Order>();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Re-check inside the gate: a caller may have refreshed while we queued.
                if (_cachedAt > DateTimeOffset.UtcNow - CacheFor) return _cached;

                string? body;
                if (FetchOverride != null) body = await FetchOverride(unifiedId, token, ct).ConfigureAwait(false);
                else
                {
                    var url = $"{ProxyBaseUrl}/v2/justdrop/orders?unified_id={Uri.EscapeDataString(unifiedId)}";
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    // The device door, NOT Authorization: Bearer: a bearer would be read as a JWT
                    // and rejected rather than fall through to this door.
                    request.Headers.Add("X-Auth-Token", token);
                    request.Headers.Add("Accept", "application/json");

                    using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        await MergedAccountRecovery.TryHandleAsync(response).ConfigureAwait(false);   // contract D
                        Log.Debug("JustDrop orders: {Status}; shelf stays empty", (int)response.StatusCode);
                        return Array.Empty<Order>();
                    }
                    body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                }
                if (body == null) return Array.Empty<Order>();

                var parsed = Parse(body);
                _cached = parsed;
                _cachedAt = DateTimeOffset.UtcNow;
                Log.Information("JustDrop orders: {Count} in the drawer", parsed.Count);
                return parsed;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug("JustDrop orders: fetch failed ({Error})", ex.Message);
                return Array.Empty<Order>();
            }
            finally { _gate.Release(); }
        }

        internal static IReadOnlyList<Order> Parse(string body)
        {
            var list = new List<Order>();
            try
            {
                var rows = JObject.Parse(body)["orders"] as JArray;
                if (rows == null) return list;

                foreach (var row in rows)
                {
                    if (row is not JObject o) continue;
                    var code = (string?)o["code"];
                    if (string.IsNullOrWhiteSpace(code)) continue;   // a receipt with no code cannot be replayed

                    var atMs = o["at"]?.Value<long?>() ?? 0;
                    list.Add(new Order
                    {
                        Id = (string?)o["id"] ?? "",
                        Code = code!,
                        Name = (string?)o["name"] ?? "",
                        SizeId = (string?)o["sizeId"] ?? "",
                        Items = o["items"]?.Value<int?>() ?? 0,
                        Pm = o["pm"]?.Value<int?>() ?? 0,
                        At = atMs > 0
                            ? DateTimeOffset.FromUnixTimeMilliseconds(atMs).ToLocalTime()
                            : DateTimeOffset.Now,
                        PaidOut = o["paidOut"]?.Value<bool?>() == true,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug("JustDrop orders: unparseable body ({Error})", ex.Message);
                return Array.Empty<Order>();
            }
            return list;
        }

        private static string SafeAuthToken()
        {
            try { return CoreSettings.Current.AuthToken ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeUnifiedId()
        {
            try { return CoreAccount.UnifiedUserId ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
