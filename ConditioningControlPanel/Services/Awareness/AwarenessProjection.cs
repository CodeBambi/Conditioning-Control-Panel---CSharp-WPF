using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// One codepath, two projections (doc 02 §6.2). A <see cref="ContextFrame"/> is a local object and
    /// may legitimately hold things the cloud must never see; what leaves the machine is whatever this
    /// class writes, and nothing else.
    ///
    /// <para><b>Cloud projection — the exhaustive list of what crosses the wire:</b> the schema
    /// version; cluster id; category; our own coarse app id and display name; bucketed history numbers
    /// (visits today, minutes today and this week rounded to five, gap since the last visit rounded to
    /// five, day streak, switch count); dwell and input-idle as BANDS rather than seconds; the
    /// fullscreen flag; the transition kind; trend LABELS (kind + magnitude); the day-arc summary,
    /// which is built from app ids only; time bucket and weekday; in-app CCP state (session running,
    /// level, login streak, recent achievement id); a media block carrying only a whitelisted playback
    /// state and a repeat COUNT; habit pattern labels; the rarity tier; her own recent lines; and —
    /// only when the user allow-listed that app for titles — the sanitised, scrubbed page title.</para>
    ///
    /// <para><b>Never, on any cloud path:</b> raw window titles, OCR text, keystrokes, URLs, process
    /// paths, screenshots, SMTC track or artist names, or the id of the app they came FROM. That last
    /// one is deliberate: the frame carries <see cref="ContextFrame.PreviousAppId"/> but not the
    /// previous app's CLUSTER, so there is no way to tell here whether the app they just left was an
    /// adult one. Sending it would leak past the adult rule below through the back door, so it is a
    /// local-only field (addendum D: when the privacy layer cannot answer, drop it).</para>
    ///
    /// <para><b>And for the <c>site_eh</c> cluster</b> the app id, the display name, the title, the day
    /// arc and the habit labels are all withheld as well — only the cluster id goes, regardless of
    /// allow lists (doc 02 §6.1, and the conservative reading of open question 2: cluster-level jokes
    /// on by default, per-site specificity opt-in).</para>
    ///
    /// <para><b>The local projection</b> is the fuller frame for the machine-local Ollama path: the
    /// page title, the now-playing track and artist, the previous app and the raw second counts are
    /// included and the adult cluster is not collapsed. Nothing this method writes is permitted to
    /// reach a remote endpoint.</para>
    ///
    /// <para><b>This is also the "what she can see" panel's source of truth.</b> Showing the real wire
    /// format is the point (doc 02 §6.4), so this method must never grow a prettier "for display"
    /// variant that differs from what is actually sent.</para>
    /// </summary>
    public static class AwarenessProjection
    {
        /// <summary>Projection schema version. Bumped when fields are added; the server tolerates unknown fields.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Recent lines included in the ban list section of a projection.</summary>
        public const int MaxRecentReactions = 10;

        /// <summary>
        /// How many of the recent lines are sent at (near) full length. Doc 02 §3.1 item 4 asks for
        /// "summaries + the full text of the last 3": the newest few are what the model is most likely
        /// to accidentally rewrite, and the older ones only need to be recognisable.
        /// </summary>
        public const int FullTextRecentReactions = 3;

        /// <summary>Character cap on the newest recent lines.</summary>
        public const int RecentFullLength = 160;

        /// <summary>Character cap on the older recent lines — enough to spot a repeated opening.</summary>
        public const int RecentSummaryLength = 64;

        /// <summary>Character cap on an allow-listed page title after scrubbing.</summary>
        public const int TitleLength = 120;

        // Emails and long digit runs are the two things most likely to be sitting in a window title
        // that the user allow-listed for a good reason ("Inbox — 4 unread") and would regret sending
        // in full ("Order 100293847562 — receipt for card ending 4471"). Order numbers, card
        // fragments, phone numbers and account ids are all digit runs; six is short enough to catch
        // them and long enough to leave "Bambi TikTok 4" and a 4-digit year alone.
        private static readonly Regex EmailPattern = new(
            @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

        private static readonly Regex LongDigitRun = new(@"\d{6,}", RegexOptions.Compiled);

        /// <summary>Playback states SMTC actually reports. Anything else reads as "unknown".</summary>
        private static readonly string[] KnownPlaybackStates =
        {
            "closed", "opened", "changing", "stopped", "playing", "paused"
        };

        /// <summary>
        /// The sanitised, bucketed JSON that may be sent to a cloud provider.
        /// </summary>
        public static string BuildCloudProjection(ContextFrame? frame) => Build(frame, cloud: true);

        /// <summary>
        /// The fuller frame for the local Ollama path, which is machine-local by definition.
        /// </summary>
        public static string BuildLocalProjection(ContextFrame? frame) => Build(frame, cloud: false);

        private static string Build(ContextFrame? frame, bool cloud)
        {
            if (frame == null) return "{}";

            bool adult = cloud && frame.IsAdultCluster;

            using var stream = new MemoryStream(768);
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();
                w.WriteNumber("v", SchemaVersion);

                w.WriteString("cluster", AwarenessText.SanitizeId(frame.AppCluster ?? "unclustered"));
                w.WriteString("category", frame.Category.ToString());

                if (!adult)
                {
                    w.WriteString("app_id", AwarenessText.SanitizeId(frame.AppId));
                    var display = AwarenessText.SanitizeDisplayName(frame.ServiceName);
                    if (display.Length > 0) w.WriteString("app", display);
                }

                // Populated upstream ONLY for user-allow-listed apps; the shipped allow list is empty.
                if (!adult && !string.IsNullOrWhiteSpace(frame.PageTitleSanitized))
                {
                    var scrubbed = ScrubTitle(frame.PageTitleSanitized);
                    if (scrubbed.Length > 0) w.WriteString("title", scrubbed);
                }

                w.WriteString("transition", frame.Transition.ToString());
                w.WriteString("dwell", DwellBand(frame.DwellSeconds));
                w.WriteBoolean("fullscreen", frame.IsFullscreen);
                w.WriteString("idle", IdleBand(frame.InputIdleSeconds));

                w.WriteNumber("visits_today", Math.Max(0, frame.VisitsToday));
                w.WriteNumber("minutes_today", RoundTo5(frame.MinutesToday));
                w.WriteNumber("minutes_week", RoundTo5(frame.MinutesThisWeek));
                w.WriteNumber("day_streak", Math.Max(0, frame.DayStreak));
                w.WriteNumber("switches_10m", Math.Max(0, frame.SwitchesLast10Min));

                if (frame.SinceLastVisit is { } gap)
                {
                    w.WriteNumber("since_last_visit_min", RoundTo5((int)Math.Round(gap.TotalMinutes)));
                }

                if (!adult && !string.IsNullOrWhiteSpace(frame.DayArcSummary))
                {
                    w.WriteString("arc", AwarenessText.SanitizeDisplayName(frame.DayArcSummary, 160));
                }

                w.WriteString("time_of_day", frame.TimeOfDay.ToString());
                w.WriteString("weekday", frame.Weekday.ToString());
                w.WriteString("tier", frame.Tier.ToString());

                w.WriteStartArray("trends");
                foreach (var trend in frame.Trends)
                {
                    if (trend == null) continue;
                    w.WriteStringValue(trend.Label);
                }
                w.WriteEndArray();

                w.WriteStartObject("ccp");
                w.WriteBoolean("session", frame.CcpSessionRunning);
                w.WriteNumber("level", Math.Max(0, frame.UserLevel));
                w.WriteNumber("login_streak", Math.Max(0, frame.LoginStreakDays));
                if (!string.IsNullOrWhiteSpace(frame.RecentAchievementId))
                {
                    w.WriteString("achievement", AwarenessText.SanitizeId(frame.RecentAchievementId));
                }
                w.WriteEndObject();

                // The media SIGNAL without the media CONTENT: how it is playing and how many times it
                // has come round. "That is the fourth replay" is the joke; the track name is not, and
                // is machine-local (doc 02 §2.2).
                if (frame.NowPlaying is { } playing)
                {
                    w.WriteStartObject("media");
                    w.WriteString("state", PlaybackState(playing.PlaybackState));
                    w.WriteNumber("repeats", Math.Max(0, playing.RepeatCount));
                    w.WriteEndObject();
                }

                // Pattern labels only — a habit's label is authored by us, its evidence never leaves.
                // Withheld entirely for adult frames: a per-site habit label is exactly the
                // specificity the cluster rule exists to prevent, and Train 2 never has any anyway.
                if (!adult)
                {
                    w.WriteStartArray("habits");
                    foreach (var habit in frame.MatchedHabits)
                    {
                        if (habit == null || habit.Muted) continue;
                        w.WriteStringValue(AwarenessText.SanitizeId(habit.Pattern));
                    }
                    w.WriteEndArray();
                }

                // The ban list: her own delivered lines, so she cannot reuse a punchline. The newest
                // few go at full length because they are what she is most likely to rewrite by
                // accident; the older ones only need to be recognisable.
                w.WriteStartArray("recent");
                int written = 0;
                foreach (var line in frame.RecentReactions)
                {
                    if (line == null || string.IsNullOrWhiteSpace(line.Text)) continue;
                    if (written >= MaxRecentReactions) break;

                    int cap = written < FullTextRecentReactions ? RecentFullLength : RecentSummaryLength;
                    var text = AwarenessText.SanitizeDisplayName(line.Text, cap);
                    written++;
                    if (text.Length == 0) continue;
                    if (line.Text.Length > text.Length) text += "…";
                    w.WriteStringValue(text);
                }
                w.WriteEndArray();

                if (!cloud)
                {
                    // Local-only extras. Machine-local by definition; never emitted above.
                    if (frame.NowPlaying is { } media)
                    {
                        w.WriteStartObject("now_playing");
                        w.WriteString("title", AwarenessText.SanitizeDisplayName(media.Title, 120));
                        if (!string.IsNullOrWhiteSpace(media.Artist))
                        {
                            w.WriteString("artist", AwarenessText.SanitizeDisplayName(media.Artist, 80));
                        }
                        w.WriteEndObject();
                    }

                    if (!string.IsNullOrWhiteSpace(frame.PreviousAppId))
                    {
                        w.WriteString("from", AwarenessText.SanitizeId(frame.PreviousAppId));
                    }

                    w.WriteNumber("idle_seconds", Math.Max(0, frame.InputIdleSeconds));
                    w.WriteNumber("dwell_seconds", Math.Max(0, frame.DwellSeconds));
                }

                w.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Removes email addresses and digit runs of six or more from an allow-listed page title, then
        /// caps it. The allow list is an "I am fine with this app's titles" statement about an APP, not
        /// a promise that every future title from it is harmless, so the scrub runs regardless.
        ///
        /// <para>Returns an empty string when nothing usable survived, in which case no title field is
        /// written at all.</para>
        /// </summary>
        public static string ScrubTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";

            var text = EmailPattern.Replace(title, "…");
            text = LongDigitRun.Replace(text, "…");
            return AwarenessText.SanitizeDisplayName(text, TitleLength);
        }

        /// <summary>Minutes to the nearest five (doc 02 §6.2) — enough for a joke, not enough for a timeline.</summary>
        public static int RoundTo5(int minutes)
        {
            if (minutes <= 0) return 0;
            return (int)(Math.Round(minutes / 5.0) * 5);
        }

        /// <summary>Dwell as a band rather than a number of seconds.</summary>
        public static string DwellBand(int seconds) => seconds switch
        {
            < 60 => "<1m",
            < 300 => "1-5m",
            < 900 => "5-15m",
            < 1800 => "15-30m",
            < 3600 => "30-60m",
            < 7200 => "1-2h",
            _ => "2h+"
        };

        /// <summary>Real input idle as a band. "afk" is a different joke from "watching".</summary>
        public static string IdleBand(int seconds) => seconds switch
        {
            < 30 => "active",
            < 180 => "quiet",
            < 1800 => "idle",
            _ => "away"
        };

        /// <summary>
        /// Whitelisted playback state. SMTC reports one of six values; anything else — including
        /// whatever a future Windows build or a third-party session decides to call itself — reads as
        /// "unknown" rather than being forwarded verbatim.
        /// </summary>
        public static string PlaybackState(string? raw)
        {
            var id = AwarenessText.SanitizeId(raw);
            foreach (var known in KnownPlaybackStates)
            {
                if (string.Equals(id, known, StringComparison.Ordinal)) return known;
            }
            return "unknown";
        }
    }
}
