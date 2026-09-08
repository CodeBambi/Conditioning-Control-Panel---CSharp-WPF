using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
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

            // Ported dialogs: assert their view models resolve real strings.
            var upd = new Views.Dialogs.UpdateProgressViewModel();
            Check("UpdateProgressDialog strings resolve",
                  upd.LocTitle != "dialog_downloading_update", upd.LocTitle);

            var url = new Views.Dialogs.UrlPromptViewModel();
            Check("UrlPromptDialog strings resolve",
                  url.LocCancel != "btn_cancel", url.LocCancel);

            CheckUrlPrompt(Check);
            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "Linux head can produce every value it renders."
                : $"{failures} assertion(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void CheckUrlPrompt(Action<string, bool, string?> check)
        {
            Window? owner = null;
            try
            {
                // Same backend as RenderProof, but no desktop lifetime or real shell/services.
                AppBuilder.Configure<App>()
                    .UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
                check("URL prompt bootstrap has no desktop lifetime", Application.Current!.ApplicationLifetime is null, null);
                owner = new Window();
                owner.Show();

                void WithDialog(string scenario, Action<UrlPromptDialog, TextBox, Task> test)
                {
                    var dialog = new UrlPromptDialog();
                    try
                    {
                        var completion = dialog.ShowDialog(owner);
                        Dispatcher.UIThread.RunJobs();
                        check($"URL prompt {scenario}: starts open without a result/error",
                            dialog.IsVisible && !completion.IsCompleted && dialog.Result is null
                            && !dialog.FindControl<TextBlock>("TxtError")!.IsVisible, null);
                        test(dialog, dialog.FindControl<TextBox>("TxtUrl")!, completion);
                    }
                    finally
                    {
                        if (dialog.IsVisible) dialog.Close();
                        Dispatcher.UIThread.RunJobs();
                    }
                }

                void PressKey(UrlPromptDialog dialog, PhysicalKey key)
                {
                    dialog.FindControl<TextBox>("TxtUrl")!.Focus();
                    check($"URL prompt {key}: TxtUrl receives keyboard input",
                        dialog.FindControl<TextBox>("TxtUrl")!.IsFocused, null);
                    dialog.KeyPressQwerty(key, RawInputModifiers.None);
                    // The key press may close/dispose the dialog; release on the inert owner then.
                    (dialog.IsVisible ? dialog : owner).KeyReleaseQwerty(key, RawInputModifiers.None);
                }

                void Submit(UrlPromptDialog dialog, bool enter)
                {
                    if (enter) PressKey(dialog, PhysicalKey.Enter);
                    else dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                }

                void Rejected(string scenario, UrlPromptDialog dialog, Task completion)
                {
                    var error = dialog.FindControl<TextBlock>("TxtError")!;
                    var message = Loc.Get("deeper_url_prompt_invalid");
                    check($"URL prompt {scenario}: rejects without completing",
                        dialog.Result is null && dialog.IsVisible && !completion.IsCompleted
                        && error.IsVisible && error.Text == message && message != "deeper_url_prompt_invalid",
                        $"Result={dialog.Result ?? "<null>"}, visible={dialog.IsVisible}, completed={completion.IsCompleted}, errorVisible={error.IsVisible}, error={error.Text}");
                }

                void Accepted(string scenario, UrlPromptDialog dialog, Task completion, string expected)
                {
                    check($"URL prompt {scenario}: returns trimmed original and closes",
                        dialog.Result == expected && !dialog.IsVisible && completion.IsCompletedSuccessfully,
                        $"Result={dialog.Result ?? "<null>"}, visible={dialog.IsVisible}, completed={completion.Status}");
                }

                foreach (var enter in new[] { false, true })
                {
                    var submit = enter ? "Enter" : "Load";
                    foreach (var input in new[] { "not a URL", "", "   ", "relative/path", "https://",
                        "file:///tmp/example.mp4", "ftp://example.invalid/a", "data:text/plain,x", "javascript:void(0)" })
                    {
                        WithDialog($"{submit} '{input}'", (dialog, text, completion) =>
                        {
                            text.Text = input;
                            Submit(dialog, enter);
                            Rejected($"{submit} '{input}'", dialog, completion);
                        });
                    }

                    foreach (var (input, expected) in new[]
                    {
                        ("  http://example.invalid/a  ", "http://example.invalid/a"),
                        ("  https://example.invalid/a.ccpenh.json  ", "https://example.invalid/a.ccpenh.json"),
                        ("  HTTPS://EXAMPLE.invalid:443/a/../b?q=%2f#Part  ", "HTTPS://EXAMPLE.invalid:443/a/../b?q=%2f#Part")
                    })
                    {
                        WithDialog($"{submit} accepts '{input}'", (dialog, text, completion) =>
                        {
                            text.Text = input;
                            Submit(dialog, enter);
                            Accepted(submit, dialog, completion, expected);
                        });
                    }

                    WithDialog($"{submit} correction", (dialog, text, completion) =>
                    {
                        text.Text = "not a URL";
                        Submit(dialog, enter);
                        Rejected($"{submit} before correction", dialog, completion);
                        if (completion.IsCompleted) return; // Already failed; a closed window cannot be corrected.
                        text.Text = "  https://example.invalid/corrected  ";
                        Submit(dialog, enter);
                        Accepted($"{submit} correction", dialog, completion, "https://example.invalid/corrected");
                    });
                }

                foreach (var escape in new[] { false, true })
                    foreach (var invalidFirst in new[] { false, true })
                    {
                        var scenario = $"{(escape ? "Escape" : "Cancel")} (invalid first: {invalidFirst})";
                        WithDialog(scenario, (dialog, text, completion) =>
                        {
                            text.Text = "not a URL";
                            if (invalidFirst)
                            {
                                Submit(dialog, false);
                                Rejected(scenario, dialog, completion);
                                if (completion.IsCompleted) return;
                            }
                            if (escape) PressKey(dialog, PhysicalKey.Escape);
                            else dialog.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Dispatcher.UIThread.RunJobs();
                            check($"URL prompt {scenario}: closes without a result",
                                dialog.Result is null && !dialog.IsVisible && completion.IsCompletedSuccessfully, null);
                        });
                    }
            }
            catch (Exception ex)
            {
                check("URL prompt fixture executes", false, ex.ToString());
            }
            finally
            {
                owner?.Close();
                if (owner is not null) Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
