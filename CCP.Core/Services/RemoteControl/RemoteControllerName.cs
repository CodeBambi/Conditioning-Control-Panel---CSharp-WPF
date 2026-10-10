namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>The controller's typed name (WPF Services/Remote/RemoteScreenState.cs): trimmed,
    /// letters/digits/space/<c>._-</c> only, 1..24 characters, else none (the HUD then says "Someone").</summary>
    public static class RemoteControllerName
    {
        public const int MaxLength = 24;

        public static string? Sanitize(string? raw)
        {
            if (raw == null) return null;
            var t = raw.Trim();
            if (t.Length == 0 || t.Length > MaxLength) return null;
            foreach (var c in t)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '_' || c == '-')) return null;
            return t;
        }
    }
}
