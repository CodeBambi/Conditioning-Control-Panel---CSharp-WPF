using System;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// WPF CommunityPromptService.ActivatePrompt's advisory: the validator flagged some fields of a
/// community prompt. Non-blocking (posted, never awaited by the activation), one OK button.
/// </summary>
internal static class CommunityPromptAdvisory
{
    internal static void Show(int flaggedFields)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var owner = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner == null) return;
                await MessageDialog.ShowAsync(owner, Loc.Get("community_prompt_warning_title"), Loc.GetF("community_prompt_warning_body", flaggedFields));
            }
            catch (Exception ex) { Log.Debug("Community prompt advisory failed: {Error}", ex.Message); }
        });
    }
}
