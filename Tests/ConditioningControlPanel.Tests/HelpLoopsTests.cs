using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.HelpLoops;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The drawn ? popover loops that replaced the dashboard tutorial clips: every dashboard feature
/// has one, every scene draws through its whole loop, the step chips are sane and localised, and
/// no help topic is left pointing at a clip that was deleted.
///
/// Set HELP_LOOPS_SHOTS to a folder to also dump one PNG per scene at its still frame.
/// </summary>
public sealed class HelpLoopsTests
{
    public static readonly string[] DashboardIds =
    {
        "FlashImages", "Video", "Subliminals", "BouncingText", "BubblePop",
        "LockCard", "SpiralOverlay", "PinkFilter", "BubbleCount", "MindWipe", "BrainDrain",
    };

    private static readonly string[] Languages =
        { "de", "en", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    public static TheoryData<string> Ids()
    {
        var d = new TheoryData<string>();
        // Every registered scene, not only the dashboard ones: wave 2 scenes get the same checks.
        foreach (var id in HelpLoopRegistry.Ids) d.Add(id);
        return d;
    }

    [Fact]
    public void Registry_HasALoopForEveryDashboardFeature()
    {
        var missing = DashboardIds.Where(id => !HelpLoopRegistry.Has(id)).ToList();
        Assert.True(missing.Count == 0, "no help loop for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Registry_HandsOutAFreshInstanceEachCall()
    {
        Assert.True(HelpLoopRegistry.TryGet("FlashImages", out var a));
        Assert.True(HelpLoopRegistry.TryGet("FlashImages", out var b));
        Assert.NotSame(a, b);
        Assert.False(HelpLoopRegistry.TryGet("NoSuchTopic", out _));
        Assert.False(HelpLoopRegistry.TryGet(null, out _));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Steps_AreOrderedAndInsideTheLoop(string id)
    {
        if (!HelpLoopRegistry.TryGet(id, out var scene)) return; // covered by the registry test
        Assert.Equal(id, scene.Id);
        Assert.True(scene.DurationMs > 0);
        Assert.InRange(scene.StillMs, 0, scene.DurationMs);
        Assert.Equal(3, scene.Steps.Count);
        double prev = 0;
        foreach (var s in scene.Steps)
        {
            Assert.True(s.StartMs < s.EndMs, $"{id}: step {s.LocKey} is empty");
            Assert.True(s.StartMs >= prev, $"{id}: step {s.LocKey} starts before the one before it ends");
            Assert.InRange(s.EndMs, 0, scene.DurationMs);
            prev = s.EndMs;
        }
    }

    [Fact]
    public void EveryStepKey_ExistsInAllNineLanguages()
    {
        var keys = HelpLoopRegistry.Ids
            .SelectMany(id => HelpLoopRegistry.TryGet(id, out var s) ? s.Steps.Select(x => x.LocKey) : Array.Empty<string>())
            .ToList();
        Assert.NotEmpty(keys);
        foreach (var lang in Languages)
        {
            var path = Path.Combine(AppDir(), "Localization", "Languages", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var missing = keys.Where(k => !doc.RootElement.TryGetProperty(k, out _)).ToList();
            Assert.True(missing.Count == 0, $"{lang}.json lacks: " + string.Join(", ", missing));
        }
    }

    [Fact]
    public void NoHelpTopic_PointsAtAMissingClip()
    {
        var dir = Path.Combine(AppDir(), "Resources", "tutorial_videos");
        var broken = HelpContentService.GetAllSectionIds()
            .Select(HelpContentService.GetContent)
            .Where(c => c.HasClip && !File.Exists(Path.Combine(dir, c.ClipFile!)))
            .Select(c => c.SectionId + " -> " + c.ClipFile)
            .ToList();
        Assert.True(broken.Count == 0, "clip missing: " + string.Join(", ", broken));
    }

    [Fact]
    public void DashboardTopics_NoLongerShipAClip()
    {
        var still = DashboardIds.Where(HelpContentService.HasContent)
            .Select(HelpContentService.GetContent)
            .Where(c => c.HasClip)
            .Select(c => c.SectionId)
            .ToList();
        Assert.True(still.Count == 0, "still on a clip: " + string.Join(", ", still));
    }

    [Fact]
    public void TopicsWithALoop_NoLongerShipAClip()
    {
        var still = HelpLoopRegistry.Ids.Where(HelpContentService.HasContent)
            .Select(HelpContentService.GetContent)
            .Where(c => c.HasClip)
            .Select(c => c.SectionId)
            .ToList();
        Assert.True(still.Count == 0, "has a loop and still names a clip: " + string.Join(", ", still));
    }

    [Theory]
    [InlineData("KeywordTriggers")]
    [InlineData("WebcamCalibration")]
    [InlineData("Modding")]
    [InlineData("SessionEditor")]
    public void RetiredPlaceholderClips_AreGone(string id)
    {
        Assert.True(HelpContentService.HasContent(id), id + " lost its help topic");
        Assert.False(HelpContentService.GetContent(id).HasClip, id + " still names a clip");
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Scene_DrawsEvery50msOfItsLoop(string id)
    {
        if (!HelpLoopRegistry.TryGet(id, out var scene)) return; // covered by the registry test
        WpfRenderHarness.OnStaThread(() =>
        {
            var back = new DrawingVisual();
            var front = new DrawingVisual();
            var palette = new LoopPalette();
            for (double t = 0; t < scene.DurationMs; t += 50)
            {
                using var b = back.RenderOpen();
                using var f = front.RenderOpen();
                var frame = new LoopFrame(b, f, palette);
                frame.Ground();
                scene.Draw(frame, t);
                Assert.True(frame.BackBlur >= 0 && !double.IsNaN(frame.BackBlur), $"{id}: bad blur at {t}");
            }
            scene.Reset();

            // One real render through the view, at the still frame, into a bitmap.
            var view = new HelpLoopView(scene);
            view.Measure(new Size(480, 270));
            view.Arrange(new Rect(0, 0, 480, 270));
            view.RenderAt(scene.StillMs);
            Assert.False(view.Failed, $"{id}: the view stopped on an exception");
            var rtb = new RenderTargetBitmap(480, 270, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(view);

            var pixels = new byte[480 * 270 * 4];
            rtb.CopyPixels(pixels, 480 * 4, 0);
            Assert.True(pixels.Where((_, i) => i % 4 == 3).Any(a => a > 0), $"{id}: rendered nothing");

            var shots = Environment.GetEnvironmentVariable("HELP_LOOPS_SHOTS");
            if (!string.IsNullOrEmpty(shots))
            {
                Directory.CreateDirectory(shots);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(Path.Combine(shots, id + ".png"));
                enc.Save(fs);
            }
        }, timeoutSeconds: 120);
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
