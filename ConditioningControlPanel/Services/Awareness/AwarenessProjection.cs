using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// One codepath, two projections (doc 02 §6.2). A <see cref="ContextFrame"/> is a local object and
    /// may legitimately hold things the cloud must never see; what leaves the machine is whatever this
    /// class writes, and nothing else.
    ///
    /// <para><b>Cloud projection — the exhaustive list of what crosses the wire:</b> cluster id,
    /// category, our own coarse app id and display name, bucketed history numbers (visits, minutes
    /// rounded to five, dwell as a band), the transition kind, trend LABELS, time bucket and weekday,
    /// in-app CCP state, habit pattern labels, recent-reaction summaries, the rarity tier, and — only
    /// when the user allow-listed that app for titles — the sanitised page title.</para>
    ///
    /// <para><b>Never, on any path:</b> raw window titles, OCR text, keystrokes, URLs, process paths,
    /// screenshots, or SMTC track/artist names. And for the <c>site_eh</c> cluster the app id, the
    /// display name, the title and the day arc are all withheld as well — only the cluster id goes,
    /// regardless of allow lists.</para>
    ///
    /// <para><b>Shell status.</b> The privacy rules above are implemented and tested now, because they
    /// are the part that cannot be walked back after it ships. The shape is minimal and the prompt
    /// package owns making it good — it may add fields, but it may not widen this list.</para>
    /// </summary>
    public static class AwarenessProjection
    {
        /// <summary>Projection schema version. Bumped when fields are added; the server tolerates unknown fields.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Recent lines included in the ban list section of a projection.</summary>
        public const int MaxRecentReactions = 10;

        /// <summary>
        /// The sanitised, bucketed JSON that may be sent to a cloud provider. Also exactly what the
        /// "what she can see" panel renders — showing the real wire format is the point (doc 02 §6.4),
        /// so this method must never grow a "for display" variant that differs from the truth.
        /// </summary>
        public static string BuildCloudProjection(ContextFrame? frame) => Build(frame, cloud: true);

        /// <summary>
        /// The fuller frame for the local Ollama path, which is machine-local by definition: page title
        /// and now-playing are included, and the adult cluster is not collapsed. Nothing here is
        /// permitted to reach a remote endpoint.
        /// </summary>
        public static string BuildLocalProjection(ContextFrame? frame) => Build(frame, cloud: false);

        private static string Build(ContextFrame? frame, bool cloud)
        {
            if (frame == null) return "{}";

            bool adult = cloud && frame.IsAdultCluster;

            using var stream = new MemoryStream(512);
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
                    w.WriteString("title", AwarenessText.SanitizeDisplayName(frame.PageTitleSanitized, 120));
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

                // Pattern labels only — a habit's label is authored by us, its evidence never leaves.
                w.WriteStartArray("habits");
                foreach (var habit in frame.MatchedHabits)
                {
                    if (habit == null || habit.Muted) continue;
                    w.WriteStringValue(AwarenessText.SanitizeId(habit.Pattern));
                }
                w.WriteEndArray();

                // The ban list: her own delivered lines, so she cannot reuse a punchline.
                w.WriteStartArray("recent");
                int written = 0;
                foreach (var line in frame.RecentReactions)
                {
                    if (line == null || string.IsNullOrWhiteSpace(line.Text)) continue;
                    if (written++ >= MaxRecentReactions) break;
                    w.WriteStringValue(AwarenessText.SanitizeDisplayName(line.Text, 160));
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
                        w.WriteString("state", AwarenessText.SanitizeDisplayName(media.PlaybackState, 24));
                        w.WriteNumber("repeats", Math.Max(0, media.RepeatCount));
                        w.WriteEndObject();
                    }

                    w.WriteNumber("idle_seconds", Math.Max(0, frame.InputIdleSeconds));
                    w.WriteNumber("dwell_seconds", Math.Max(0, frame.DwellSeconds));
                }

                w.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
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
    }
}
