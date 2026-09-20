using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Deeper catalogue submission feedback (Mort's request): after the
    // fire-and-forget submit, remember each submission and surface when it gets
    // accepted/published — a status badge on the library row (built in
    // MainWindow.DeeperHub.cs) plus a one-time notification on the next status
    // poll. This partial owns the persistence + polling glue.
    public partial class MainWindow
    {
        // Don't hammer the endpoint when the Deeper tab is opened repeatedly in
        // one session. Startup always polls; tab-open polls at most this often.
        private DateTime _lastSubmissionCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan SubmissionCheckThrottle = TimeSpan.FromSeconds(90);
        private bool _submissionCheckInFlight;

        private static bool IsAcceptedStatus(string? status) =>
            string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "published", StringComparison.OrdinalIgnoreCase);

        private static string CanonicalSubmissionKey(string filePath)
        {
            try { return System.IO.Path.GetFullPath(filePath); }
            catch { return filePath; }
        }

        // Called after a submit attempt resolves. Persists the submission so the
        // app can track its status going forward. Only Success/Duplicate carry a
        // server id worth remembering; other outcomes are no-ops.
        private void RecordDeeperSubmission(string filePath, SubmissionResult result)
        {
            try
            {
                string id;
                string status;
                switch (result)
                {
                    case SubmissionResult.Success s:
                        id = s.Id;
                        status = string.IsNullOrEmpty(s.Status) ? "pending" : s.Status;
                        break;
                    case SubmissionResult.Duplicate d:
                        id = d.ExistingId;
                        status = string.IsNullOrEmpty(d.ExistingStatus) ? "pending" : d.ExistingStatus;
                        break;
                    default:
                        return;
                }

                if (string.IsNullOrEmpty(id)) return;
                var settings = App.Settings?.Current;
                if (settings == null) return;

                var key = CanonicalSubmissionKey(filePath);
                settings.DeeperSubmissions.TryGetValue(key, out var existing);
                var rec = existing ?? new DeeperSubmissionRecord { SubmittedUtc = DateTime.UtcNow };
                rec.CatalogueId = id;
                rec.Status = status;
                rec.LastCheckedUtc = DateTime.UtcNow;
                // If a re-submit reports the file is already accepted, suppress a
                // retroactive "published" toast — the duplicate toast already
                // told the user it's in the catalogue.
                if (IsAcceptedStatus(status)) rec.AcceptedNotified = true;

                settings.DeeperSubmissions[key] = rec;
                App.Settings?.Save();

                // Show the badge immediately without waiting for a poll/reload.
                ApplyDeeperFilterAndSort();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Catalogue] RecordDeeperSubmission failed: {Error}", ex.Message);
            }
        }

        // Polls the catalogue for status changes on tracked submissions and, on a
        // pending → accepted transition, fires a one-time notification. Safe to
        // call on startup and on Deeper-tab open; cheap no-op when there's
        // nothing pending or no auth. <paramref name="force"/> bypasses the
        // per-session throttle (used by the startup call).
        public async Task CheckDeeperSubmissionStatusesAsync(bool force = false)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null || App.Catalogue == null) return;
                if (string.IsNullOrEmpty(settings.AuthToken)) return;
                if (_submissionCheckInFlight) return;

                if (settings.DeeperSubmissions.Count == 0) return;
                // Nothing to learn if every tracked submission is already in a
                // terminal state we've notified for.
                bool anyOpen = settings.DeeperSubmissions.Values.Any(r =>
                    !IsAcceptedStatus(r.Status) || !r.AcceptedNotified);
                if (!anyOpen) return;

                if (!force && DateTime.UtcNow - _lastSubmissionCheckUtc < SubmissionCheckThrottle) return;

                _submissionCheckInFlight = true;
                _lastSubmissionCheckUtc = DateTime.UtcNow;

                var statuses = await App.Catalogue.FetchMySubmissionsAsync(default).ConfigureAwait(true);
                if (statuses == null) return;

                bool changed = false;
                foreach (var kvp in settings.DeeperSubmissions)
                {
                    var rec = kvp.Value;
                    if (rec == null || string.IsNullOrEmpty(rec.CatalogueId)) continue;
                    if (!statuses.TryGetValue(rec.CatalogueId, out var serverStatus) || string.IsNullOrEmpty(serverStatus))
                        continue;

                    rec.LastCheckedUtc = DateTime.UtcNow;

                    if (!string.Equals(rec.Status, serverStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        rec.Status = serverStatus;
                        changed = true;
                    }

                    if (IsAcceptedStatus(serverStatus) && !rec.AcceptedNotified)
                    {
                        rec.AcceptedNotified = true;
                        changed = true;
                        NotifyDeeperSubmissionAccepted(rec.CatalogueId, kvp.Key);
                    }
                }

                if (changed)
                {
                    App.Settings?.Save();
                    ApplyDeeperFilterAndSort();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Catalogue] CheckDeeperSubmissionStatuses failed: {Error}", ex.Message);
            }
            finally
            {
                _submissionCheckInFlight = false;
            }
        }

        // Sticky toast keyed by catalogue id (stable across file moves) so it
        // survives until the user sees/dismisses it — exactly the "notify next
        // time you start it up" behaviour. The View action jumps to the library.
        private void NotifyDeeperSubmissionAccepted(string catalogueId, string canonicalPath)
        {
            try
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(canonicalPath);
                try
                {
                    var match = _deeperAllEntries.FirstOrDefault(en =>
                        string.Equals(CanonicalSubmissionKey(en.FilePath), canonicalPath, StringComparison.OrdinalIgnoreCase));
                    if (match != null && !string.IsNullOrEmpty(match.Name)) name = match.Name;
                }
                catch (Exception ex) { Diag.Swallowed(ex); }

                var msg = Loc.GetF("deeper_submission_accepted_toast_fmt", name);
                App.Notifications?.ShowSticky(
                    "deeper_submission_accepted_" + catalogueId,
                    msg,
                    NotificationType.Success,
                    actionLabel: Loc.Get("deeper_submission_accepted_action_view"),
                    action: SwitchToDeeperLibraryTab);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Catalogue] NotifyDeeperSubmissionAccepted failed: {Error}", ex.Message);
            }
        }
    }
}
