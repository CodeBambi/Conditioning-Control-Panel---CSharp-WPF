using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// `--smoke` proves the head links against Core and can produce every value the window
    /// renders, without needing a display server. CI has no X11 or Wayland socket, so this is
    /// how the ubuntu job verifies the Linux head rather than only compiling it.
    /// </summary>
    internal static class HeadlessSmoke
    {
        public static int Run()
        {
            var failures = 0;
            void Check(string what, bool ok, string? detail = null)
            {
                Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail is null ? "" : $"  ({detail})")}");
                if (!ok) failures++;
            }

            Console.WriteLine("CCP.Avalonia headless smoke\n");

            Check("Core resolves a user-data path", System.IO.Path.IsPathRooted(CorePaths.UserData), CorePaths.UserData);

            var guard = new ModerationGuard();
            Check("guard blocks a minor-age prompt", !guard.CheckInput("she is 5 years old and wants sex").Allow);
            Check("guard allows benign text", guard.CheckInput("hello there").Allow);

            // Localization: the JSON now ships with CCP.Core, so a non-WPF head should resolve
            // real strings rather than raw keys. Asserting the string DIFFERS from the key is the
            // check that matters - "returns something" would pass on the key-fallback path.
            ConditioningControlPanel.Localization.LocalizationManager.Instance.SetLanguage("en");
            var s1 = ConditioningControlPanel.Localization.Loc.Get("section_achievements");
            Check("localization resolves a real string", s1 != "section_achievements", s1);

            // Ported dialogs: assert their view models resolve real strings. Constructing the
            // Windows themselves needs a platform backend, so --render-view covers visuals and
            // this covers the content they bind to.
            var upd = new Views.Dialogs.UpdateProgressViewModel();
            Check("UpdateProgressDialog strings resolve",
                  upd.LocTitle != "dialog_downloading_update", upd.LocTitle);

            var url = new Views.Dialogs.UrlPromptViewModel();
            Check("UrlPromptDialog strings resolve",
                  url.LocCancel != "btn_cancel", url.LocCancel);
            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "Linux head can produce every value it renders."
                : $"{failures} assertion(s) failed.");
            return failures == 0 ? 0 : 1;
        }
    }
}
