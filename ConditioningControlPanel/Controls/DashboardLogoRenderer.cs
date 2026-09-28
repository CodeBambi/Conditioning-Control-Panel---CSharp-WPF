using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace ConditioningControlPanel.Controls;

/// <summary>Native rendering of the approved layered logo, in its original 1024px coordinates.</summary>
internal sealed class DashboardLogoRenderer : IDisposable
{
    private const double Tau = Math.PI * 2;
    private readonly List<IDisposable> _owned = new();
    private readonly SKBitmap _art;
    private readonly Part[] _waves;
    private readonly Part _word, _arc;
    private readonly Badge[] _badges;
    private readonly SKPaint _image, _mask, _spark;
    private readonly SKImageFilter _waveBlur, _badgeBlur, _arcBlur, _sparkBlur;
    private readonly SKShader _waveSweep, _arcSweep, _sheen;
    private readonly SKRoundRect _frame;
    private bool _disposed;

    public DashboardLogoRenderer(Stream artwork)
    {
        try
        {
            using var decoded = SKBitmap.Decode(artwork) ?? throw new InvalidDataException("Logo artwork could not be decoded.");
            _art = Own(new SKBitmap(1024, 1024, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(_art))
                canvas.DrawBitmap(decoded, new SKRect(0, 0, 1024, 1024));
            _image = Own(new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium });
            _mask = Own(new SKPaint { BlendMode = SKBlendMode.DstIn });
            _spark = Own(new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f });
            _waveBlur = Own(SKImageFilter.CreateBlur(5, 5));
            _badgeBlur = Own(SKImageFilter.CreateBlur(8, 8));
            _arcBlur = Own(SKImageFilter.CreateBlur(9, 9));
            _sparkBlur = Own(SKImageFilter.CreateBlur(7, 7));
            _frame = Own(new SKRoundRect(new SKRect(0, 0, 1024, 1024), 83));
            _waves = new[] { Crop(12, 400, 222, 207, 12), Crop(790, 400, 222, 207, 12) };
            _word = Crop(239, 786, 550, 180, 10);
            _badges = new[] { BadgeAt(279, 188, 94), BadgeAt(512, 113, 93), BadgeAt(749, 189, 94),
                BadgeAt(177, 708, 93), BadgeAt(848, 708, 93) };
            _arc = Crop(245, 244, 535, 490, predicate: (x, y) =>
            {
                var radius = Math.Sqrt((x - 512) * (x - 512) + (y - 503) * (y - 503));
                return radius > 209 && radius < 245;
            });
            _waveSweep = Own(SKShader.CreateLinearGradient(new SKPoint(-72, 0), new SKPoint(72, 0),
                new[] { SKColors.Transparent, SKColors.White, SKColors.Transparent }, new[] { 0f, .5f, 1f }, SKShaderTileMode.Clamp));
            _arcSweep = Own(SKShader.CreateSweepGradient(new SKPoint(267, 259),
                new[] { SKColors.White, new SKColor(255, 255, 255, 204), SKColors.Transparent, SKColors.Transparent, SKColors.White },
                new[] { 0f, .028f, .12f, .90f, 1f }));
            _sheen = Own(SKShader.CreateLinearGradient(new SKPoint(-85, 0), new SKPoint(85, 0),
                new[] { SKColors.Transparent, new SKColor(255, 211, 249), SKColors.Transparent },
                new[] { 0f, .5f, 1f }, SKShaderTileMode.Clamp));
        }
        catch { Dispose(); throw; }
    }

    public void Draw(SKCanvas canvas, int width, int height, double phase, double energy)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width <= 0 || height <= 0) return;
        phase = double.IsFinite(phase) ? phase % Tau : 0;
        if (phase < 0) phase += Tau;
        energy = double.IsFinite(energy) ? Math.Clamp(energy, 0, 1) : 0;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale(width / 1024f, height / 1024f);
        canvas.ClipRoundRect(_frame, SKClipOperation.Intersect, true);
        canvas.DrawBitmap(_art, 0, 0, _image);
        for (var i = 0; i < _waves.Length; i++) DrawWave(canvas, _waves[i], i, phase, energy);
        DrawWord(canvas, phase);
        DrawArc(canvas, phase, energy);
        DrawBadges(canvas, phase, energy);
        DrawSparks(canvas, phase, energy);
        DrawSheen(canvas, phase, energy);
        canvas.Restore();
    }

    private void DrawWave(SKCanvas canvas, Part part, int index, double phase, double energy)
    {
        part.WorkCanvas.Clear(SKColors.Transparent);
        part.LightCanvas.Clear(SKColors.Transparent);
        var power = 2.8 + 6.5 * energy;
        for (var x = 0; x < part.Width; x += 2)
        {
            var u = (double)x / part.Width;
            var envelope = Math.Pow(Math.Sin(Math.PI * u), 1.3);
            var dy = (float)(envelope * power * (Math.Sin(u * Tau * 1.7 - phase * 3 + index) * .7
                + Math.Sin(u * Tau * 3.1 + phase * 2) * .3));
            var source = new SKRect(x, 0, x + 2, part.Height);
            var target = new SKRect(x, dy, x + 2, part.Height + dy);
            part.WorkCanvas.DrawBitmap(part.Art, source, target, _image);
            part.LightCanvas.DrawBitmap(part.Glow, source, target, _image);
        }
        Feather(part);
        canvas.DrawBitmap(part.Work, part.X, part.Y, _image);
        var progress = (phase / Tau * 3 + index * .47) % 1;
        var center = (float)((index == 0 ? progress : 1 - progress) * part.Width);
        part.LightCanvas.Save();
        part.LightCanvas.Translate(center, 0);
        _mask.Shader = _waveSweep;
        part.LightCanvas.DrawPaint(_mask);
        _mask.Shader = null;
        part.LightCanvas.Restore();
        Glow(canvas, part.Light, part.X, part.Y, .28 + .8 * energy, .6, _waveBlur);
    }

    private void DrawWord(SKCanvas canvas, double phase)
    {
        var part = _word;
        part.WorkCanvas.Clear(SKColors.Transparent);
        var scale = (float)(1.006 + .019 * (1 - Math.Cos(phase)) * .5);
        part.WorkCanvas.Save();
        part.WorkCanvas.Translate(part.Width / 2f + (float)(2.5 * Math.Sin(phase)),
            part.Height / 2f + (float)(1.4 * Math.Sin(phase)));
        part.WorkCanvas.Scale(scale);
        part.WorkCanvas.DrawBitmap(part.Art, -part.Width / 2f, -part.Height / 2f, _image);
        part.WorkCanvas.Restore();
        Feather(part);
        canvas.DrawBitmap(part.Work, part.X, part.Y, _image);
    }

    private void DrawArc(SKCanvas canvas, double phase, double energy)
    {
        var part = _arc;
        part.WorkCanvas.Clear(SKColors.Transparent);
        part.WorkCanvas.DrawBitmap(part.Glow, 0, 0, _image);
        part.WorkCanvas.Save();
        part.WorkCanvas.RotateDegrees((float)((phase * 2 + .4) * 180 / Math.PI), 267, 259);
        _mask.Shader = _arcSweep;
        part.WorkCanvas.DrawPaint(_mask);
        _mask.Shader = null;
        part.WorkCanvas.Restore();
        Glow(canvas, part.Work, part.X, part.Y, .45 + .68 * energy, .55, _arcBlur);
    }

    private void DrawBadges(SKCanvas canvas, double phase, double energy)
    {
        for (var i = 0; i < _badges.Length; i++)
        {
            var part = _badges[i].Part;
            var pulse = i < 3 ? Periodic(phase * 2, i * .57, .36)
                : Periodic(phase * 3, i == 3 ? .45 : 3.75, .60);
            var strength = (i < 3 ? .22 : .46) + energy * .48;
            Glow(canvas, part.Glow, part.X, part.Y, pulse * strength, .55, _badgeBlur);
        }
    }

    private void Glow(SKCanvas canvas, SKBitmap bitmap, float x, float y, double alpha, double bloom, SKImageFilter blur)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        _image.BlendMode = SKBlendMode.Screen;
        _image.Color = SKColors.White.WithAlpha(Alpha(alpha));
        canvas.DrawBitmap(bitmap, x, y, _image);
        _image.Color = SKColors.White.WithAlpha(Alpha(alpha * bloom));
        _image.ImageFilter = blur;
        canvas.DrawBitmap(bitmap, x, y, _image);
        _image.ImageFilter = null;
        _image.Color = SKColors.White;
        _image.BlendMode = SKBlendMode.SrcOver;
    }

    private void DrawSparks(SKCanvas canvas, double phase, double energy)
    {
        var count = (int)Math.Round(6 + energy * 18);
        for (var i = 0; i < count; i++)
        {
            var right = i % 2 == 1;
            var life = (phase / Tau * 3 + i * .381966) % 1;
            var x = (right ? 803 : 225) + (right ? 1 : -1) * (life * 34 + i % 4 * 5);
            var y = 518 + Math.Sin(i * 7.1) * 23 + Math.Sin(life * Math.PI) * (right ? 1 : -1) * (12 + 20 * energy);
            var alpha = Math.Pow(Math.Sin(life * Math.PI), 4) * (.12 + .7 * energy);
            Star(canvas, x, y, 1.1 + energy * 1.8, alpha, right ? new SKColor(243, 153, 255) : new SKColor(255, 171, 201));
        }
        if (energy <= .02) return;
        for (var i = 0; i < _badges.Length; i++)
        {
            var badge = _badges[i];
            var angle = phase * (i % 2 == 1 ? 1 : -1) + i * 1.9;
            Star(canvas, badge.X + Math.Cos(angle) * (badge.Radius - 5), badge.Y + Math.Sin(angle) * (badge.Radius - 5),
                3.2, energy * Periodic(phase * 2, i * .8, .55), new SKColor(255, 231, 249));
        }
    }

    private void Star(SKCanvas canvas, double x, double y, double size, double alpha, SKColor color)
    {
        _spark.Color = color.WithAlpha(Alpha(alpha));
        for (var pass = 0; pass < 2; pass++)
        {
            _spark.ImageFilter = pass == 0 ? _sparkBlur : null;
            canvas.DrawLine((float)(x - size), (float)y, (float)(x + size), (float)y, _spark);
            canvas.DrawLine((float)x, (float)(y - size), (float)x, (float)(y + size), _spark);
        }
    }

    private void DrawSheen(SKCanvas canvas, double phase, double energy)
    {
        if (energy < .01) return;
        canvas.Save();
        canvas.Translate(123, 0);
        canvas.Skew(-.24f, 0);
        canvas.Translate((float)(-350 + phase / Tau * 1750), 0);
        _image.Shader = _sheen;
        _image.BlendMode = SKBlendMode.Screen;
        _image.Color = SKColors.White.WithAlpha(Alpha(.10 * energy));
        canvas.DrawRect(-85, 0, 170, 1024, _image);
        _image.Shader = null;
        _image.Color = SKColors.White;
        _image.BlendMode = SKBlendMode.SrcOver;
        canvas.Restore();
    }

    private Part Crop(int x, int y, int width, int height, int feather = 0, Func<int, int, bool>? predicate = null)
    {
        var art = Own(new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(art)) canvas.DrawBitmap(_art, -x, -y);
        var pixels = art.Pixels;
        for (var j = 0; j < height; j++)
            for (var i = 0; i < width; i++)
            {
                var color = pixels[j * width + i];
                var hot = Math.Max(color.Red, color.Blue);
                var alpha = Smooth((hot - 75) / 135.0) * Smooth((hot - color.Green - 24) / 65.0);
                pixels[j * width + i] = color.WithAlpha(predicate == null || predicate(x + i, y + j) ? Alpha(alpha) : (byte)0);
            }
        var glow = Own(new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        glow.Pixels = pixels;
        var work = Own(new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var light = Own(new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        return new Part(x, y, width, height, art, glow, work, light, Own(new SKCanvas(work)), Own(new SKCanvas(light)),
            feather == 0 ? null : FeatherShader(width, 0, feather), feather == 0 ? null : FeatherShader(0, height, feather));
    }

    private Badge BadgeAt(int x, int y, int radius) => new(x, y, radius,
        Crop(x - radius - 14, y - radius - 14, radius * 2 + 28, radius * 2 + 28, predicate: (px, py) =>
            (px - x) * (px - x) + (py - y) * (py - y) < (radius + 8) * (radius + 8)));

    private SKShader FeatherShader(int width, int height, int edge)
    {
        var fraction = (float)edge / Math.Max(width, height);
        return Own(SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
            new[] { SKColors.Transparent, SKColors.White, SKColors.White, SKColors.Transparent },
            new[] { 0f, fraction, 1 - fraction, 1f }, SKShaderTileMode.Clamp));
    }

    private void Feather(Part part)
    {
        _mask.Shader = part.HorizontalFeather;
        part.WorkCanvas.DrawPaint(_mask);
        _mask.Shader = part.VerticalFeather;
        part.WorkCanvas.DrawPaint(_mask);
        _mask.Shader = null;
    }

    private T Own<T>(T item) where T : IDisposable { _owned.Add(item); return item; }
    private static byte Alpha(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    private static double Smooth(double value) { value = Math.Clamp(value, 0, 1); return value * value * (3 - 2 * value); }
    private static double Periodic(double phase, double center, double width) =>
        Math.Exp(-Math.Pow(Math.Atan2(Math.Sin(phase - center), Math.Cos(phase - center)) / width, 2));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }

    private sealed record Badge(int X, int Y, int Radius, Part Part);
    private sealed record Part(int X, int Y, int Width, int Height, SKBitmap Art, SKBitmap Glow,
        SKBitmap Work, SKBitmap Light, SKCanvas WorkCanvas, SKCanvas LightCanvas,
        SKShader? HorizontalFeather, SKShader? VerticalFeather);
}