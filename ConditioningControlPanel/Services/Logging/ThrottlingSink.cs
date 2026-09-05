using System;
using System.Collections.Generic;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// Caps how many times one message can repeat inside a minute, and says so when it does.
    ///
    /// <para>A week of real logs is 34,500 lines, and a third of the bytes are three templates
    /// repeating: an emote crossfade every five seconds, a blocked bark, and a profile-sync
    /// response. Nobody reads the 400th copy, and the lines that matter are buried between them.
    /// The first 20 per template per minute go through; the rest are counted, and the count is
    /// written as one line the next time that template speaks:</para>
    ///
    /// <code>(suppressed 380 similar '[EMOTE] xfade -&gt; {Emote}' in the last 60s)</code>
    ///
    /// <para>Errors and Fatals are throttled by the same rule on purpose. A crash loop is exactly
    /// the case that used to write half a gigabyte, and the count is the diagnostic - a thousand
    /// identical stack traces are not a thousand facts.</para>
    ///
    /// <para>Keyed by (category, message TEMPLATE), never the rendered text: the template is the
    /// call site, so "connected to 3 devices" and "connected to 4 devices" are one thing, which is
    /// what makes the cap meaningful.</para>
    /// </summary>
    public sealed class ThrottlingSink : ILogEventSink, IDisposable
    {
        /// <summary>Events per key per window that pass through untouched.</summary>
        public const int BurstLimit = 20;
        public const int WindowMs = 60_000;

        /// <summary>Distinct keys tracked. Far above the app's real template count; the eviction
        /// path exists for templates built by concatenation, which would otherwise grow forever.</summary>
        private const int KeyCap = 2048;

        /// <summary>Joins category and template into one dictionary key; never appears in a template.</summary>
        private const char Separator = (char)1;

        private static readonly MessageTemplate SummaryTemplate =
            new MessageTemplateParser().Parse("(suppressed {Count} similar '{Template}' in the last 60s)");

        private sealed class Entry
        {
            public long WindowStart;
            public int Count;
            public int Suppressed;
            public LogEventLevel Level;
            public string Category = "App";
        }

        private readonly ILogEventSink _inner;
        private readonly Dictionary<string, Entry> _keys = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private bool _disposed;

        public ThrottlingSink(ILogEventSink inner) => _inner = inner;

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) return;
            if (_inner == null) return;

            bool pass = true;
            int summarise = 0;
            string template = string.Empty, category = "App";
            LogEventLevel level = logEvent.Level;

            try
            {
                category = CategoryOf(logEvent);
                template = logEvent.MessageTemplate?.Text ?? string.Empty;
                var key = category + Separator + template;
                long now = Environment.TickCount64;

                lock (_gate)
                {
                    if (!_keys.TryGetValue(key, out var entry))
                    {
                        if (_keys.Count >= KeyCap) EvictOldest();
                        entry = new Entry { WindowStart = now };
                        _keys[key] = entry;
                    }

                    if (now - entry.WindowStart >= WindowMs)
                    {
                        summarise = entry.Suppressed;
                        level = entry.Level;
                        entry.WindowStart = now;
                        entry.Count = 0;
                        entry.Suppressed = 0;
                    }

                    entry.Category = category;
                    entry.Level = logEvent.Level;
                    entry.Count++;
                    pass = entry.Count <= BurstLimit;
                    if (!pass) entry.Suppressed++;
                }
            }
            catch
            {
                // swallow: a throttle that throws costs the line it was throttling. Fall through
                // and emit.
                pass = true;
            }

            if (summarise > 0) EmitSummary(logEvent.Timestamp, level, category, template, summarise);
            if (pass) Safe(logEvent);
        }

        private static string CategoryOf(LogEvent e) =>
            e.Properties.TryGetValue(CategoryEnricher.PropertyName, out var v)
            && v is ScalarValue s && s.Value is string cat && cat.Length > 0 ? cat : "App";

        private void EmitSummary(DateTimeOffset at, LogEventLevel level, string category, string template, int count)
        {
            try
            {
                // The summary keeps the suppressed line's own level so a storm of errors does not
                // report itself as an Information note.
                var props = new[]
                {
                    new LogEventProperty("Count", new ScalarValue(count)),
                    new LogEventProperty("Template", new ScalarValue(template)),
                    new LogEventProperty(CategoryEnricher.PropertyName, new ScalarValue(category))
                };
                Safe(new LogEvent(at, level < LogEventLevel.Information ? LogEventLevel.Information : level,
                    null, SummaryTemplate, props));
            }
            catch { /* swallow: the summary is a courtesy, not a guarantee */ }
        }

        private void Safe(LogEvent e)
        {
            try { _inner.Emit(e); }
            catch { /* swallow: never throw out of a sink */ }
        }

        /// <summary>Drop the key whose window started longest ago - the least active template.</summary>
        private void EvictOldest()
        {
            string? oldestKey = null;
            long oldest = long.MaxValue;
            foreach (var kv in _keys)
            {
                if (kv.Value.WindowStart < oldest) { oldest = kv.Value.WindowStart; oldestKey = kv.Key; }
            }
            if (oldestKey != null) _keys.Remove(oldestKey);
        }

        /// <summary>
        /// Flush the outstanding counts. Without this, whatever a session suppressed in its last
        /// minute is simply lost, which is the minute a crash-loop investigation cares about.
        /// </summary>
        public void Flush()
        {
            List<(DateTimeOffset At, LogEventLevel Level, string Cat, string Template, int Count)> pending = new();
            lock (_gate)
            {
                foreach (var kv in _keys)
                {
                    if (kv.Value.Suppressed <= 0) continue;
                    var sep = kv.Key.IndexOf(Separator);
                    pending.Add((DateTimeOffset.Now, kv.Value.Level, kv.Value.Category,
                        sep >= 0 ? kv.Key.Substring(sep + 1) : kv.Key, kv.Value.Suppressed));
                    kv.Value.Suppressed = 0;
                }
            }
            foreach (var p in pending) EmitSummary(p.At, p.Level, p.Cat, p.Template, p.Count);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Flush(); } catch { /* swallow */ }
            try { (_inner as IDisposable)?.Dispose(); } catch { /* swallow */ }
        }
    }
}
