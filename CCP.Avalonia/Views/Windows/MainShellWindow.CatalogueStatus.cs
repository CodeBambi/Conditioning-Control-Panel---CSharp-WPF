// PORTED from the READ side of ConditioningControlPanel/MainWindow/MainWindow.CatalogueSubmissions.cs
// (CheckCatalogueSubmissionStatusesAsync, NotifyCatalogueSubmissionAccepted, ResolveCatalogueDisplayName),
// MainWindow.DeeperSubmissions.cs (CheckDeeperSubmissionStatusesAsync, NotifyDeeperSubmissionAccepted)
// and MainWindow.PresetIO.cs (CreateCatalogueStatusBadge, RefreshCatalogueShareBadges): the /mine
// status polls and the share-status pill. Recording a NEW submission (the write side) is U3's.
//
// Deviation: WPF's accepted toast is ShowSticky (persists until dismissed, remembered in
// DismissedNotificationKeys). This head's NotificationService has no sticky toast, so it is a
// 15 s toast; the record's AcceptedNotified flag still makes it once-only.
// ponytail: add ShowSticky to Helpers/NotificationService when a second caller needs it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const string CatalogueKindDeeper = "deeper";   // throttle key only, not a route
        private static readonly TimeSpan CatalogueCheckThrottle = TimeSpan.FromSeconds(90);
        private readonly Dictionary<string, DateTime> _lastCatalogueCheckUtc = new();
        private readonly HashSet<string> _catalogueChecksInFlight = new();

        /// <summary>Startup (force) and tab-open (throttled) polls, WPF MainWindow.xaml.cs:3531-3535.</summary>
        private void PollCatalogueStatuses(bool force)
        {
            _ = CheckDeeperSubmissionStatusesAsync(force);
            _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindPresets, force);
            _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindSessions, force);
            _ = CheckCatalogueSubmissionStatusesAsync(CatalogueKindMods, force);
        }

        /// <summary>WPF CheckCatalogueSubmissionStatusesAsync: GET /api/catalogue/{kind}/mine, update
        /// the records, fire a one-time accepted toast, repaint the badges.</summary>
        internal Task CheckCatalogueSubmissionStatusesAsync(string kind, bool force = false) =>
            PollAsync(kind, GetCatalogueDict(kind), force,
                () => App.Catalogue.FetchMyCatalogueAssetsAsync(kind, default),
                (id, key) => App.Notifications.Show(
                    Loc.GetF("catalogue_submission_accepted_toast_fmt", ResolveCatalogueDisplayName(kind, key)),
                    Helpers.NotificationType.Success, TimeSpan.FromSeconds(15)),
                () => RefreshCatalogueShareBadges(kind));

        /// <summary>WPF CheckDeeperSubmissionStatusesAsync: GET /api/enhancements/mine; a changed status
        /// repaints the library rows' badges.</summary>
        internal Task CheckDeeperSubmissionStatusesAsync(bool force = false) =>
            PollAsync(CatalogueKindDeeper, CoreSettings.Current.DeeperSubmissions, force,
                () => App.Catalogue.FetchMySubmissionsAsync(default),
                (id, key) => App.Notifications.Show(
                    Loc.GetF("deeper_submission_accepted_toast_fmt", Path.GetFileNameWithoutExtension(key)),
                    Helpers.NotificationType.Success, TimeSpan.FromSeconds(15),
                    Loc.Get("deeper_submission_accepted_action_view"), () => ShowTab("deeper")),
                RefreshDeeperLibraryRows);

        // The two WPF polls are the same loop over a different dictionary and endpoint.
        private async Task PollAsync(string kind, Dictionary<string, DeeperSubmissionRecord>? dict, bool force,
            Func<Task<Dictionary<string, string>?>> fetch, Action<string, string> notifyAccepted, Action repaint)
        {
            try
            {
                if (string.IsNullOrEmpty(CoreSettings.Current.AuthToken)) return;
                if (dict == null || dict.Count == 0) return;
                if (_catalogueChecksInFlight.Contains(kind)) return;
                if (!dict.Values.Any(r => !IsCatalogueAcceptedStatus(r.Status) || !r.AcceptedNotified)) return;
                if (!force && _lastCatalogueCheckUtc.TryGetValue(kind, out var last)
                    && DateTime.UtcNow - last < CatalogueCheckThrottle) return;

                _catalogueChecksInFlight.Add(kind);
                _lastCatalogueCheckUtc[kind] = DateTime.UtcNow;

                var statuses = await fetch();
                if (statuses == null) return;

                bool changed = false;
                foreach (var (key, rec) in dict)
                {
                    if (rec == null || string.IsNullOrEmpty(rec.CatalogueId)) continue;
                    if (!statuses.TryGetValue(rec.CatalogueId, out var serverStatus) || string.IsNullOrEmpty(serverStatus))
                        continue;

                    rec.LastCheckedUtc = DateTime.UtcNow;
                    if (!string.Equals(rec.Status, serverStatus, StringComparison.OrdinalIgnoreCase))
                    {
                        rec.Status = serverStatus;
                        changed = true;
                    }
                    if (IsCatalogueAcceptedStatus(serverStatus) && !rec.AcceptedNotified)
                    {
                        rec.AcceptedNotified = true;
                        changed = true;
                        notifyAccepted(rec.CatalogueId, key);
                    }
                }

                if (changed)
                {
                    CoreSettings.Save();
                    repaint();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[Catalogue] status poll {Kind} failed: {Error}", kind, ex.Message);
            }
            finally
            {
                _catalogueChecksInFlight.Remove(kind);
            }
        }

        /// <summary>Session file name, the preset's name, or the mod's name (WPF ResolveCatalogueDisplayName).</summary>
        private static string ResolveCatalogueDisplayName(string kind, string key)
        {
            try
            {
                if (kind == CatalogueKindSessions)
                {
                    var fileName = Path.GetFileNameWithoutExtension(key);
                    return fileName.EndsWith(".session", StringComparison.OrdinalIgnoreCase) ? fileName[..^8] : fileName;
                }
                if (kind == CatalogueKindPresets)
                {
                    var preset = CoreSettings.Current.UserPresets?.FirstOrDefault(p => p.Id == key);
                    if (!string.IsNullOrEmpty(preset?.Name)) return preset.Name;
                }
                if (kind == CatalogueKindMods && CoreMods.InstalledMods.TryGetValue(key, out var mod)
                    && !string.IsNullOrEmpty(mod.Name)) return mod.Name;
            }
            catch { }
            return key;
        }

        /// <summary>WPF RefreshCatalogueShareBadges: the preset detail pill and the session rack
        /// repaint; the mod manager rebuilds its own badges whenever its list refreshes.</summary>
        private void RefreshCatalogueShareBadges(string kind)
        {
            if (kind is CatalogueKindPresets or CatalogueKindSessions)
                Named<Tabs.PresetsTabView>("PresetsTab")?.RefreshCatalogueBadges();
        }

        /// <summary>WPF CreateCatalogueStatusBadge (MainWindow.PresetIO.cs:295): a small status pill, or
        /// null when the asset was never shared. Static so dialogs can render it.</summary>
        internal static Border? CreateCatalogueStatusBadge(DeeperSubmissionRecord? rec)
        {
            if (rec == null || string.IsNullOrEmpty(rec.CatalogueId)) return null;

            var (glyph, label, bg, fg) =
                IsCatalogueAcceptedStatus(rec.Status) ? ("✅", "catalogue_status_approved", "#334CAF50", "#7BE08A")
                : string.Equals(rec.Status, "rejected", StringComparison.OrdinalIgnoreCase)
                    ? ("⚠", "catalogue_status_rejected", "#33FF6B6B", "#FF9B9B")
                    : ("⏳", "catalogue_status_pending", "#33FFB347", "#FFC97A");

            return new Border
            {
                Background = new SolidColorBrush(Color.Parse(bg)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{glyph} {Loc.Get(label)}",
                    Foreground = new SolidColorBrush(Color.Parse(fg)),
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                },
            };
        }
    }
}
