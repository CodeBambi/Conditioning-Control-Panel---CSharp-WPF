using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// WPF MainWindow.ImportEnhancementFiles (MainWindow.DeeperTab.cs:775): copy .ccpenh.json files
    /// into the library folder, skipping duplicates and JSON that is not an enhancement, and say what
    /// happened in toasts. One door for the Import button and for a window drop.
    /// </summary>
    internal static class DeeperImport
    {
        internal enum Tone { Success, Warning, Info }

        /// <summary>Where the result toasts go (tests read them).</summary>
        internal static Action<string, Tone> Notify = (text, tone) =>
        {
            var type = tone switch
            {
                Tone.Success => Helpers.NotificationType.Success,
                Tone.Warning => Helpers.NotificationType.Warning,
                _ => Helpers.NotificationType.Info,
            };
            if (tone == Tone.Warning) App.Notifications.Show(text, type, TimeSpan.FromSeconds(10));
            else App.Notifications.Show(text, type);
        };

        /// <summary>WPF: DeeperLastDirectory, saved (tests swap it so no settings file is written).</summary>
        internal static Action<string> RememberDirectory = dir =>
        {
            if (CoreSettings.Current is not { } s) return;
            s.DeeperLastDirectory = dir;
            CoreSettings.Save();
        };

        /// <summary>The library row to bring into view afterwards: the last file written, else the
        /// row a duplicate already has. Null when nothing was imported or matched.</summary>
        internal static string? ImportFiles(IEnumerable<string> paths, string? libraryFolder = null)
        {
            var list = paths?.Where(p => !string.IsNullOrEmpty(p)).ToList() ?? new List<string>();
            var imported = new List<string>();
            var errors = new List<string>();
            int skippedNonEnhancement = 0;
            string? lastImportedPath = null;
            string? duplicateOfPath = null;

            foreach (var path in list)
            {
                var fileName = Path.GetFileName(path);
                if (!EnhancementImportRules.IsImportablePath(path))
                {
                    errors.Add(Loc.GetF("deeper_import_err_not_enh_fmt", fileName));
                    continue;
                }
                try
                {
                    // Plain .json that is not an enhancement (settings.json, a preset...) is
                    // skipped quietly rather than reported as a failure.
                    if (!EnhancementImportRules.FileLooksLikeEnhancement(path))
                    {
                        skippedNonEnhancement++;
                        continue;
                    }
                    // Same file, or identical content under another name: no "Name (2)" copy.
                    var existing = DeeperLocalLibrary.FindDuplicateOf(path, libraryFolder);
                    if (existing != null)
                    {
                        duplicateOfPath = existing;
                        continue;
                    }
                    // Validate by loading; bad schema / oversized files throw with a useful message.
                    var enhancement = EnhancementSerializer.LoadFromFile(path);
                    var saved = DeeperLocalLibrary.PromoteToLibrary(enhancement, "import", libraryFolder);
                    if (saved == null)
                    {
                        errors.Add(Loc.GetF("deeper_import_err_write_fmt", fileName));
                        continue;
                    }
                    lastImportedPath = saved;
                    imported.Add(Path.GetFileName(saved));
                }
                catch (EnhancementLoadException ex)
                {
                    errors.Add($"{fileName}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ImportEnhancementFiles: import failed");
                    errors.Add($"{fileName}: {ex.GetType().Name}");
                }
            }

            // Remember the source folder so the next manual import opens where this one picked from.
            if (lastImportedPath != null && list.Count > 0)
            {
                try
                {
                    var dir = Path.GetDirectoryName(list[0]);
                    if (!string.IsNullOrEmpty(dir)) RememberDirectory(dir);
                }
                catch (Exception ex) { Log.Debug(ex, "ImportEnhancementFiles: remember directory"); }
            }

            if (imported.Count > 0)
            {
                Notify(imported.Count == 1
                    ? Loc.GetF("deeper_import_done_one_fmt", imported[0])
                    : Loc.GetF("deeper_import_done_many_fmt", imported.Count), Tone.Success);
            }
            if (errors.Count > 0)
            {
                Notify(Loc.GetF("deeper_import_failed_fmt", errors.Count) + "\n" + string.Join("\n", errors), Tone.Warning);
            }
            else if (imported.Count == 0)
            {
                if (duplicateOfPath != null) Notify(Loc.Get("deeper_import_already_in_library"), Tone.Info);
                else if (skippedNonEnhancement > 0) Notify(Loc.Get("deeper_import_skipped_not_enh"), Tone.Info);
            }

            return lastImportedPath ?? duplicateOfPath;
        }
    }
}
