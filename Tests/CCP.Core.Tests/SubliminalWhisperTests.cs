using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// SubliminalWhisper against WPF SubliminalService: the 90% Reset roll, the 4-8 s / 1-2 s gaps, the
// (sub x master)^1.5 volume curve and FindLinkedAudio's order (mod folder, then shared Bambi clips
// only for mods that may borrow them, else the neutral voice).
public sealed class SubliminalWhisperTests
{
    [Fact]
    public void Rules_match_wpf()
    {
        Assert.True(SubliminalWhisper.ResetFollows(0.90));
        Assert.False(SubliminalWhisper.ResetFollows(0.9001));
        var r = new Random(1);
        for (int i = 0; i < 200; i++)
        {
            Assert.InRange(SubliminalWhisper.ResetDelayMs(r), 4000, 7999);
            Assert.InRange(SubliminalWhisper.DeferredResetDelayMs(r), 1000, 1999);
        }
        Assert.Equal(Math.Pow(0.25, 1.5), SubliminalWhisper.Volume(50, 50), 5);
        Assert.Equal(300, SubliminalWhisper.HapticLeadMs + SubliminalWhisper.VisualAfterHapticMs);
        Assert.True(SubliminalWhisper.IsFreezePhrase("bambi FREEZE"));
    }

    // NTFS (Windows CI) is case-insensitive, so the first name variant already hits and comes back in
    // the variant's spelling; on Linux the file's own spelling is exact.
    private static bool PathEq(string expected, string? actual) =>
        string.Equals(expected, actual, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void Same(string expected, string? actual) =>
        Assert.True(PathEq(expected, actual), $"expected {expected}, got {actual ?? "null"}");

    [Fact]
    public void FindLinkedAudio_mod_then_shared_else_neutral()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var mod = Directory.CreateDirectory(Path.Combine(root, "mod", "resources", "sounds", "flashes_audio")).FullName;
            var shared = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            File.WriteAllText(Path.Combine(shared, "BAMBI FREEZE.mp3"), "");
            File.WriteAllText(Path.Combine(shared, "Good Girl.WAV"), "");

            Assert.Equal(mod, SubliminalWhisper.ModAudioDir(Path.Combine(root, "mod")));
            Assert.Null(SubliminalWhisper.ModAudioDir(shared));

            // Shared Bambi clips: case variants, then the case-insensitive pass.
            Same(Path.Combine(shared, "BAMBI FREEZE.mp3"),
                SubliminalWhisper.FindLinkedAudio("Bambi Freeze", null, shared, BuiltInMods.BambiSleepId));
            Same(Path.Combine(shared, "Good Girl.WAV"),
                SubliminalWhisper.FindLinkedAudio("GOOD girl", null, shared, BuiltInMods.BambiSleepId));

            // The mod's own clip wins.
            File.WriteAllText(Path.Combine(mod, "bambi freeze.ogg"), "");
            Same(Path.Combine(mod, "bambi freeze.ogg"),
                SubliminalWhisper.FindLinkedAudio("Bambi Freeze", mod, shared, BuiltInMods.BambiSleepId));

            // CCP Default never borrows the Bambi voice.
            Assert.False(PathEq(Path.Combine(shared, "BAMBI FREEZE.mp3"),
                SubliminalWhisper.FindLinkedAudio("Bambi Freeze", null, shared, BuiltInMods.CCPDefaultId)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Manifest_keeps_safe_names_only()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{\"words\":{\"Let Go!\":\"let-go.mp3\",\"bad\":\"../x.mp3\",\"FREEZE\":\"freeze.mp3\"}}");
            var map = SubliminalWhisper.ReadManifest(file);
            Assert.Equal("let-go.mp3", map["let go"]);
            Assert.Equal("freeze.mp3", map["freeze"]);
            Assert.False(map.ContainsKey("bad"));
        }
        finally { File.Delete(file); }
    }
}
