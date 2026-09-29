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
// THE WRITE HALF IS STILL OUT:
//   RecordCatalogueSubmission(kind, key, SubmissionResult)
//       Its persistence body is portable line for line, but its parameter type is not:
//       SubmissionResult was head-only; it is Core's since catalogue U1
//       (CCP.Core/Services/Catalogue/CatalogueClient.cs), so this is now unblocked (U3 wires it).
//       It also ends by calling RefreshCatalogueShareBadges (MainShellWindow.CatalogueStatus.cs).
//   (The status polls - CheckCatalogueSubmissionStatusesAsync, the accepted toast and the badge -
//   are in MainShellWindow.CatalogueStatus.cs.)
//
// Checked and NOT the blocker: CoreReleaseContent. It answers pack ids, install stamps and pack
// info; the catalogue submission flow reads none of those.

using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

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
    }
}
