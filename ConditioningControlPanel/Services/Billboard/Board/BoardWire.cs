using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>
    /// Reads the <c>board</c> block off <c>GET /config/marquee</c> (Tonight Board, 2026-10-07).
    /// Pure: no I/O, no WPF. The server already validates every post; this side re-checks the
    /// parts that decide what the app DOES (the button target, the audience, the size), so a bad
    /// value in Redis can never become a wrong click or a stranger's picture.
    /// </summary>
    public static class BoardWire
    {
        public const int MaxFrameWidth = 64;
        public const int MaxHeight = 36;
        public const int MaxFrames = 8;
        public const int MaxFps = 12;

        /// <summary>
        /// The post in a marquee response, or null when there is none (no block, a null block,
        /// a cleared or expired post) or the block cannot be trusted (no version, an unknown
        /// audience, an unreadable expiry). Null means "show no board".
        /// </summary>
        public static BoardPost? Parse(string? marqueeJson)
        {
            if (string.IsNullOrWhiteSpace(marqueeJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(marqueeJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (!doc.RootElement.TryGetProperty("board", out var b)) return null;
                return ParseBlock(b);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The block itself. Exposed for tests.</summary>
        public static BoardPost? ParseBlock(JsonElement b)
        {
            if (b.ValueKind != JsonValueKind.Object) return null;

            if (!TryInt(b, "v", out var version) || version <= 0) return null;

            var fx = new List<string>();
            if (b.TryGetProperty("fx", out var fxEl) && fxEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in fxEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var name = (item.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (BoardFx.All.Contains(name) && !fx.Contains(name)) fx.Add(name);
                }
            }

            DateTime? until = null;
            if (b.TryGetProperty("until", out var untilEl) && untilEl.ValueKind != JsonValueKind.Null)
            {
                if (untilEl.ValueKind != JsonValueKind.String) return null;
                if (!DateTime.TryParse(untilEl.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var u)) return null;
                until = DateTime.SpecifyKind(u, DateTimeKind.Utc);
            }

            string? link = null;
            if (b.TryGetProperty("link", out var linkEl) && linkEl.ValueKind == JsonValueKind.String)
                link = BoardLinks.Normalise(linkEl.GetString());

            var audience = BoardAudience.Everyone;
            if (b.TryGetProperty("aud", out var audEl) && audEl.ValueKind != JsonValueKind.Null)
            {
                if (audEl.ValueKind != JsonValueKind.String) return null;
                var parsed = ParseAudience(audEl.GetString());
                if (parsed == null) return null; // a name this build does not know: hide, never widen
                audience = parsed.Value;
            }

            int frames = TryInt(b, "frames", out var f) ? Math.Clamp(f, 1, MaxFrames) : 1;
            int fps = TryInt(b, "fps", out var r) ? Math.Clamp(r, 1, MaxFps) : 6;
            int w = TryInt(b, "w", out var ww) ? Math.Clamp(ww, 1, MaxFrameWidth) : MaxFrameWidth;
            int h = TryInt(b, "h", out var hh) ? Math.Clamp(hh, 1, MaxHeight) : MaxHeight;

            return new BoardPost(version, fx, until, link, audience, frames, fps, w, h);
        }

        public static BoardAudience? ParseAudience(string? value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "" or "everyone" => BoardAudience.Everyone,
                "free" => BoardAudience.Free,
                "patrons" => BoardAudience.Patrons,
                _ => null,
            };

        private static bool TryInt(JsonElement o, string name, out int value)
        {
            value = 0;
            if (!o.TryGetProperty(name, out var el)) return false;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d) && !double.IsNaN(d))
            {
                value = (int)Math.Clamp(Math.Floor(d), int.MinValue, int.MaxValue);
                return true;
            }
            if (el.ValueKind == JsonValueKind.String &&
                int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;
            return false;
        }
    }

    /// <summary>Who sees a post, and for how long. Pure.</summary>
    public static class BoardRules
    {
        public static bool Sees(BoardAudience audience, BillboardTier tier) => audience switch
        {
            BoardAudience.Everyone => true,
            BoardAudience.Free => tier == BillboardTier.Free,
            BoardAudience.Patrons => tier != BillboardTier.Free,
            _ => false,
        };

        /// <summary>A post with no expiry lives until it is cleared; one past its time is gone.</summary>
        public static bool IsLive(BoardPost post, DateTime nowUtc) =>
            post.UntilUtc == null || nowUtc < post.UntilUtc.Value;
    }

    /// <summary>
    /// The board's one button. Only these targets exist: "lobby", "premium", "discord",
    /// "tab:&lt;key&gt;", or an https address on cclabs.app. Anything else is no button.
    /// </summary>
    public static class BoardLinks
    {
        public const string LobbyTab = "availablesubjects";
        public const string PremiumTab = "premium";

        private static readonly Regex TabKey = new("^[a-z0-9_-]{1,40}$", RegexOptions.CultureInvariant);

        /// <summary>The link as the app will use it, or null when it is not on the allowlist.</summary>
        public static string? Normalise(string? link)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;
            var s = link.Trim();
            if (s.Any(c => char.IsControl(c) || c == '"' || c == '\'' || c == '<' || c == '>' || c == '`' || c == ' ')) return null;

            var lower = s.ToLowerInvariant();
            if (lower is "lobby" or "premium" or "discord") return lower;

            if (lower.StartsWith("tab:", StringComparison.Ordinal))
            {
                var key = lower.Substring(4);
                return TabKey.IsMatch(key) ? "tab:" + key : null;
            }

            if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps) return null;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
            if (!uri.IsDefaultPort) return null;
            var host = uri.Host.ToLowerInvariant();
            if (host != "cclabs.app" && host != "www.cclabs.app") return null;
            return uri.AbsoluteUri;
        }

        /// <summary>The button for a normalised link. Labels come from <paramref name="label"/> (a loc lookup).</summary>
        public static BillboardAction ToAction(string? link, Func<string, string> label)
        {
            var n = Normalise(link);
            if (n == null) return BillboardAction.None;
            return n switch
            {
                "lobby" => new BillboardAction(BillboardActionKind.Tab, LobbyTab, label("board_btn_lobby")),
                "premium" => new BillboardAction(BillboardActionKind.Tab, PremiumTab, label("board_btn_premium")),
                "discord" => new BillboardAction(BillboardActionKind.Link, DiscordLinks.Invite, label("board_btn_discord")),
                _ when n.StartsWith("tab:", StringComparison.Ordinal) =>
                    new BillboardAction(BillboardActionKind.Tab, n.Substring(4), label("board_btn_open")),
                _ => new BillboardAction(BillboardActionKind.Link, n, label("board_btn_open")),
            };
        }
    }
}
