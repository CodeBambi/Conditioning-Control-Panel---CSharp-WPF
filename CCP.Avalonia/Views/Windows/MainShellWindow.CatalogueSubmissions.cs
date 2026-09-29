// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.CatalogueSubmissions.cs
// (223 lines). Sorted member by member against the fifteen Core seams; the file splits cleanly in
// two along the read/write line, and the READ half is restored below.
//
// THE READ HALF IS HERE AND IS REAL. Every one of its inputs is in Core: the record shape is
// CCP.Core/Models/DeeperSubmissionRecord.cs and the three per-kind dictionaries are fields of
// AppSettings (CataloguePresetSubmissions / CatalogueSessionSubmissions / CatalogueModSubmissions,
// AppSettings.cs:2347-2375), which CoreSettings.Current hands over. Nothing here touches a control,
// so the x:Name hazard does not apply to this partial at all.
//
// Callers: MainShellWindow.CatalogueStatus.cs (CreateCatalogueStatusBadge, the /mine polls), which
// paints the preset detail pill and session rack pill (PresetsTabView) and the Mod Manager row pill.
//
// THE WRITE HALF: RecordCatalogueSubmission is ported (catalogue U3). Its callers, the preset/session/
// mod Share buttons, are split out of U4 and not wired yet; whoever wires them repaints through
// RefreshCatalogueShareBadges (MainShellWindow.CatalogueStatus.cs) as WPF does. The status polls, accepted toast
// (15 s, not WPF's sticky one) and ResolveCatalogueDisplayName are in MainShellWindow.CatalogueStatus.cs.
//
// Checked and NOT the blocker: CoreReleaseContent. It answers pack ids, install stamps and pack
// info; the catalogue submission flow reads none of those.

using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Route segment / dictionary selector for user-shared presets.</summary>
        public const string CatalogueKindPresets = "presets";

        /// <summary>Route segment / dictionary selector for user-shared sessions.</summary>
        public const string CatalogueKindSessions = "sessions";

        /// <summary>Route segment / dictionary selector for user-shared mods.</summary>
        public const string CatalogueKindMods = "mods";

        /// <summary>The two server statuses that mean "it is in the catalogue". Anything else -
        /// pending, rejected, null - is still open.</summary>
        internal static bool IsCatalogueAcceptedStatus(string? status) =>
            string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "published", StringComparison.OrdinalIgnoreCase);

        /// <summary>Sessions are file-backed, so their submission key is the canonical full path.
        /// Presets and mods key by Id and do not go through here.</summary>
        internal static string CanonicalCataloguePathKey(string filePath)
        {
            try { return System.IO.Path.GetFullPath(filePath); }
            catch { return filePath; }
        }

        private static Dictionary<string, DeeperSubmissionRecord>? GetCatalogueDict(string kind)
        {
            // CoreSettings.Current is never null - with no head attached it is the shared default
            // instance, whose three dictionaries are empty. That is the honest "nothing shared yet"
            // answer, so the WPF null branch has nothing left to guard.
            var s = CoreSettings.Current;
            return kind switch
            {
                CatalogueKindPresets => s.CataloguePresetSubmissions,
                CatalogueKindSessions => s.CatalogueSessionSubmissions,
                CatalogueKindMods => s.CatalogueModSubmissions,
                _ => null,
            };
        }

        /// <summary>Current moderation status for one shared asset, or null if it was never
        /// submitted. Static so a dialog can render its badge without a shell reference - that is
        /// how ModManagerDialog uses it on WPF.</summary>
        internal static DeeperSubmissionRecord? GetCatalogueRecord(string kind, string key)
        {
            var dict = GetCatalogueDict(kind);
            if (dict == null || string.IsNullOrEmpty(key)) return null;
            dict.TryGetValue(key, out var rec);
            return rec;
        }

        /// <summary>WPF MainWindow.CatalogueSubmissions.cs:63: remember a share's server id/status. Only
        /// Success/Duplicate carry one; other outcomes are no-ops. WPF ends with RefreshCatalogueShareBadges
        /// (MainShellWindow.CatalogueStatus.cs, instance); the Share caller does that when it lands.</summary>
        internal static void RecordCatalogueSubmission(string kind, string key, SubmissionResult result)
        {
            try
            {
                if (ToRecordFields(result) is not var (id, status) || string.IsNullOrEmpty(key)) return;
                var dict = GetCatalogueDict(kind);
                if (dict == null) return;
                dict.TryGetValue(key, out var existing);
                dict[key] = UpdateRecord(existing, id, status);
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Debug("[Catalogue] RecordCatalogueSubmission failed: {Error}", ex.Message);
            }
        }

        private static (string Id, string Status)? ToRecordFields(SubmissionResult result)
        {
            var (id, status) = result switch
            {
                SubmissionResult.Success s => (s.Id, s.Status),
                SubmissionResult.Duplicate d => (d.ExistingId, d.ExistingStatus),
                _ => (null, null),
            };
            if (string.IsNullOrEmpty(id)) return null;
            return (id, string.IsNullOrEmpty(status) ? "pending" : status);
        }

        private static DeeperSubmissionRecord UpdateRecord(DeeperSubmissionRecord? existing, string id, string status)
        {
            var rec = existing ?? new DeeperSubmissionRecord { SubmittedUtc = DateTime.UtcNow };
            rec.CatalogueId = id;
            rec.Status = status;
            rec.LastCheckedUtc = DateTime.UtcNow;
            // A re-submit that reports it is already accepted must not fire a retroactive "published"
            // toast later - the duplicate toast already said so.
            if (IsCatalogueAcceptedStatus(status)) rec.AcceptedNotified = true;
            return rec;
        }
    }
}
