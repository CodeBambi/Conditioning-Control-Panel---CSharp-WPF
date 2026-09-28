using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>
    /// #1307: AI Effect Control turned itself off on some restarts. An entitlement read that came
    /// back false (a failed Patreon check, a Discord or SubscribeStar patron whose source had not
    /// answered yet) cleared and SAVED the switch. The switch is now only the user's choice, and the
    /// tier gate sits at the point of use.
    /// </summary>
    public class AiEffectControlGateTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public void Effective_only_with_the_switch_and_lab_access(bool saved, bool lab, bool expected)
        {
            Assert.Equal(expected, AiEffectControlGate.IsOn(saved, lab));
            Assert.Equal(expected, AiEffectControlGate.IsOn(new CompanionPromptSettings { AllowAiToControlEffects = saved }, lab));
        }

        [Fact]
        public void Null_settings_is_off()
        {
            Assert.False(AiEffectControlGate.IsOn((CompanionPromptSettings?)null, true));
        }

        [Fact]
        public void A_locked_read_never_changes_the_saved_choice()
        {
            var s = new CompanionPromptSettings { AllowAiToControlEffects = true };

            Assert.False(AiEffectControlGate.IsOn(s, labAccess: false));
            Assert.True(s.AllowAiToControlEffects);

            // Tier 2 confirms later in the same launch: the choice is still there.
            Assert.True(AiEffectControlGate.IsOn(s, labAccess: true));
        }

        /// <summary>
        /// Source guard. The only production code allowed to write the switch is the user's own
        /// click handler (ChkCapEffects_Changed) and the settings model itself. A repair that clears
        /// it from an entitlement read is exactly the bug.
        /// </summary>
        [Fact]
        public void Nothing_but_the_click_handler_writes_the_switch()
        {
            var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
            var write = new Regex(@"\bAllowAiToControlEffects\s*=(?!=)");
            var offenders = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                         && !f.EndsWith("CompanionPromptSettings.cs", StringComparison.Ordinal))
                .SelectMany(f => File.ReadAllLines(f)
                    .Select((line, i) => (f, i, line))
                    .Where(t => write.IsMatch(t.line) && !t.line.TrimStart().StartsWith("//")))
                .Select(t => $"{Path.GetFileName(t.f)}:{t.i + 1}: {t.line.Trim()}")
                .ToList();

            Assert.Equal(new[] { "MainWindow.Patreon.cs" },
                offenders.Select(o => o.Split(':')[0]).Distinct().ToArray());
            Assert.Single(offenders);
            Assert.Contains("= on;", offenders[0]);
        }

        [Fact]
        public void Every_effect_reader_goes_through_the_gate()
        {
            // Any mention of the property in code (not a comment) is a read, except the gate itself,
            // the settings model (declaration, defaults, Clone) and the click handler's own write.
            // A narrower pattern (== true / != true) let `if (cp.AllowAiToControlEffects)` through.
            var mention = new Regex(@"\bAllowAiToControlEffects\b");
            var clickWrite = new Regex(@"\bAllowAiToControlEffects\s*=\s*on\s*;");
            // Every product root (WPF, Core, Avalonia, VR): the gate itself now lives in Core.
            var offenders = SourceRoots.EnumerateProductSources("*.cs")
                .Where(f => !f.EndsWith("AiEffectControlGate.cs", StringComparison.Ordinal)
                         && !f.EndsWith("CompanionPromptSettings.cs", StringComparison.Ordinal))
                .SelectMany(f => File.ReadAllLines(f)
                    .Select((line, i) => (f, i, line))
                    .Where(t => mention.IsMatch(t.line)
                             && !t.line.TrimStart().StartsWith("//")
                             && !clickWrite.IsMatch(t.line)))
                .Select(t => $"{Path.GetFileName(t.f)}:{t.i + 1}: {t.line.Trim()}")
                .ToList();
            Assert.Empty(offenders);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
            return dir!.FullName;
        }
    }
}
