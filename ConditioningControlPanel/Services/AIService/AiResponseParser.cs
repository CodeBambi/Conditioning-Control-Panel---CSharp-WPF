using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.AIService
{
    public class AiResponseParser : IAiResponseParser
    {
        private readonly Func<string> _fallbackProvider;

        private static readonly JsonDocumentOptions JsonOptions = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        public AiResponseParser(Func<string> fallbackProvider)
        {
            _fallbackProvider = fallbackProvider;
        }

        public ParsedAiResponse Parse(string response)
        {
            var trimmedResponse = response.Trim();
            trimmedResponse = ExtractFromMarkdownBlocks(trimmedResponse);

            if (TryProcessStandardJson(trimmedResponse, out var standardResult))
            {
                return standardResult;
            }

            return ParseMixedFormat(response);
        }

        private string ExtractFromMarkdownBlocks(string text)
        {
            var jsonBlockMatch = Regex.Match(text, @"```json\s*(.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (jsonBlockMatch.Success)
            {
                return jsonBlockMatch.Groups[1].Value.Trim();
            }

            var codeBlockMatch = Regex.Match(text, @"```\s*(.*?)\s*```", RegexOptions.Singleline);
            if (codeBlockMatch.Success)
            {
                return codeBlockMatch.Groups[1].Value.Trim();
            }

            return text;
        }

        private bool TryProcessStandardJson(string text, out ParsedAiResponse result)
        {
            result = new ParsedAiResponse();
            try
            {
                var jsonCandidate = ExtractOuterJsonObject(text);
                using var jsonDoc = JsonDocument.Parse(jsonCandidate, JsonOptions);

                if (jsonDoc.RootElement.TryGetProperty("response", out var respProp))
                {
                    result.CleanText = respProp.GetString()?.Trim() ?? string.Empty;
                    ExtractEffects(jsonDoc.RootElement, result.Commands);
                    result.CleanText = SanitizeResponse(result.CleanText);
                    return true;
                }
            }
            catch
            {
                // Not standard JSON or missing 'response' property
            }
            return false;
        }

        private string ExtractOuterJsonObject(string text)
        {
            var firstBrace = text.IndexOf('{');
            var lastBrace = text.LastIndexOf('}');
            if (firstBrace != -1 && lastBrace > firstBrace)
            {
                var json = text.Substring(firstBrace, lastBrace - firstBrace + 1);
                return BalanceBraces(json);
            }
            return text;
        }

        private void ExtractEffects(JsonElement root, List<AiCommandData> commands)
        {
            if (root.TryGetProperty("effects", out var effectsProp) && effectsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var effect in effectsProp.EnumerateArray())
                {
                    var cmd = AiCommandData.ParseCommand(effect.GetRawText());
                    if (cmd != null)
                        commands.Add(cmd);
                }
            }
        }

        private ParsedAiResponse ParseMixedFormat(string response)
        {
            var result = new ParsedAiResponse();
            var textOnly = response;
            var index = 0;

            while ((index = textOnly.IndexOf('{', index)) != -1)
            {
                var jsonBlock = FindAndBalanceJsonBlock(textOnly, index, out var start, out var end);
                if (jsonBlock != null)
                {
                    if (TryProcessJsonInMixedText(jsonBlock, ref textOnly, start, end, ref index, result))
                    {
                        continue;
                    }
                }
                index++;
            }

            result.CleanText = SanitizeResponse(textOnly);
            return result;
        }

        private string? FindAndBalanceJsonBlock(string text, int startIndex, out int start, out int end)
        {
            start = startIndex;
            end = -1;
            var balance = 0;
            var lastPossibleEnd = -1;

            for (var i = start; i < text.Length; i++)
            {
                if (text[i] == '{') balance++;
                else if (text[i] == '}')
                {
                    balance--;
                    lastPossibleEnd = i;
                }

                if (balance == 0)
                {
                    end = i;
                    break;
                }
            }

            if (end == -1 && lastPossibleEnd != -1)
            {
                end = lastPossibleEnd;
            }

            if (end != -1)
            {
                var json = text.Substring(start, end - start + 1);
                return BalanceBraces(json);
            }

            return null;
        }

        private string BalanceBraces(string json)
        {
            json = RepairJson(json);

            int openCount = json.Count(c => c == '{');
            int closeCount = json.Count(c => c == '}');
            while (openCount > closeCount)
            {
                json += "}";
                closeCount++;
            }

            int openBracket = json.Count(c => c == '[');
            int closeBracket = json.Count(c => c == ']');
            while (openBracket > closeBracket)
            {
                json += "]";
                closeBracket++;
            }

            return json;
        }

        private string RepairJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;

            json = Regex.Replace(json, @"(\[[ \t\r\n]*),", "$1");
            json = Regex.Replace(json, @"(\{[ \t\r\n]*),", "$1");
            json = Regex.Replace(json, @",([ \t\r\n]*),", ",");

            var repaired = new StringBuilder();
            var stack = new Stack<char>();
            bool inQuotes = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    inQuotes = !inQuotes;
                }

                if (!inQuotes)
                {
                    if (c == '{') stack.Push('{');
                    else if (c == '[') stack.Push('[');
                    else if (c == '}')
                    {
                        if (stack.Count > 0 && stack.Peek() == '{') stack.Pop();
                    }
                    else if (c == ']')
                    {
                        while (stack.Count > 0 && stack.Peek() == '{')
                        {
                            repaired.Append('}');
                            stack.Pop();
                        }
                        if (stack.Count > 0 && stack.Peek() == '[') stack.Pop();
                    }
                }
                repaired.Append(c);
            }
            json = repaired.ToString();

            json = Regex.Replace(json, @"([{,]\s*)([a-zA-Z0-9_]+)(\s*:)", "$1\"$2\"$3");
            json = Regex.Replace(json, @"""(.*?)""", m => m.Value.Replace("\r", "\\r").Replace("\n", "\\n"), RegexOptions.Singleline);

            return json;
        }

        private bool TryProcessJsonInMixedText(string json, ref string textOnly, int start, int end, ref int index, ParsedAiResponse result)
        {
            try
            {
                using var doc = JsonDocument.Parse(json, JsonOptions);
                if (doc.RootElement.TryGetProperty("response", out var respProp))
                {
                    var responseText = respProp.GetString() ?? string.Empty;
                    ExtractEffects(doc.RootElement, result.Commands);

                    textOnly = textOnly.Remove(start, end - start + 1);
                    textOnly = textOnly.Insert(start, responseText);
                    index = start + responseText.Length;
                    return true;
                }

                var cmdOld = AiCommandData.ParseCommand(json);
                if (cmdOld != null)
                {
                    result.Commands.Add(cmdOld);
                    textOnly = textOnly.Remove(start, end - start + 1);
                    index = start;
                    return true;
                }
            }
            catch
            {
                // Ignore and move on
            }
            return false;
        }

        private string SanitizeResponse(string? response)
        {
            if (string.IsNullOrEmpty(response))
                return response ?? string.Empty;

            var sanitized = Regex.Replace(response, @"\[Category:[^\]]*\]", "", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"\[[A-Za-z]+/[A-Za-z]+\]", "", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"\[(?:Category|App|Title|Duration|Context):[^\]]*\]", "", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"\s{2,}", " ");
            sanitized = sanitized.Trim();

            return string.IsNullOrWhiteSpace(sanitized) ? _fallbackProvider() : sanitized;
        }
    }
}
