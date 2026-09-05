using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Serilog.Events;
using Serilog.Parsing;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// Renders a log event's message template the way the old output template did, with
    /// <c>{Message:lj}</c>: string values appear as themselves, not wrapped in quotes.
    ///
    /// <para>Serilog's own <see cref="LogEvent.RenderMessage(TextWriter, System.IFormatProvider)"/>
    /// renders a string scalar through <see cref="ScalarValue.Render"/>, which QUOTES it, because
    /// the default rendering is meant to round-trip as structured data. The app's file sink used to
    /// be an output template carrying <c>:lj</c>, so nobody ever saw those quotes; moving to an
    /// <see cref="Serilog.Formatting.ITextFormatter"/> lost the flag and every string parameter
    /// arrived on disk in quotes - <c>Application starting v"6.9.1"</c>, <c>lang='"en"'</c>, and a
    /// bare <c>""</c> wherever a parameter was the empty string.</para>
    ///
    /// <para>It also broke the category column: <see cref="CategoryEnricher"/> reads the tag off the
    /// front of the message, and <c>"[BARK]"</c> passed as a PARAMETER rendered as <c>"[BARK]"</c>
    /// (quotes included), which is not a tag. One shared renderer means the formatter and the
    /// enricher can never disagree about what a line says.</para>
    /// </summary>
    public static class LiteralMessage
    {
        /// <summary>The message as it will appear on disk, before redaction and prefix stripping.</summary>
        public static string Render(LogEvent logEvent)
        {
            if (logEvent?.MessageTemplate == null) return string.Empty;
            using var sw = new StringWriter(CultureInfo.InvariantCulture);
            Render(logEvent, sw);
            return sw.ToString();
        }

        /// <summary>Writes the rendered message straight to <paramref name="output"/>.</summary>
        public static void Render(LogEvent logEvent, TextWriter output)
        {
            if (logEvent?.MessageTemplate == null || output == null) return;

            foreach (var token in logEvent.MessageTemplate.Tokens)
            {
                if (token is TextToken text)
                {
                    output.Write(text.Text);
                    continue;
                }

                if (token is PropertyToken property)
                {
                    RenderProperty(property, logEvent.Properties, output);
                    continue;
                }

                // Not a shape Serilog produces today, but a token type we do not know how to
                // treat literally is still better rendered than dropped.
                token.Render(logEvent.Properties, output, CultureInfo.InvariantCulture);
            }
        }

        private static void RenderProperty(
            PropertyToken token,
            IReadOnlyDictionary<string, LogEventPropertyValue> properties,
            TextWriter output)
        {
            if (!properties.TryGetValue(token.PropertyName, out var value))
            {
                // Serilog writes the placeholder back out when the parameter is missing, and so do
                // we: "{Count}" on the line is a clearer bug report than a silent gap.
                output.Write(token.ToString());
                return;
            }

            if (!token.Alignment.HasValue)
            {
                WriteValue(value, token.Format, output);
                return;
            }

            // Alignment has to measure the rendered text, so this one path buffers.
            using var buffer = new StringWriter(CultureInfo.InvariantCulture);
            WriteValue(value, token.Format, buffer);
            Pad(output, buffer.ToString(), token.Alignment.Value);
        }

        private static void WriteValue(LogEventPropertyValue value, string? format, TextWriter output)
        {
            // The whole point: a string is written as itself. Everything else - numbers, enums,
            // sequences, structures - keeps Serilog's own rendering, including the format string.
            if (value is ScalarValue scalar && scalar.Value is string s)
            {
                output.Write(s);
                return;
            }
            value.Render(output, format, CultureInfo.InvariantCulture);
        }

        private static void Pad(TextWriter output, string value, Alignment alignment)
        {
            int pad = alignment.Width - value.Length;
            if (pad <= 0)
            {
                output.Write(value);
                return;
            }
            if (alignment.Direction == AlignmentDirection.Left)
            {
                output.Write(value);
                output.Write(new string(' ', pad));
            }
            else
            {
                output.Write(new string(' ', pad));
                output.Write(value);
            }
        }
    }
}
