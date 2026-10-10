using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// One animated GIF (or animated WebP: SKCodec reads both), opened once and decoded frame by
    /// frame on demand. Replaces XamlAnimatedGif, the WPF-only package the tube's emote layers used.
    ///
    /// <para>Memory: the clip keeps only its compressed bytes (about 2 MB for an avatar0 clip) plus
    /// the codec. Frames are never cached as a list; a <see cref="GifPlayer"/> decodes the next one
    /// into its single framebuffer, so a playing layer costs one 192x309 bitmap, not ~100 of them.</para>
    /// </summary>
    internal sealed class GifClip : IDisposable
    {
        private readonly SKData _data;
        private readonly SKCodec _codec;
        private readonly int[] _durations;
        private readonly int[] _required;
        private readonly bool[] _restorePrevious;

        public int Width { get; }
        public int Height { get; }
        public int FrameCount => _durations.Length;
        /// <summary>Per-frame delay in ms, clamped as browsers and XamlAnimatedGif do (0-10 ms reads as 100).</summary>
        public ReadOnlySpan<int> FrameDurationsMs => _durations;
        public int TotalMs { get; }

        private GifClip(SKData data, SKCodec codec)
        {
            _data = data;
            _codec = codec;
            Width = codec.Info.Width;
            Height = codec.Info.Height;
            var info = codec.FrameInfo;
            if (info.Length == 0)
            {
                // A still image: one frame that holds for a beat.
                _durations = new[] { 100 };
                _required = new[] { -1 };
                _restorePrevious = new[] { false };
            }
            else
            {
                _durations = new int[info.Length];
                _required = new int[info.Length];
                _restorePrevious = new bool[info.Length];
                for (int i = 0; i < info.Length; i++)
                {
                    _durations[i] = info[i].Duration <= 10 ? 100 : info[i].Duration;
                    _required[i] = info[i].RequiredFrame;
                    _restorePrevious[i] = info[i].DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious;
                }
            }
            int total = 0;
            foreach (var d in _durations) total += d;
            TotalMs = total;
        }

        /// <summary>Opens a clip, or null when the stream is not a decodable image.</summary>
        public static GifClip? Open(Stream stream)
        {
            try
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var data = SKData.CreateCopy(ms.ToArray());
                var codec = SKCodec.Create(data);
                if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                {
                    codec?.Dispose();
                    data.Dispose();
                    return null;
                }
                return new GifClip(data, codec);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Decodes frame <paramref name="index"/> into a BGRA premultiplied buffer that currently
        /// holds frame <paramref name="bufferHolds"/> (-1 = nothing useful). GIF frames are deltas,
        /// so the buffer's previous frame is handed to the codec as the prior frame when the format
        /// allows it; otherwise the codec rebuilds the frame's dependencies itself.
        /// </summary>
        public bool DecodeInto(int index, int bufferHolds, IntPtr dst, int rowBytes, byte[] zeroRow)
        {
            if (index < 0 || index >= FrameCount) return false;
            var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            int prior = -1;
            if (_required[index] == -1 || _codec.FrameInfo.Length == 0)
            {
                // Independent frame: start from transparent.
                for (int y = 0; y < Height; y++)
                    Marshal.Copy(zeroRow, 0, dst + y * rowBytes, Math.Min(zeroRow.Length, rowBytes));
            }
            else if (bufferHolds == index - 1 && index > 0 && !_restorePrevious[index - 1]
                     && _required[index] <= index - 1)
            {
                prior = index - 1;
            }
            var opts = _codec.FrameInfo.Length == 0 ? new SKCodecOptions() : new SKCodecOptions(index, prior);
            var result = _codec.GetPixels(info, dst, rowBytes, opts);
            return result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput;
        }

        public void Dispose()
        {
            _codec.Dispose();
            _data.Dispose();
        }
    }

    /// <summary>
    /// Plays a <see cref="GifClip"/> ONCE into one WriteableBitmap (the tube's emote clips never
    /// loop: the controller crossfades to the next clip when one completes). Driven by
    /// <see cref="Advance"/> from the owner's frame clock; honours each frame's own delay.
    /// </summary>
    internal sealed class GifPlayer : IDisposable
    {
        private readonly GifClip _clip;
        private readonly byte[] _zeroRow;
        private double _accMs;
        private int _decoded = -1;

        public string Name { get; }
        public WriteableBitmap Bitmap { get; }
        public int FrameIndex { get; private set; }
        public bool IsComplete { get; private set; }
        public int FrameCount => _clip.FrameCount;

        public GifPlayer(string name, GifClip clip)
        {
            Name = name;
            _clip = clip;
            Bitmap = new WriteableBitmap(new PixelSize(clip.Width, clip.Height), new Vector(96, 96),
                                         PixelFormat.Bgra8888, AlphaFormat.Premul);
            _zeroRow = new byte[clip.Width * 4];
            Decode(0);
        }

        /// <summary>Moves the playhead on by <paramref name="elapsed"/>. True when the shown frame changed.</summary>
        public bool Advance(TimeSpan elapsed)
        {
            if (IsComplete) return false;
            _accMs += Math.Max(0, elapsed.TotalMilliseconds);
            int target = FrameIndex;
            var durations = _clip.FrameDurationsMs;
            while (_accMs >= durations[target])
            {
                _accMs -= durations[target];
                if (target + 1 >= _clip.FrameCount) { IsComplete = true; break; }
                target++;
            }
            if (target == FrameIndex) return false;
            // Walk every frame in between: each GIF frame is a delta on the one before.
            for (int i = FrameIndex + 1; i <= target; i++) Decode(i);
            FrameIndex = target;
            return true;
        }

        private void Decode(int index)
        {
            using var fb = Bitmap.Lock();
            if (!_clip.DecodeInto(index, _decoded, fb.Address, fb.RowBytes, _zeroRow)) _decoded = -1;
            else _decoded = index;
        }

        public void Dispose()
        {
            Bitmap.Dispose();
            _clip.Dispose();
        }
    }
}
