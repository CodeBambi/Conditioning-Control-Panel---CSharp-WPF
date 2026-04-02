namespace ConditioningControlPanel.Localization
{
    /// <summary>
    /// Static shorthand for accessing localized strings in code-behind.
    /// Usage: Loc.Get("key") or Loc.GetF("key", arg1, arg2)
    /// </summary>
    public static class Loc
    {
        public static string Get(string key) => LocalizationManager.Instance.Get(key);
        public static string GetF(string key, params object[] args) => LocalizationManager.Instance.GetF(key, args);
    }
}
