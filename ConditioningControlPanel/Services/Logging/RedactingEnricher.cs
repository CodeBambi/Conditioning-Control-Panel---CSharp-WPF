using Serilog.Core;
using Serilog.Events;

namespace ConditioningControlPanel.Services.Logging
{
    /// <summary>
    /// Runs <see cref="LogRedactor"/> over every string that arrives as a log PROPERTY.
    ///
    /// <para>Properties are the half of a log line the formatter cannot reach as text: by the time
    /// it renders, a path is already spliced into the output. Doing it here also means a structured
    /// sink (the flight recorder keeps events, not strings) holds redacted values too.</para>
    ///
    /// <para>The formatter still runs the redactor over the finished line as a safety net, because
    /// roughly half the call sites in this app use an interpolated <c>$"..."</c> string, which puts
    /// the content into the TEMPLATE where no enricher can see it.</para>
    /// </summary>
    public sealed class RedactingEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (logEvent == null) return;
            foreach (var kv in logEvent.Properties)
            {
                var replacement = Visit(kv.Value);
                if (replacement != null)
                    logEvent.AddOrUpdateProperty(new LogEventProperty(kv.Key, replacement));
            }
        }

        /// <summary>
        /// Returns a replacement value, or null when nothing in the value needed redacting.
        /// Serilog's property values are immutable, so a change means rebuilding the container.
        /// </summary>
        public static LogEventPropertyValue? Visit(LogEventPropertyValue value)
        {
            switch (value)
            {
                case ScalarValue scalar when scalar.Value is string s:
                {
                    var redacted = LogRedactor.Redact(s);
                    return ReferenceEquals(redacted, s) ? null : new ScalarValue(redacted);
                }

                case SequenceValue seq:
                {
                    LogEventPropertyValue[]? copy = null;
                    for (int i = 0; i < seq.Elements.Count; i++)
                    {
                        var replaced = Visit(seq.Elements[i]);
                        if (replaced == null) continue;
                        copy ??= System.Linq.Enumerable.ToArray(seq.Elements);
                        copy[i] = replaced;
                    }
                    return copy == null ? null : new SequenceValue(copy);
                }

                case StructureValue str:
                {
                    LogEventProperty[]? copy = null;
                    for (int i = 0; i < str.Properties.Count; i++)
                    {
                        var replaced = Visit(str.Properties[i].Value);
                        if (replaced == null) continue;
                        copy ??= System.Linq.Enumerable.ToArray(str.Properties);
                        copy[i] = new LogEventProperty(str.Properties[i].Name, replaced);
                    }
                    return copy == null ? null : new StructureValue(copy, str.TypeTag);
                }

                case DictionaryValue dict:
                {
                    System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ScalarValue, LogEventPropertyValue>>? copy = null;
                    int i = 0;
                    foreach (var entry in dict.Elements)
                    {
                        var replaced = Visit(entry.Value);
                        if (replaced != null)
                        {
                            copy ??= new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ScalarValue, LogEventPropertyValue>>(dict.Elements);
                            copy[i] = new System.Collections.Generic.KeyValuePair<ScalarValue, LogEventPropertyValue>(entry.Key, replaced);
                        }
                        i++;
                    }
                    return copy == null ? null : new DictionaryValue(copy);
                }
            }
            return null;
        }
    }
}
