using System;
using System.IO;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services.Speech
{
    /// <summary>
    /// The folder the offline speech model goes in, and the one button that opens it.
    ///
    /// <para>Four people spent forty-minute support threads on this in one week, and every thread
    /// was the same shape: the hint says <c>Resources\Models\vosk</c>, the folder is under
    /// <c>%LOCALAPPDATA%\Programs\ConditioningControlPanel</c> where nothing else the app talks
    /// about lives, and on a fresh install it does not exist yet - so the instruction names a
    /// path the user cannot find and then cannot create with any confidence that it is the right
    /// one ("I cant find Resources under the main CCP folder ... and therefor i cannot find a
    /// readme", 2026-09-17).</para>
    ///
    /// <para><see cref="Ensure"/> creates it precisely so the button is never a dead end. That is
    /// the whole trick: an empty folder sitting open in Explorer is an unambiguous "drop it
    /// here", and <c>SpeechService.ResolveModelDir</c> already treats an empty folder exactly as
    /// it treats a missing one.</para>
    /// </summary>
    internal static class SpeechModelFolder
    {
        /// <summary>Where the app looks for a model. Same path the hints name.</summary>
        internal static string Root => SpeechService.ModelRoot;

        /// <summary>
        /// Make sure <paramref name="root"/> exists and hand it back, or null when it could not be
        /// created (a read-only install directory, which is a real deployment). Never throws.
        /// </summary>
        internal static string? Ensure(string? root)
        {
            if (string.IsNullOrWhiteSpace(root)) return null;
            try
            {
                Directory.CreateDirectory(root);
                return Directory.Exists(root) ? root : null;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SpeechModelFolder: could not create {Root}", root);
                return null;
            }
        }

        /// <summary>
        /// Open the models folder in Explorer, creating it first. False means neither happened,
        /// and the caller says so rather than looking like it did nothing.
        /// </summary>
        internal static bool Open()
        {
            var dir = Ensure(Root);
            if (dir == null) return false;
            return ExplorerLauncher.OpenFolder(dir);
        }
    }
}
