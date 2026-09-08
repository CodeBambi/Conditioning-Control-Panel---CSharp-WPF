using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            CheckTextEditor(Check);
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

        /// <summary>
        /// #493 TextEditorDialog regressions, at public seams only. The headless platform is
        /// already set up by <see cref="CheckUrlPrompt"/>; this fixture must not set it up again.
        /// The dialog constructor is entirely in-memory (no file, network or service access), so
        /// no fixture file is created or deleted here.
        /// </summary>
        private static void CheckTextEditor(Action<string, bool, string?> check)
        {
            Window? owner = null;
            try
            {
                owner = new Window();
                owner.Show();

                static Border? Row(TextEditorDialog dialog, int index)
                {
                    var list = dialog.FindControl<ItemsControl>("ItemList")!;
                    var container = list.ContainerFromIndex(index);
                    return container?.GetVisualDescendants().OfType<Border>()
                        .FirstOrDefault(b => b.Name == "ItemBorder");
                }

                static uint? Fill(Border? border) =>
                    (border?.Background as ISolidColorBrush)?.Color.ToUInt32();

                static List<TextItem> Rows(TextEditorDialog dialog) =>
                    dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>().ToList();

                static void Select(TextEditorDialog dialog, int index, bool selected)
                {
                    Rows(dialog)[index].IsSelected = selected;
                    Dispatcher.UIThread.RunJobs();
                }

                // Every button the Ask() stand-in builds, in declaration order, with its caption.
                // Buttons hold a TextBlock rather than Content (access-key reason in the dialog).
                static List<(Button Button, string Caption)> PromptButtons(Window prompt) =>
                    prompt.GetVisualDescendants().OfType<Button>()
                        .Select(b => (b, (b.Content as TextBlock)?.Text ?? string.Empty)).ToList();

                static void ClickButton(Button button)
                {
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                }

                static Window? OpenPrompt(TextEditorDialog dialog) =>
                    dialog.OwnedWindows.FirstOrDefault(w => w.IsVisible);

                static void PressKey(Window target, PhysicalKey key)
                {
                    target.KeyPressQwerty(key, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                    if (target.IsVisible) target.KeyReleaseQwerty(key, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                // Leaves no prompt behind if the keyboard route did not answer it. The answer is
                // named per call site by its own known button index, not by a generic
                // answer-anything helper, and it is always the non-destructive one.
                static void AnswerLeftoverPrompt(TextEditorDialog dialog, int index, int expectedButtons)
                {
                    if (OpenPrompt(dialog) is not Window prompt) return;
                    var buttons = PromptButtons(prompt);
                    if (buttons.Count != expectedButtons) return;
                    buttons[index].Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                }

                // Closing an editor that holds unsaved edits raises the real three-answer
                // save-before-closing prompt. It is resolved through that prompt's own middle
                // answer ("do not save, close"), i.e. the same action a user has - never by
                // Hide(), never by a blanket answer-every-dialog helper, and never by a
                // production flag. Any other prompt shape is reported, not clicked.
                void CloseEditor(string scenario, TextEditorDialog dialog)
                {
                    if (!dialog.IsVisible) return;
                    dialog.Close();
                    Dispatcher.UIThread.RunJobs();
                    var prompt = OpenPrompt(dialog);
                    if (prompt is null) return;
                    var buttons = PromptButtons(prompt);
                    if (buttons.Count != 3)
                    {
                        check($"text editor {scenario}: close raises the known three-answer prompt",
                            false, $"buttons={string.Join("|", buttons.Select(b => b.Caption))}");
                        return;
                    }
                    ClickButton(buttons[1].Button); // "do not save" -> Close(false)
                }

                void WithEditor(string scenario, Action<TextEditorDialog, Task<bool?>> test)
                {
                    var dialog = new TextEditorDialog("pool", new Dictionary<string, bool>
                    {
                        ["ALPHA"] = true,
                        ["BRAVO"] = false
                    });
                    Task<bool?>? completion = null;
                    try
                    {
                        completion = dialog.ShowDialog<bool?>(owner!);
                        Dispatcher.UIThread.RunJobs();
                        test(dialog, completion);
                    }
                    catch (Exception ex)
                    {
                        check($"text editor {scenario}: fixture executes", false, ex.ToString());
                    }
                    finally
                    {
                        CloseEditor(scenario, dialog);
                        Dispatcher.UIThread.RunJobs();
                        // IsCompleted alone would also accept a faulted or cancelled modal, so the
                        // outcome is observed: successful completion, its result, and a closed window.
                        var faulted = completion?.Exception?.ToString();
                        check($"text editor {scenario}: modal task completed successfully and window closed",
                            completion is not null && completion.IsCompletedSuccessfully
                            && !dialog.IsVisible && OpenPrompt(dialog) is null,
                            $"status={completion?.Status.ToString() ?? "<never shown>"}, " +
                            $"result={(completion?.IsCompletedSuccessfully == true ? completion.Result?.ToString() ?? "<null>" : "<none>")}, " +
                            $"visible={dialog.IsVisible}, ownedOpen={OpenPrompt(dialog) is not null}" +
                            (faulted is null ? "" : $", fault={faulted}"));
                    }
                }

                // S1 - selected row fill. The WPF DataTrigger changes Background, BorderBrush and
                // BorderThickness together; the port must do the same through the .selected class.
                WithEditor("selection", (dialog, _) =>
                {
                    var surface = (dialog.FindResource("SurfaceBgBrush") as ISolidColorBrush)?.Color.ToUInt32();
                    var row = Row(dialog, 0);
                    check("text editor unselected row paints SurfaceBgBrush",
                        row is not null && surface is not null && Fill(row) == surface,
                        $"row={(row is null ? "<null>" : "found")}, fill={Fill(row)?.ToString("X8") ?? "<null>"}, surface={surface?.ToString("X8") ?? "<null>"}");

                    ((TextItem)dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<object>().First()).IsSelected = true;
                    Dispatcher.UIThread.RunJobs();
                    var selected = Row(dialog, 0);
                    check("text editor selected row takes the selected fill and border",
                        Fill(selected) == Color.Parse("#3A2A5A").ToUInt32()
                        && selected!.BorderThickness == new Thickness(1),
                        $"fill={Fill(selected)?.ToString("X8") ?? "<null>"}, expected={Color.Parse("#3A2A5A").ToUInt32():X8}, thickness={selected?.BorderThickness.ToString() ?? "<null>"}");

                    ((TextItem)dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<object>().First()).IsSelected = false;
                    Dispatcher.UIThread.RunJobs();
                    check("text editor deselected row returns to SurfaceBgBrush",
                        Fill(Row(dialog, 0)) == surface,
                        $"fill={Fill(Row(dialog, 0))?.ToString("X8") ?? "<null>"}, surface={surface?.ToString("X8") ?? "<null>"}");
                });

                // S2 - the confirmation captions must be localized. English captions would pass a
                // comparison against Loc.Get on an English UI even if they were hardcoded, so the
                // check runs under an actual non-English locale with known literals (fr: Oui/Non)
                // and the language is restored in finally.
                WithEditor("localized prompt", (dialog, _) =>
                {
                    var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
                    try
                    {
                        LocalizationManager.Instance.SetLanguage("fr");
                        Select(dialog, 0, true);
                        ClickButton(dialog.FindControl<Button>("BtnRemoveSelected")!);

                        var prompt = OpenPrompt(dialog);
                        var captions = prompt is null
                            ? new List<string>()
                            : PromptButtons(prompt).Select(b => b.Caption).ToList();
                        var shown = string.Join("|", captions);

                        check("text editor remove-selected prompt shows the French Yes/No captions",
                            captions.Count == 2 && captions[0] == "Oui" && captions[1] == "Non",
                            $"captions={shown}, language={LocalizationManager.Instance.CurrentLanguage}");
                        check("text editor remove-selected prompt captions come from btn_yes/btn_no, not raw keys",
                            captions.Count == 2
                            && captions[0] == Loc.Get("btn_yes") && captions[1] == Loc.Get("btn_no")
                            && captions[0] != "btn_yes" && captions[1] != "btn_no",
                            $"captions={shown}, btn_yes={Loc.Get("btn_yes")}, btn_no={Loc.Get("btn_no")}");

                        // Resolve the known prompt through its own negative button, the action a
                        // user has; nothing is hidden and no answer-everything helper is used.
                        if (prompt is not null)
                        {
                            var buttons = PromptButtons(prompt);
                            if (buttons.Count == 2) ClickButton(buttons[1].Button);
                        }
                        check("text editor remove-selected prompt answered No keeps both rows",
                            OpenPrompt(dialog) is null && Rows(dialog).Count == 2,
                            $"open={OpenPrompt(dialog) is not null}, rows={Rows(dialog).Count}");
                    }
                    finally
                    {
                        LocalizationManager.Instance.SetLanguage(previousLanguage);
                    }
                });

                // S3 - keyboard defaults and modal outcomes on the real prompts.
                // Enter = the first button at every call site, which is what the frozen WPF head
                // does (no call passes a defaultResult, so Win32 defaults to the first button -
                // Yes on the removals, OK on the notices). Escape on a two-answer prompt resolves
                // to No: that is the owner-requested safe dismissal, NOT claimed WPF parity
                // (Win32 ignores Escape on YesNo), and it is outcome-identical to the window-X
                // dismissal the port already allows. The three-answer close prompt keeps all three
                // answers, with Escape = Cancel (keep editing), which is WPF parity.
                WithEditor("remove prompt Enter", (dialog, _) =>
                {
                    Select(dialog, 0, true);
                    ClickButton(dialog.FindControl<Button>("BtnRemoveSelected")!);
                    if (OpenPrompt(dialog) is Window prompt) PressKey(prompt, PhysicalKey.Enter);
                    check("text editor Enter on the remove-selected prompt answers Yes and removes the row",
                        OpenPrompt(dialog) is null && Rows(dialog).Count == 1
                        && Rows(dialog)[0].Text == "BRAVO",
                        $"open={OpenPrompt(dialog) is not null}, rows={string.Join(",", Rows(dialog).Select(r => r.Text))}");
                    AnswerLeftoverPrompt(dialog, 1, 2);
                });

                WithEditor("remove prompt Escape", (dialog, _) =>
                {
                    Select(dialog, 0, true);
                    ClickButton(dialog.FindControl<Button>("BtnRemoveSelected")!);
                    if (OpenPrompt(dialog) is Window prompt) PressKey(prompt, PhysicalKey.Escape);
                    check("text editor Escape on the remove-selected prompt dismisses it and removes nothing",
                        OpenPrompt(dialog) is null && Rows(dialog).Count == 2,
                        $"open={OpenPrompt(dialog) is not null}, rows={string.Join(",", Rows(dialog).Select(r => r.Text))}");
                    AnswerLeftoverPrompt(dialog, 1, 2);
                });

                WithEditor("notice prompt Enter", (dialog, _) =>
                {
                    ClickButton(dialog.FindControl<Button>("BtnRemoveSelected")!); // nothing selected
                    var prompt = OpenPrompt(dialog);
                    var single = prompt is not null && PromptButtons(prompt).Count == 1;
                    if (prompt is not null) PressKey(prompt, PhysicalKey.Enter);
                    check("text editor Enter closes the OK-only no-selection notice",
                        single && OpenPrompt(dialog) is null,
                        $"okOnly={single}, open={OpenPrompt(dialog) is not null}");
                    AnswerLeftoverPrompt(dialog, 0, 1);
                });

                WithEditor("unsaved close prompt", (dialog, completion) =>
                {
                    Rows(dialog)[0].IsEnabled = false; // a real edit: TextItem_Changed sets _hasChanges
                    Dispatcher.UIThread.RunJobs();

                    dialog.Close();
                    Dispatcher.UIThread.RunJobs();
                    var prompt = OpenPrompt(dialog);
                    var captions = prompt is null ? new List<string>() : PromptButtons(prompt).Select(b => b.Caption).ToList();
                    check("text editor closing with unsaved edits offers all three answers",
                        captions.Count == 3 && captions[0] == Loc.Get("btn_yes")
                        && captions[1] == Loc.Get("btn_no") && captions[2] == Loc.Get("btn_cancel"),
                        $"captions={string.Join("|", captions)}");

                    if (prompt is not null) PressKey(prompt, PhysicalKey.Escape);
                    check("text editor Escape on the unsaved-close prompt keeps the editor open with its edits",
                        OpenPrompt(dialog) is null && dialog.IsVisible && !completion.IsCompleted
                        && dialog.ResultData is null && Rows(dialog)[0].IsEnabled == false,
                        $"open={OpenPrompt(dialog) is not null}, visible={dialog.IsVisible}, completed={completion.IsCompleted}, " +
                        $"result={(dialog.ResultData is null ? "<null>" : "set")}, edit={Rows(dialog).FirstOrDefault()?.IsEnabled}");
                    AnswerLeftoverPrompt(dialog, 2, 3); // Cancel: keep editing

                    dialog.Close();
                    Dispatcher.UIThread.RunJobs();
                    if (OpenPrompt(dialog) is Window second) PressKey(second, PhysicalKey.Enter);
                    check("text editor Enter on the unsaved-close prompt saves and closes",
                        OpenPrompt(dialog) is null && !dialog.IsVisible
                        && dialog.ResultData is not null && dialog.ResultData!.Count == 2
                        && dialog.ResultData!["ALPHA"] == false,
                        $"open={OpenPrompt(dialog) is not null}, visible={dialog.IsVisible}, " +
                        $"result={(dialog.ResultData is null ? "<null>" : string.Join(",", dialog.ResultData.Select(kv => $"{kv.Key}={kv.Value}")))}");
                    AnswerLeftoverPrompt(dialog, 1, 3); // No: close without saving
                });
            }
            catch (Exception ex)
            {
                check("text editor fixture executes", false, ex.ToString());
            }
            finally
            {
                owner?.Close();
                if (owner is not null) Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
