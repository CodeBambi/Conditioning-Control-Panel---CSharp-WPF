using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Migrations
{
    /// <summary>
    /// TEMPORARY hotfix overlay for the Circe's Lock gender-neutral pass: patches the EXTRACTED
    /// <c>builtin_mods\builtin-locked\</c> tree in place, because the archive it came from does not
    /// refresh on its own.
    ///
    /// <para>That tree is unzipped from <c>locked-resources.ccpmod</c>, which reaches users inside the
    /// mod-locked content pack, and ModService only re-extracts when the pack stamp changes. A user who
    /// already holds the pack therefore keeps the old gendered flash voice lines and art indefinitely.
    /// The 29 replacement files ship in the installer under <c>LockedMod\overrides\</c> (same relative
    /// paths as inside the archive) and are copied over the tree every time it is ready.</para>
    ///
    /// <para>Deleting the 25 superseded voice lines is not optional: FlashService and the avatar's
    /// voice-line bubble ENUMERATE <c>flashes_audio</c> and print the file name as the line, so a
    /// shadowed old file would still be picked and still be shown. The old names are the keys of
    /// <see cref="CirceNeutralMigration.VoiceLineStemMap"/>, so there is one list, not two.</para>
    ///
    /// <para>Rules: a file is copied only when the target is missing or its length differs (write to
    /// a temp file, then one overwrite move), so a patched or freshly re-extracted tree costs a few
    /// dozen stat calls per launch. Nothing here throws; a locked file is logged, counted and retried
    /// on the next launch. Remove with the csproj <c>CirceNeutralInBoxOverride</c> block once the
    /// rebuilt pack has shipped.</para>
    /// </summary>
    internal static class CirceNeutralPackPatch
    {
        /// <summary>Where the replacement files ship, relative to the install dir.</summary>
        internal static readonly string OverridesRelativeDir = Path.Combine("LockedMod", "overrides");

        /// <summary>The voice-line folder inside the extracted tree.</summary>
        internal static readonly string FlashesAudioRelativeDir = Path.Combine("resources", "sounds", "flashes_audio");

        /// <summary>Suffix of the staging copy written next to a target before the overwrite move.</summary>
        private const string TempSuffix = ".ccp-patch.tmp";

        /// <summary><c>{exe}\LockedMod\overrides</c>.</summary>
        internal static string DefaultOverridesRoot => Path.Combine(AppContext.BaseDirectory, OverridesRelativeDir);

        /// <summary>File names of the superseded voice lines, deleted from <see cref="FlashesAudioRelativeDir"/>.</summary>
        internal static IEnumerable<string> ObsoleteVoiceLineFiles =>
            CirceNeutralMigration.VoiceLineStemMap.Keys.Select(stem => stem + ".mp3");

        /// <summary>File names of the replacement voice lines (what <see cref="ObsoleteVoiceLineFiles"/> became).</summary>
        internal static IEnumerable<string> ReplacementVoiceLineFiles =>
            CirceNeutralMigration.VoiceLineStemMap.Values.Select(stem => stem + ".mp3");

        /// <summary>What one run did. <see cref="ToString"/> is the single log line.</summary>
        internal sealed class Result
        {
            public int Copied { get; set; }
            public int Deleted { get; set; }
            public int Failed { get; set; }

            public bool ChangedAnything => Copied > 0 || Deleted > 0;

            public override string ToString() =>
                $"copied {Copied}, deleted {Deleted}, failed {Failed}";
        }

        /// <summary>
        /// Copies every file under <paramref name="overridesRoot"/> into the same relative path under
        /// <paramref name="extractDir"/> (when missing or a different length), then deletes each
        /// superseded voice line whose replacement is now in the tree. A missing tree or a missing
        /// overrides folder is a complete no-op. Never throws.
        /// </summary>
        internal static Result Apply(string extractDir, string overridesRoot)
        {
            var result = new Result();
            try
            {
                // Never create a tree that is not there: an empty builtin-locked\resources\ would read
                // as an extracted mod on the next launch.
                if (string.IsNullOrWhiteSpace(extractDir) || !Directory.Exists(extractDir)) return result;
                // No overrides (a build without them) means no replacements, and deleting the old
                // lines on their own would only take 25 voice lines away.
                if (string.IsNullOrWhiteSpace(overridesRoot) || !Directory.Exists(overridesRoot)) return result;

                CopyOverrides(extractDir, overridesRoot, result);
                DeleteObsolete(extractDir, result);

                if (result.ChangedAnything)
                    App.Logger?.Information("CirceNeutralPackPatch: patched {Dir}: {Result}", extractDir, result.ToString());
            }
            catch (Exception ex)
            {
                result.Failed++;
                App.Logger?.Warning("CirceNeutralPackPatch: failed for {Dir}: {Error}", extractDir, ex.Message);
            }
            return result;
        }

        private static void CopyOverrides(string extractDir, string overridesRoot, Result result)
        {
            string[] sources;
            try { sources = Directory.GetFiles(overridesRoot, "*", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                result.Failed++;
                App.Logger?.Warning("CirceNeutralPackPatch: could not enumerate {Dir}: {Error}", overridesRoot, ex.Message);
                return;
            }

            foreach (var source in sources)
            {
                string? temp = null;
                try
                {
                    var target = Path.Combine(extractDir, Path.GetRelativePath(overridesRoot, source));
                    var targetInfo = new FileInfo(target);
                    if (targetInfo.Exists && targetInfo.Length == new FileInfo(source).Length) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    temp = target + TempSuffix;
                    File.Copy(source, temp, overwrite: true);
                    File.Move(temp, target, overwrite: true);
                    temp = null;
                    result.Copied++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    App.Logger?.Warning("CirceNeutralPackPatch: could not copy {File}: {Error}", source, ex.Message);
                }
                finally
                {
                    if (temp != null)
                    {
                        try { File.Delete(temp); } catch { /* best effort; the next run overwrites it */ }
                    }
                }
            }
        }

        private static void DeleteObsolete(string extractDir, Result result)
        {
            var folder = Path.Combine(extractDir, FlashesAudioRelativeDir);
            if (!Directory.Exists(folder)) return;

            foreach (var (oldStem, newStem) in CirceNeutralMigration.VoiceLineStemMap)
            {
                var name = oldStem + ".mp3";
                try
                {
                    var path = Path.Combine(folder, name);
                    if (!File.Exists(path)) continue;
                    // Only once the replacement is in place (its copy may have failed this run): a
                    // gendered line that still plays beats a line that silently disappeared. The
                    // next launch retries both halves.
                    if (!File.Exists(Path.Combine(folder, newStem + ".mp3"))) continue;
                    File.Delete(path);
                    result.Deleted++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    App.Logger?.Warning("CirceNeutralPackPatch: could not delete {File}: {Error}", name, ex.Message);
                }
            }
        }
    }
}
