using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// Startup probe that keeps a BROKEN font install from turning every navigation into a blank
    /// screen.
    ///
    /// <para><b>The incident (v6.8.6).</b> A user's Cascadia install was missing/corrupt. Every
    /// panel he opened rendered blank, and the dispatcher took an
    /// <c>UnauthorizedAccessException</c> at
    /// <c>MS.Internal.Text.TextInterface.FontFamily.GetFirstMatchingFont</c> ->
    /// <c>Typeface.ConstructCachedTypeface</c> -> <c>TextBlock.MeasureOverride</c>, inside
    /// <c>ContextLayoutManager.UpdateLayout</c> off <c>HwndTarget.OnResize</c>. Because it throws
    /// from the LAYOUT pass it re-throws on every measure, so the UI never finishes a frame and
    /// crash.log grew to roughly half a gigabyte in one session. Reinstalling the fonts fixed it;
    /// the app had no guard.</para>
    ///
    /// <para><b>Why a comma fallback chain is not enough.</b> Every Cascadia site in this app
    /// ALREADY named a chain (<c>"Cascadia Mono, Consolas, Courier New"</c>) when that crash
    /// happened. A chain only covers a family that is ABSENT: WPF walks to the next link when a
    /// name does not resolve. It does not cover a family that is PRESENT but unreadable - to decide
    /// whether the first link matches, WPF has to open the font file, and that open is what throws.
    /// The throw happens before the chain is ever walked. So the chain is worth having (it is the
    /// cheap half of the fix, and it does handle the uninstalled-font case) but the only thing that
    /// helps a corrupt face is to STOP NAMING IT.</para>
    ///
    /// <para><b>What this does.</b> Once, at startup, on the UI thread: resolve each risky family
    /// exactly the way the layout pass would (build a <see cref="Typeface"/> and ask for its glyph
    /// typeface, which is what calls <c>GetFirstMatchingFont</c>). Any family whose probe throws is
    /// recorded as broken, logged ONE time, and struck out of every chain we hand to WPF from then
    /// on - including the app-level <c>Font.Mono</c> resource the markup binds with
    /// <c>DynamicResource</c>. A corrupt Cascadia now costs one warning line and a slightly
    /// different monospace face instead of an unusable app.</para>
    ///
    /// <para>Probing is cheap (a handful of families, each a single typeface construction) and
    /// deliberately covers only the faces we NAME OURSELVES that a stock Windows install may not
    /// have in good order. User-picked fonts go through
    /// <see cref="Helpers.FontPickerHelper.Resolve"/>, which has its own guard.</para>
    /// </summary>
    public static class FontGuard
    {
        /// <summary>The monospace chain the app uses for code/telemetry readouts.</summary>
        public const string MonoChain = "Cascadia Mono, Consolas, Courier New";

        /// <summary>Resource key the markup binds so a broken face can be swapped out at runtime.</summary>
        public const string MonoResourceKey = "Font.Mono";

        /// <summary>
        /// Families worth probing: ones we hardcode that are NOT guaranteed present and healthy.
        /// "Segoe UI" is deliberately absent - it is the floor everything else falls back to, and
        /// a machine whose Segoe UI is corrupt cannot render a WPF window at all.
        /// </summary>
        private static readonly string[] ProbedFamilies =
        {
            "Cascadia Mono",
            "Consolas",
            "Courier New",
            "Segoe MDL2 Assets",
            "Segoe UI Emoji",
        };

        private static readonly HashSet<string> _broken = new(StringComparer.OrdinalIgnoreCase);
        private static bool _verified;

        /// <summary>Families whose probe threw this session. Empty on a healthy machine.</summary>
        public static IReadOnlyCollection<string> BrokenFamilies => _broken;

        /// <summary>True when <paramref name="family"/> failed its startup probe.</summary>
        public static bool IsBroken(string? family) =>
            !string.IsNullOrWhiteSpace(family) && _broken.Contains(family!.Trim());

        /// <summary>
        /// The guarded monospace family - <see cref="MonoChain"/> with any broken link removed.
        /// Code-behind that used to write <c>new FontFamily("Cascadia Mono, Consolas, Courier New")</c>
        /// should use this instead so it follows the same verdict as the markup.
        /// </summary>
        public static FontFamily Mono => Family(MonoChain);

        /// <summary>
        /// A <see cref="FontFamily"/> for <paramref name="chain"/> with broken links struck out.
        /// Never throws: a failure here would defeat the point, so the hard floor is Segoe UI.
        /// </summary>
        public static FontFamily Family(string chain)
        {
            try { return new FontFamily(Sanitize(chain)); }
            catch { return new FontFamily("Segoe UI"); }
        }

        /// <summary>
        /// Drops every family that failed its probe from a comma-separated chain. Returns the
        /// chain unchanged on a healthy machine (the overwhelmingly common case), and never
        /// returns empty - if every link is broken the caller still gets Segoe UI to name.
        /// </summary>
        public static string Sanitize(string? chain)
        {
            if (string.IsNullOrWhiteSpace(chain)) return "Segoe UI";
            if (_broken.Count == 0) return chain!;
            return FilterChain(chain!, IsBroken);
        }

        /// <summary>
        /// The pure string half of <see cref="Sanitize"/>, split out so it is testable without a
        /// WPF dispatcher or a deliberately corrupted font install.
        ///
        /// <para>Links are compared by their trimmed text. A pack-relative link
        /// (<c>"./#Press Start 2P"</c>) never matches an installed family name, so bundled faces
        /// pass through untouched - which is right: they ship with the app and cannot be the
        /// machine's broken system font.</para>
        /// </summary>
        public static string FilterChain(string chain, Func<string, bool> isBroken)
        {
            if (string.IsNullOrWhiteSpace(chain)) return "Segoe UI";
            if (isBroken == null) return chain;

            var kept = chain
                .Split(',')
                .Select(link => link.Trim())
                .Where(link => link.Length > 0 && !isBroken(link))
                .ToList();

            return kept.Count > 0 ? string.Join(", ", kept) : "Segoe UI";
        }

        /// <summary>
        /// Probe every risky family once and publish the app-level font resources. Safe to call
        /// twice (the second call is a no-op) and safe to call before any window exists.
        /// </summary>
        public static void Verify()
        {
            if (_verified) return;
            _verified = true;

            foreach (var family in ProbedFamilies)
            {
                if (CanResolve(family, out var error)) continue;

                _broken.Add(family);
                // ONE line per broken family. Deliberately a warning, not an error: the app is
                // still fully usable, and this is the line that turns "the whole app is blank"
                // into a two-minute diagnosis (reinstall the font).
                App.Logger?.Warning(
                    "[FONTGUARD] Font family {Family} is installed but unreadable ({Error}) - dropping it from every font chain for this session. The app will use its fallback face. Reinstalling that font restores it.",
                    family, error);
            }

            PublishResources();
        }

        /// <summary>
        /// Puts the guarded chains into <c>Application.Current.Resources</c>. Markup binds these
        /// with <c>DynamicResource</c>, so swapping the value here re-renders every consumer -
        /// which is what lets a broken face be corrected without touching a single call site.
        /// </summary>
        private static void PublishResources()
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var mono = Sanitize(MonoChain);
                app.Resources[MonoResourceKey] = new FontFamily(mono);

                if (_broken.Count > 0)
                    App.Logger?.Information("[FONTGUARD] {Key} resolved to \"{Chain}\"", MonoResourceKey, mono);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("[FONTGUARD] could not publish font resources: {Msg}", ex.Message);
            }
        }

        /// <summary>
        /// Resolve <paramref name="family"/> the same way a layout pass would. Building the
        /// <see cref="Typeface"/> is not enough on its own - it is
        /// <see cref="Typeface.TryGetGlyphTypeface"/> that reaches
        /// <c>GetFirstMatchingFont</c> and opens the font file, which is exactly where the v6.8.6
        /// crash came from. <see cref="FontFamily.LineSpacing"/> is touched too because the
        /// measure pass reads it.
        /// </summary>
        private static bool CanResolve(string family, out string error)
        {
            error = "";
            try
            {
                var ff = new FontFamily(family);
                var typeface = new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                typeface.TryGetGlyphTypeface(out _);
                _ = ff.LineSpacing;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
