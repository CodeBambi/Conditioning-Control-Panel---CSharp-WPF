using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Super Inner Bloom glue: when a bloom spawns, what a pop releases, and the clean sweep. The
/// maths is <see cref="InnerBloom"/>, the drawing is <see cref="BubbleLayer"/>. Blooms only ride
/// the compositor layer (the film has no WPF twin), and only while SuperAccess says so.
/// No reward here: every bubble pays through the one ordinary ambient pop path.
/// </summary>
public partial class BubbleService
{
    private sealed class BloomTrack
    {
        public BloomTrack(int released) { Sweep = new InnerBloom.Sweep(released); }
        public InnerBloom.Sweep Sweep { get; }
        public Point CenterPx;
        public double RadiusPx;
    }

    private sealed record PendingRelease(int Root, InnerBloom.Node Node, Point CenterDip, double RadiusDip,
                                         System.Windows.Forms.Screen Screen, long PoppedMs);

    private readonly Dictionary<int, BloomTrack> _blooms = new();
    private readonly List<PendingRelease> _bloomReleases = new();
    private int _bloomSeq;
    private bool _bloomHooked;

    /// <summary>About one plain ambient spawn in six, one bloom at a time, compositor only.</summary>
    private Bubble? TryCreateBloom(System.Windows.Forms.Screen screen, bool isClickable)
    {
        if (!Bubble.UseCompositor || !_ambientHost) return null;
        if (_blooms.Count > 0 || _bloomReleases.Count > 0) return null;
        if (!SuperAccess.IsOn(SuperEffect.InnerBloom) || !InnerBloom.RollSpawn(_random)) return null;
        if (!_bloomHooked) { _bloomHooked = true; SuperAccess.Changed += OnSuperChanged; }

        var plan = InnerBloom.Plan(_random);
        plan.Seed = _random.NextDouble() * Math.PI * 2;
        plan.ClockMs = Environment.TickCount64;
        var bloom = new Bubble(screen, _bubbleImage, _random, OnPop, OnMiss, OnDestroy, isClickable,
                               sizeMult: InnerBloom.BigSizeMult);
        int id = ++_bloomSeq;
        bloom.AttachBloom(plan, id);
        _blooms[id] = new BloomTrack(plan.ReleasedCount());
        _ = LoadBloomPicturesAsync(plan);
        return bloom;
    }

    /// <summary>Each leaf wears a picture from the player's own flash pool (consent rules live there).
    /// Local stills are decoded small for the faces inside; remote ones show film only until released.</summary>
    private static async Task LoadBloomPicturesAsync(InnerBloom.Node plan)
    {
        try
        {
            var leaves = plan.Leaves().ToList();
            var paths = await Task.Run(() => App.Flash?.GetChaosImagePaths(leaves.Count) ?? new List<string>());
            for (int i = 0; i < leaves.Count && i < paths.Count; i++)
            {
                var path = paths[i];
                leaves[i].PicturePath = path;
                if (FlashService.IsRemotePath(path)) continue;
                var still = await Task.Run(() => DecodeBloomStill(path));
                if (still != null) leaves[i].Picture = still;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Inner Bloom pictures: {E}", ex.Message); }
    }

    private static BitmapSource? DecodeBloomStill(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 160;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>A bloom member popped: count it for the sweep, and if it carried kids start its burst.</summary>
    private void OnBloomPopped(Bubble b)
    {
        if (b.BloomRoot == 0 || !_blooms.TryGetValue(b.BloomRoot, out var track)) return;
        if (b.IsBloomKid && track.Sweep.Popped() && MotionFx.Level != MotionLevel.Off)
            Bubble.Layer?.AddBloomClean(track.CenterPx.X, track.CenterPx.Y, track.RadiusPx, Loc.Get("super_bloom_clean"));
        if (b.Bloom is not { } node) return;

        var c = b.CenterDip;
        double rDip = b.SizeDip * InnerBloom.GlassOfSprite;
        if (node.IsRoot)
        {
            track.CenterPx = new Point(c.X * b.DpiScale, c.Y * b.DpiScale);
            track.RadiusPx = rDip * b.DpiScale;
        }
        b.HideForBloomBurst();
        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)(c.X * b.DpiScale), (int)(c.Y * b.DpiScale)));
        long now = Environment.TickCount64;
        _bloomReleases.Add(new PendingRelease(b.BloomRoot, node, c, rDip, screen, now));
        Bubble.Layer?.AddBloomBurst(node, c.X * b.DpiScale, c.Y * b.DpiScale, rDip * b.DpiScale, now,
                                    InnerBloom.Motion.For(MotionFx.Level));
    }

    /// <summary>Once a burst has squeezed (at once under Motion Off) its kids become real bubbles.</summary>
    private void StepBlooms()
    {
        if (_bloomReleases.Count == 0 && _blooms.Count == 0) return;
        long now = Environment.TickCount64;
        var m = InnerBloom.Motion.For(MotionFx.Level);
        for (int i = _bloomReleases.Count - 1; i >= 0; i--)
        {
            var p = _bloomReleases[i];
            if (!m.Still && now - p.PoppedMs < InnerBloom.SqueezeS * 1000) continue;
            _bloomReleases.RemoveAt(i);
            try { ReleaseKids(p, now, m); }
            catch (Exception ex) { App.Logger?.Debug("Inner Bloom release: {E}", ex.Message); }
        }
        // A bloom is over once nothing of it is alive and nothing waits to be released.
        if (_blooms.Count == 0) return;
        foreach (var id in _blooms.Keys.ToArray())
            if (!_bloomReleases.Any(r => r.Root == id) && !_bubbles.Any(b => b.BloomRoot == id && b.IsAlive))
                _blooms.Remove(id);
    }

    private void ReleaseKids(PendingRelease p, long now, InnerBloom.Motion m)
    {
        double t = p.Node.TimeAt(now);
        var settings = App.Settings.Current;
        bool clickable = App.IsSessionRunning ? settings.BubblesClickable : true;
        for (int j = 0; j < p.Node.Kids.Count; j++)
        {
            var kid = p.Node.Kids[j];
            var at = InnerBloom.Place(p.Node, j, t, p.RadiusDip, m);
            var kb = new Bubble(p.Screen, _bubbleImage, _random, OnPop, OnMiss, OnDestroy, clickable,
                                sizeDip: InnerBloom.ReleasedSizeDip(at.Radius));
            if (kid.IsCarrier)
            {
                kid.ClockMs = now;
                kb.AttachBloom(kid, p.Root);
            }
            kb.ReleaseFromBloom(p.Root, new Point(p.CenterDip.X + at.X, p.CenterDip.Y + at.Y), at.Angle,
                                kid.PicturePath, escaper: !p.Node.IsRoot, onEscaped: NoteBloomLost);
            _bubbles.Add(kb);
        }
    }

    /// <summary>A released bubble got away (floated off or escaped): no clean sweep for its bloom.</summary>
    private void NoteBloomLost(Bubble b)
    {
        if (b.BloomRoot != 0 && b.IsBloomKid && _blooms.TryGetValue(b.BloomRoot, out var track)) track.Sweep.Lose();
    }

    /// <summary>Drop every bloom: the panic key, Stop, a pause and a field clear all come through here.</summary>
    private void ClearBloomState()
    {
        _blooms.Clear();
        _bloomReleases.Clear();
        try { Bubble.Layer?.ClearBlooms(); } catch (Exception ex) { Diag.Swallowed(ex); }
    }

    /// <summary>The switch went off (or the tier lapsed): every bloom bubble leaves at once.</summary>
    private void OnSuperChanged(SuperEffect effect)
    {
        if (effect != SuperEffect.InnerBloom || SuperAccess.IsOn(effect)) return;
        DispatcherHelper.RunOnUI(() =>
        {
            foreach (var b in _bubbles.Where(b => b.BloomRoot != 0).ToArray())
            {
                try { b.ForceDestroy(); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
            ClearBloomState();
        });
    }

    private void UnhookBlooms()
    {
        if (!_bloomHooked) return;
        _bloomHooked = false;
        SuperAccess.Changed -= OnSuperChanged;
    }
}
