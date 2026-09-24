using System;
using System.Text.Json;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.AIService;

internal static class CompanionProxyContract
{
    internal static AiReplyResult ReadFailure(int status, string? body)
    {
        string? code = null;
        var retryable = status >= 500;
        try
        {
            using var json = JsonDocument.Parse(body ?? "{}");
            if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                code = error.GetString();
            if (json.RootElement.TryGetProperty("retryable", out var retry) && retry.ValueKind is JsonValueKind.True or JsonValueKind.False)
                retryable = retry.GetBoolean();
        }
        catch (JsonException) { /* Expected for an HTML gateway error. */ }
        if (code == "content_refused")
            return new AiReplyResult(string.Empty, false, new ModerationRefusalInfo(null, ModerationSource.Output));
        var failure = code switch
        {
            "budget_exhausted" or "weekly_budget_exhausted" or "weekly_budget_limit" => AiFailureKind.BudgetLimit,
            "daily_limit" or "daily_limit_reached" or "quota_exceeded" => AiFailureKind.DailyLimit,
            "input_too_large" or "invalid_response" or "empty_response" or "reply_incomplete" or "invalid_reply" or "empty_reply" => AiFailureKind.InvalidResponse,
            "request_in_progress" or "chat_busy" => AiFailureKind.Busy,
            _ => status is 401 or 403 ? AiFailureKind.SignInRequired : AiFailureKind.Unavailable
        };
        return AiReplyResult.Failed(failure, retryable);
    }

    internal static string CleanReply(string? text, string? finishReason)
    {
        // A cut-off generation must not be passed off as a complete answer or execute effects.
        if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        var clean = AiTextHygiene.StripMetadataTags(AiTextHygiene.Clean(text)).Trim();
        if (string.IsNullOrWhiteSpace(clean) || AiService.CountGarbledChars(clean) > 0) return string.Empty;
        // A plain chat must not expose a half-written effects envelope or reasoning block.
        if (clean.StartsWith("{", StringComparison.Ordinal) || clean.StartsWith("```", StringComparison.Ordinal)
            || clean.Contains("<think>", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return clean;
    }
}
