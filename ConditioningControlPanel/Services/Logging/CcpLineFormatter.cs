using System;
using System.Globalization;
using System.IO;
using Serilog.Events;
using Serilog.Formatting;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// The line format for every file the app writes:
    /// <code>
    /// HH:mm:ss.fff LVL [Category     ] message
    /// </code>
    ///
    /// <para>An <see cref="ITextFormatter"/> rather than an output template, for three things a
    /// template cannot do: strip a <c>[Category]</c> prefix that is now duplicated by the category
    /// column, compact an exception down to the frames that are actually ours, and run the redactor
    /// over the finished line. That last one is the safety net that matters: about half this
    /// codebase's log calls pass an interpolated <c>$"..."</c> string, so the user's path is inside
    /// the message TEMPLATE where the property enricher can never see it.</para>
    ///
    /// <para>The date is deliberately absent. It is written once in the session header; repeating
    /// it 34,000 times a week bought nothing but bytes.</para>
    /// </summary>
    public sealed class CcpLineFormatter : ITextFormatter
    {
        /// <summary>Width of the category column. Long categories are cut, not elided: an ellipsis
        /// would cost a character that a reader can spend on the name.</summary>
        public const int CategoryWidth = 13;

        private const int MaxAppFrames = 8;

        public void Format(LogEvent logEvent, TextWriter output)
        {
            if (logEvent == null || output == null) return;
            try
            {
                output.Write(logEvent.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
                output.Write(' ');
                output.Write(Abbreviate(logEvent.Level));
                output.Write(" [");
                var cat = Category(logEvent);
                output.Write(cat.Length >= CategoryWidth ? cat.Substring(0, CategoryWidth) : cat.PadRight(CategoryWidth));
                output.Write("] ");

                var message = Render(logEvent);
                int skip = PrefixLength(message, cat);
                output.Write(LogRedactor.Redact(skip > 0 ? message.Substring(skip) : message));
                output.WriteLine();

                if (logEvent.Exception != null)
                    WriteException(logEvent.Exception, output);
            }
            catch (Exception ex)
            {
                // swallow: a formatter that throws takes the whole sink with it, and a mangled line
                // is worth more than a lost one.
                try { output.WriteLine("?? formatter failed: " + ex.GetType().Name); } catch { }
            }
        }

        public static string Abbreviate(LogEventLevel level) => level switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            _ => "FTL"
        };

        private static string Category(LogEvent e)
        {
            if (e.Properties.TryGetValue(CategoryEnricher.PropertyName, out var value)
                && value is ScalarValue s && s.Value is string cat && cat.Length > 0)
                return cat;
            return "App";
        }

        /// <summary>
        /// The message, with string parameters written literally. NOT
        /// <see cref="LogEvent.RenderMessage(TextWriter, System.IFormatProvider)"/>: that quotes
        /// every string scalar. See <see cref="LiteralMessage"/>.
        /// </summary>
        private static string Render(LogEvent e) => LiteralMessage.Render(e);

        /// <summary>
        /// How many characters of the message repeat the category column. Only an EXACT repeat is
        /// stripped ("[RES] ..." under category RES, "ProfileSyncService: ..." under ProfileSync);
        /// anything else is left alone so that existing greps against the message keep working.
        /// </summary>
        public static int PrefixLength(string message, string cat)
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(cat)) return 0;

            if (message[0] == '[')
            {
                int close = message.IndexOf(']', 1);
                if (close != cat.Length + 1) return 0;
                if (string.Compare(message, 1, cat, 0, cat.Length, StringComparison.OrdinalIgnoreCase) != 0) return 0;
                return SkipSpace(message, close + 1);
            }

            int i = 0;
            while (i < message.Length && IsWordChar(message[i])) i++;
            int wordEnd = i;
            if (i < message.Length && message[i] == '.')
            {
                i++;
                while (i < message.Length && IsWordChar(message[i])) i++;
            }
            if (i >= message.Length || message[i] != ':') return 0;

            int len = wordEnd;
            if (len == cat.Length + 7 &&
                string.Compare(message, cat.Length, "Service", 0, 7, StringComparison.Ordinal) == 0)
                len = cat.Length;
            if (len != cat.Length) return 0;
            if (string.Compare(message, 0, cat, 0, cat.Length, StringComparison.OrdinalIgnoreCase) != 0) return 0;
            return SkipSpace(message, i + 1);
        }

        private static int SkipSpace(string s, int at)
        {
            while (at < s.Length && s[at] == ' ') at++;
            return at;
        }

        private static bool IsWordChar(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';

        /// <summary>
        /// Compact the exception: every "type: message" line survives (including the inner-exception
        /// chain), our own frames survive up to <see cref="MaxAppFrames"/> with the path cut to a
        /// file name, and runs of framework frames collapse to a count. A WPF layout exception
        /// carries 40+ frames of PresentationFramework that tell a reader nothing and cost a
        /// kilobyte each time the exception repeats.
        /// </summary>
        private static void WriteException(Exception exception, TextWriter output)
        {
            var text = LogRedactor.Redact(exception.ToString());
            int appFrames = 0, framework = 0;

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("at ", StringComparison.Ordinal))
                {
                    if (IsAppFrame(line) && appFrames < MaxAppFrames)
                    {
                        FlushFramework(ref framework, output);
                        appFrames++;
                        output.Write("      ");
                        output.WriteLine(CompactFrame(line));
                    }
                    else framework++;
                    continue;
                }

                if (line.StartsWith("--- End of stack trace", StringComparison.Ordinal)) continue;

                FlushFramework(ref framework, output);
                output.Write("   ");
                output.WriteLine(line);
            }
            FlushFramework(ref framework, output);
        }

        private static void FlushFramework(ref int count, TextWriter output)
        {
            if (count == 0) return;
            output.Write("      ... ");
            output.Write(count.ToString(CultureInfo.InvariantCulture));
            output.WriteLine(" framework frames");
            count = 0;
        }

        private static bool IsAppFrame(string frame) =>
            frame.IndexOf("ConditioningControlPanel", StringComparison.Ordinal) >= 0 ||
            frame.IndexOf("CCP.", StringComparison.Ordinal) >= 0;

        /// <summary>" in ~\src\Services\Foo.cs:line 123" -&gt; " in Foo.cs:123".</summary>
        private static string CompactFrame(string frame)
        {
            int at = frame.LastIndexOf(" in ", StringComparison.Ordinal);
            if (at < 0) return frame;
            var tail = frame.Substring(at + 4);
            int slash = tail.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) tail = tail.Substring(slash + 1);
            return frame.Substring(0, at + 4) + tail.Replace(":line ", ":");
        }
    }
}
