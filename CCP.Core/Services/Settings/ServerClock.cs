using System;
using System.Net.Http;
using System.Threading;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The proxy's idea of "now", learned from the HTTP <c>Date</c> header of its own answers.
    ///
    /// <para>Why: <c>/v2/user/sync</c> is HMAC-signed with a unix timestamp and the server refuses
    /// anything outside a 300 s window. A PC whose clock is hours off (a wrong time zone set as
    /// "local = UTC", a dead CMOS battery) signed every sync with its own clock and earned a 403 on
    /// every one of them, for ever: Sep 2026 telemetry showed one desktop at ts_age 18002 s, five
    /// hours out. Signing with <see cref="UtcNow"/> puts that PC back inside the window after its
    /// first answer from the proxy, whatever that answer was (the 403 carries a Date too).</para>
    ///
    /// <para>Guard rails: an offset past <see cref="MaxTrustedOffset"/> is ignored (a Date header
    /// that far off is a broken middlebox, not a clock), and an offset inside
    /// <see cref="Deadband"/> reads as zero (the header has one-second resolution and the round
    /// trip adds a little, so a healthy PC keeps signing with its own clock exactly as before).</para>
    /// </summary>
    public static class ServerClock
    {
        /// <summary>Offsets smaller than this are noise and read as zero.</summary>
        public static readonly TimeSpan Deadband = TimeSpan.FromSeconds(5);

        /// <summary>Anything bigger than this is not a clock we are willing to believe.</summary>
        public static readonly TimeSpan MaxTrustedOffset = TimeSpan.FromDays(2);

        private static long _offsetTicks;

        /// <summary>Server time minus local time, as last learned. Zero until something is learned.</summary>
        public static TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

        /// <summary>Local UTC now, corrected by the learned offset.</summary>
        public static DateTimeOffset UtcNow => DateTimeOffset.UtcNow + Offset;

        /// <summary>
        /// Pure: the offset a Date header implies, or null when it should not be adopted.
        /// Returns <see cref="TimeSpan.Zero"/> inside the dead band.
        /// </summary>
        public static TimeSpan? ComputeOffset(DateTimeOffset? serverDate, DateTimeOffset localNow)
        {
            if (serverDate == null) return null;
            var offset = serverDate.Value - localNow;
            if (offset.Duration() > MaxTrustedOffset) return null;
            if (offset.Duration() < Deadband) return TimeSpan.Zero;
            return offset;
        }

        /// <summary>
        /// Learn from any response of our own proxy. Safe to call with null and from any thread.
        /// Returns true when the stored offset moved by more than the dead band.
        /// </summary>
        public static bool Observe(HttpResponseMessage? response)
        {
            try
            {
                return Observe(response?.Headers?.Date, DateTimeOffset.UtcNow);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Testable core of <see cref="Observe(HttpResponseMessage?)"/>.</summary>
        public static bool Observe(DateTimeOffset? serverDate, DateTimeOffset localNow)
        {
            var next = ComputeOffset(serverDate, localNow);
            if (next == null) return false;
            var previous = Offset;
            Interlocked.Exchange(ref _offsetTicks, next.Value.Ticks);
            var moved = (next.Value - previous).Duration() > Deadband;
            if (moved)
            {
                Serilog.Log.Information("[Clock] Server clock offset now {Seconds:F0}s (was {Was:F0}s)",
                    next.Value.TotalSeconds, previous.TotalSeconds);
            }
            return moved;
        }

        /// <summary>
        /// Read a signed-route 403 body: <c>{reason:'clock_skew'|'bad_signature', server_time: ms}</c>
        /// (CCP-Server #233). Returns the reason (null when absent) and the server time when present.
        /// Tolerant of any other body, including the older plain-text refusal.
        /// </summary>
        public static string? ParseRefusal(string? body, out DateTimeOffset? serverTime)
        {
            serverTime = null;
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(body);
                var ms = obj["server_time"];
                if (ms != null && (ms.Type == Newtonsoft.Json.Linq.JTokenType.Integer || ms.Type == Newtonsoft.Json.Linq.JTokenType.Float))
                {
                    var value = (long)ms.ToObject<double>();
                    if (value > 0) serverTime = DateTimeOffset.FromUnixTimeMilliseconds(value);
                }
                return obj["reason"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Test seam: forget what was learned.</summary>
        internal static void ResetForTests() => Interlocked.Exchange(ref _offsetTicks, 0);
    }

    /// <summary>
    /// Feeds <see cref="ServerClock"/> from every answer an <see cref="HttpClient"/> receives. Wraps
    /// a plain <see cref="HttpClientHandler"/>, so the client behaves exactly as a bare one did.
    /// </summary>
    public sealed class ServerClockHandler : DelegatingHandler
    {
        public ServerClockHandler() : base(new HttpClientHandler()) { }

        protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            ServerClock.Observe(response);
            return response;
        }
    }
}
