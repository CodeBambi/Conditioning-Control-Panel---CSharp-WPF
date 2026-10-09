// PORTED from ConditioningControlPanel/MainWindow/MainWindow.DeeperTab.cs:1224-1345
// (SubmitDeeperLibraryEntryAsync + ShowCatalogueSubmissionResultToast) and
// MainWindow.DeeperSubmissions.cs:36-80 (RecordDeeperSubmission). The client is Core CatalogueClient
// (App.Catalogue), so every head submits through the same code.
//
// The status side lives in MainShellWindow.CatalogueStatus.cs: CheckDeeperSubmissionStatusesAsync
// (the /api/enhancements/mine poll) and the one-time accepted toast with a View action (15 s, not
// WPF's sticky one). The library row's badge: DeeperTabViewModel.ResolveSubmissionBadge.
// WPF's IsAcceptedStatus / CanonicalSubmissionKey copies are the catalogue pair one file over
// (byte-identical bodies).

using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF MainWindow.DeeperTab.cs:1217: only video enhancements with a Hypnotube URL.</summary>
        internal static bool IsCatalogueEligible(EnhancementLibraryEntry? entry) =>
            entry != null && entry.MediaType == Models.Deeper.MediaTypes.Video
            && ConditioningControlPanel.Helpers.HtUrlHelper.IsEligibleHtUrl(entry.MediaSource);

        /// <summary>WPF MainWindow.DeeperTab.cs:1227: affirmation modal, Core submit, record, toast.</summary>
        internal async Task SubmitDeeperLibraryEntryAsync(EnhancementLibraryEntry entry)
        {
            // Defense in depth: the row button is disabled without a token, but it can expire meanwhile.
            if (string.IsNullOrEmpty(CoreSettings.Current.AuthToken))
            {
                App.Notifications.Show(Loc.Get("catalogue_toast_auth_failed"),
                    NotificationType.Warning, TimeSpan.FromSeconds(8));
                return;
            }

            var label = string.IsNullOrEmpty(entry.Name) ? System.IO.Path.GetFileName(entry.FilePath) : entry.Name;
            var dialog = new CatalogueSubmitDialog(label);
            if (!await dialog.ShowDialogSafe<bool>(this) || !dialog.Confirmed) return;
            ShowCatalogueSubmissionResultToast(await SubmitDeeperEntryAsync(entry.FilePath));
            RefreshDeeperLibraryRows();   // WPF RecordDeeperSubmission: show the badge at once
        }

        /// <summary>The Core submit plus the record, without UI.</summary>
        internal static async Task<SubmissionResult> SubmitDeeperEntryAsync(string filePath)
        {
            SubmissionResult result;
            try { result = await App.Catalogue.SubmitEnhancementAsync(filePath, default); }
            catch (Exception ex)
            {
                // The client is designed never to throw; surface anything that escapes as UnknownError.
                Log.Warning(ex, "[Catalogue] Submit threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }
            RecordDeeperSubmission(filePath, result);
            return result;
        }

        /// <summary>Persist a Success/Duplicate so its status can be tracked; other outcomes are no-ops.</summary>
        internal static void RecordDeeperSubmission(string filePath, SubmissionResult result)
        {
            try
            {
                if (ToRecordFields(result) is not var (id, status)) return;
                var settings = CoreSettings.Current;
                var key = CanonicalCataloguePathKey(filePath);
                settings.DeeperSubmissions.TryGetValue(key, out var existing);
                settings.DeeperSubmissions[key] = UpdateRecord(existing, id, status);
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Debug("[Catalogue] RecordDeeperSubmission failed: {Error}", ex.Message);
            }
        }

        /// <summary>WPF MainWindow.DeeperTab.cs:1265: one localized toast per SubmissionResult.</summary>
        internal static void ShowCatalogueSubmissionResultToast(SubmissionResult result)
        {
            var (msg, type, secs) = DescribeSubmissionResult(result);
            if (result is SubmissionResult.UnknownError u)
                Log.Warning("[Catalogue] Submission UnknownError status={Status} body={Body}", u.StatusCode, u.Body);
            App.Notifications.Show(msg, type, TimeSpan.FromSeconds(secs));
        }

        internal static (string Message, NotificationType Type, int Seconds) DescribeSubmissionResult(SubmissionResult result)
        {
            switch (result)
            {
                case SubmissionResult.Success:
                    return (Loc.Get("catalogue_toast_success"), NotificationType.Success, 6);
                case SubmissionResult.Duplicate d:
                    return (Loc.Get(d.ExistingStatus switch
                    {
                        "approved" => "catalogue_toast_duplicate_approved",
                        "rejected" => "catalogue_toast_duplicate_rejected",
                        _ => "catalogue_toast_duplicate_pending",
                    }), NotificationType.Info, 6);
                case SubmissionResult.ValidationError v:
                {
                    var key = v.ErrorCode switch
                    {
                        "missing_title" => "catalogue_toast_error_missing_title",
                        "missing_creator" => "catalogue_toast_error_missing_creator",
                        "invalid_media_source" => "catalogue_toast_error_invalid_media_source",
                        "invalid_schema" => "catalogue_toast_error_invalid_schema",
                        "file_too_large" => "catalogue_toast_error_file_too_large",
                        "stale_guidelines_version" => "catalogue_toast_error_stale_guidelines",
                        _ => "",
                    };
                    return (key.Length > 0 ? Loc.Get(key) : Loc.GetF("catalogue_toast_error_generic_fmt", v.ErrorCode),
                        NotificationType.Warning, 8);
                }
                case SubmissionResult.AuthFailed:
                    return (Loc.Get("catalogue_toast_auth_failed"), NotificationType.Warning, 10);
                case SubmissionResult.TooLarge:
                    return (Loc.Get("catalogue_toast_too_large"), NotificationType.Error, 8);
                case SubmissionResult.RateLimited r:
                    return (r.RetryAfterSeconds is > 0
                        ? Loc.GetF("catalogue_toast_rate_limited_minutes_fmt", Math.Max(1, (int)Math.Ceiling(r.RetryAfterSeconds.Value / 60.0)))
                        : Loc.Get("catalogue_toast_rate_limited_unknown"), NotificationType.Warning, 10);
                default:
                    return (Loc.Get("catalogue_toast_unknown_error"), NotificationType.Error, 8);
            }
        }
    }
}
