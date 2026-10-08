using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Fx;
using SkiaSharp;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>One place the Premium page's motes rise from: a card or a group sign, in the
    /// canvas's own coordinates. <paramref name="Diamond"/> = cyan Prime diamonds, otherwise round
    /// gold glitter.</summary>
    public readonly record struct VaultZone(Rect Bounds, Color Color, bool Diamond);

    public partial class AmbientFxCanvas
    {
        private struct VMote
        {
            public float X, Y, VX, VY, Life, Max, SizePx, Phase, PhaseSpd, Spin;
            public uint Rgb;
            public bool Diamond;
        }

        private VMote[] _vault = Array.Empty<VMote>();
        private int _vaultN;
        private float _vaultT;
        private VaultZone[] _vaultZones = Array.Empty<VaultZone>();
        private readonly Dictionary<uint, SKColorFilter> _vaultTints = new();
        private readonly SKPaint _vaultShape = new() { IsAntialias = true, BlendMode = SKBlendMode.Plus, Style = SKPaintStyle.Fill };
        private readonly SKPath _vaultPath = new();

        /// <summary>Motes alive right now (tests).</summary>
        internal int VaultMoteCount => _vaultN;

        private bool VaultLayer => (_config.Layers & AmbientFxLayers.VaultMotes) != 0;

        /// <summary>Where the motes rise from. The page calls this after layout and on scroll, so the
        /// gold stays near the Basic cards and the cyan near the Prime ones.</summary>
        public void SetVaultZones(IReadOnlyList<VaultZone> zones)
        {
            var copy = new VaultZone[zones?.Count ?? 0];
            for (int i = 0; i < copy.Length; i++) copy[i] = zones![i];
            _vaultZones = copy;
        }

        /// <summary>The Reduced level may tick for this layer alone (the page starts no other layer then).</summary>
        private bool ReducedVaultMayRun() =>
            VaultLayer && Env.Level == MotionLevel.Reduced
            && Env.AllowAmbientMotion(_tier) && _particleBudget > 0;

        internal void StepVault(float dt)
        {
            if (!VaultLayer)
            {
                _vaultN = 0;
                return;
            }
            var level = Env.Level;
            int target = _fogOnly ? 0 : VaultMoteMath.Target(level, _liveBudget);
            if (_vault.Length < VaultMoteMath.Cap(MotionLevel.Full)) _vault = new VMote[VaultMoteMath.Cap(MotionLevel.Full)];

            float speed = (float)VaultMoteMath.SpeedScale(level);
            for (int i = _vaultN - 1; i >= 0; i--)
            {
                var m = _vault[i];
                m.X += m.VX * dt * speed;
                m.Y += m.VY * dt * speed;
                m.Phase += m.PhaseSpd * dt;
                m.Life -= dt;
                if (m.Life <= 0f || m.Y < -0.05f) _vault[i] = _vault[--_vaultN];
                else _vault[i] = m;
            }
            if (_vaultN > target) _vaultN = target;

            double every = 1.0 / Math.Max(0.001, VaultMoteMath.SpawnPerSecond(level));
            _vaultT = _vaultN >= target ? Math.Min(_vaultT + dt, (float)every) : _vaultT + dt;
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 1 || h <= 1) return;
            while (_vaultN < target && _vaultT > every)
            {
                _vaultT -= (float)every;
                SpawnVaultMote(w, h);
            }
        }

        private static readonly Color[] LooseHues =
        {
            Color.FromRgb(0xFF, 0xD2, 0x7A),   // gold
            Color.FromRgb(0x8C, 0xF0, 0xFF),   // ice cyan
            Color.FromRgb(0xD8, 0xC6, 0xFF),   // lilac
        };

        private void SpawnVaultMote(double w, double h)
        {
            Point at;
            Color c;
            bool diamond;
            if (_vaultZones.Length > 0 && _rng.NextDouble() < VaultMoteMath.ZoneShare)
            {
                var z = _vaultZones[_rng.Next(_vaultZones.Length)];
                var b = z.Bounds;
                var p = VaultMoteMath.SpawnOnRing(new RectD(b.X, b.Y, b.Width, b.Height), _rng.NextDouble(), _rng.NextDouble(), _rng.NextDouble());
                at = new Point(p.X, p.Y);
                c = z.Color;
                diamond = z.Diamond;
            }
            else
            {
                at = new Point(_rng.NextDouble() * w, h * (0.15 + _rng.NextDouble() * 0.85));
                int k = _rng.Next(LooseHues.Length);
                c = LooseHues[k];
                diamond = k == 1;
            }
            if (at.X < 0 || at.X > w || at.Y < 0 || at.Y > h) return;

            float life = (float)(VaultMoteMath.LifeMin + _rng.NextDouble() * (VaultMoteMath.LifeMax - VaultMoteMath.LifeMin));
            _vault[_vaultN++] = new VMote
            {
                X = (float)(at.X / w),
                Y = (float)(at.Y / h),
                VX = (float)((_rng.NextDouble() - 0.5) * 0.012),
                VY = -(float)(0.012 + _rng.NextDouble() * 0.030),
                Life = life, Max = life,
                SizePx = (float)(diamond ? 3.0 + _rng.NextDouble() * 3.5 : 2.2 + _rng.NextDouble() * 3.0),
                Phase = (float)(_rng.NextDouble() * Math.PI * 2),
                PhaseSpd = (float)(3.0 + _rng.NextDouble() * 5.0),
                Spin = (float)(_rng.NextDouble() * 0.6 - 0.3),
                Rgb = (uint)((c.R << 16) | (c.G << 8) | c.B),
                Diamond = diamond,
            };
        }

        private SKColorFilter VaultTint(uint rgb)
        {
            if (_vaultTints.TryGetValue(rgb, out var f)) return f;
            f = SKColorFilter.CreateBlendMode(new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), SKBlendMode.Modulate);
            _vaultTints[rgb] = f;
            return f;
        }

        private void DrawVault(SKCanvas canvas, float w, float h)
        {
            if (_vaultN == 0) return;
            // Skia draws in device pixels; the motes were sized in element units.
            float scale = Bounds.Width > 1 ? (float)(w / Bounds.Width) : 1f;
            for (int i = 0; i < _vaultN; i++)
            {
                var m = _vault[i];
                float env = (float)Math.Sin(Math.PI * Math.Clamp(1.0 - m.Life / m.Max, 0.0, 1.0));
                float twinkle = 0.55f + 0.45f * (float)Math.Sin(m.Phase);
                float a = env * twinkle;
                if (a <= 0.01f) continue;
                float x = m.X * w, y = m.Y * h, s = m.SizePx * scale;

                // A soft halo under every mote, in its own colour.
                _paint.ColorFilter = VaultTint(m.Rgb);
                _paint.Color = SKColors.White.WithAlpha(Alpha(0.42f * a));
                DrawSprite(canvas, FxSprites.Dot, x, y, s * 4.2f, s * 4.2f);
                _paint.ColorFilter = null;

                var core = new SKColor((byte)(m.Rgb >> 16), (byte)(m.Rgb >> 8), (byte)m.Rgb, Alpha(0.95f * a));
                _vaultShape.Color = core;
                if (m.Diamond)
                {
                    // A cut diamond: a tall rhombus, tilted a little and turning slowly.
                    float tilt = m.Spin * m.Phase * 0.15f;
                    float cos = (float)Math.Cos(tilt), sin = (float)Math.Sin(tilt);
                    float hw = s * 0.62f, hh = s;
                    _vaultPath.Reset();
                    _vaultPath.MoveTo(x - sin * hh, y - cos * hh);
                    _vaultPath.LineTo(x + cos * hw, y - sin * hw);
                    _vaultPath.LineTo(x + sin * hh, y + cos * hh);
                    _vaultPath.LineTo(x - cos * hw, y + sin * hw);
                    _vaultPath.Close();
                    canvas.DrawPath(_vaultPath, _vaultShape);
                }
                else
                {
                    canvas.DrawCircle(x, y, s * 0.45f, _vaultShape);
                }

                // At the top of its twinkle a mote throws a four-point glint.
                if (twinkle > 0.93f)
                {
                    float g = s * (m.Diamond ? 2.6f : 2.0f);
                    _vaultShape.Color = SKColors.White.WithAlpha(Alpha(0.75f * a));
                    _vaultShape.Style = SKPaintStyle.Stroke;
                    _vaultShape.StrokeWidth = Math.Max(1f, s * 0.22f);
                    canvas.DrawLine(x - g, y, x + g, y, _vaultShape);
                    canvas.DrawLine(x, y - g, x, y + g, _vaultShape);
                    _vaultShape.Style = SKPaintStyle.Fill;
                }
            }
        }
    }
}
