using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Video;

/// <summary>
/// The Windows decoder: the operating system's own Media Foundation source reader.
///
/// <para><b>What it proves and what it does not.</b> It proves the OS opened a container, found a
/// video stream, agreed to convert it to BGRX, and handed pictures back. That is the FILE being
/// readable. <b>It is not evidence that anything reached a screen</b> and no caller may treat it as
/// such — see <see cref="IVideoPresence"/>, which is the half that asks the OS about a surface, and
/// which is the whole reason the two halves are separate types.</para>
///
/// <para><b>Startup is process-wide and deliberately never shut down.</b> <c>MFStartup</c> and
/// <c>MFShutdown</c> are a matched pair per process; this class starts the platform once, on first
/// use, and never calls the other half. A shutdown from one presence would pull the platform out
/// from under another clip on another thread, and the process is exiting anyway.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MediaFoundationClipSource : IVideoClipSource
{
    private static readonly object StartupGate = new();
    private static int _startupResult = int.MinValue;

    /// <inheritdoc/>
    public bool MediaStackUsable => OperatingSystem.IsWindows() && EnsureStarted() == 0;

    /// <inheritdoc/>
    public CapabilityState Open(string path, out IVideoClip? clip)
    {
        clip = null;
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!OperatingSystem.IsWindows())
        {
            return Refuse(VideoReasonCodes.VideoMechanismAbsent,
                "Media Foundation is a Windows mechanism and this process is not on Windows; nothing was attempted");
        }

        var startup = EnsureStarted();
        if (startup != 0)
        {
            return Refuse(VideoReasonCodes.VideoMediaStackUnavailable,
                $"MFStartup(0x{MediaFoundationInterop.MfVersion:X8}) returned 0x{startup:X8}, so this machine's "
                + "media stack could not be brought up at all and nothing on it can be decoded");
        }

        if (!File.Exists(path))
        {
            return Refuse(VideoReasonCodes.VideoClipUnreadable, $"there is no file at '{path}' to decode");
        }

        return MediaFoundationClip.TryOpen(path, out clip);
    }

    private static CapabilityState Refuse(string code, string detail) =>
        new CapabilityState.Unavailable(new CapabilityReason(code, detail));

    /// <summary>
    /// <c>MFStartup</c> once per process, remembered as its HRESULT so a machine with no media stack
    /// answers the same way every time instead of paying a failing native call per clip.
    /// </summary>
    private static int EnsureStarted()
    {
        if (Volatile.Read(ref _startupResult) != int.MinValue)
        {
            return _startupResult;
        }

        lock (StartupGate)
        {
            if (_startupResult == int.MinValue)
            {
                try
                {
                    _startupResult = MediaFoundationInterop.MFStartup(MediaFoundationInterop.MfVersion, 0);
                }
                catch (DllNotFoundException)
                {
                    // A Windows SKU with the media feature pack removed ("N" editions) has no
                    // mfplat.dll at all. That is a typed refusal, not a crash, and it is the same
                    // machine property that would stop every codec on the box.
                    _startupResult = unchecked((int)0x8007007E); // HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND)
                }
                catch (EntryPointNotFoundException)
                {
                    _startupResult = unchecked((int)0x80070078); // HRESULT_FROM_WIN32(ERROR_CALL_NOT_IMPLEMENTED)
                }
            }

            return _startupResult;
        }
    }

    /// <summary>One open source reader, converted to BGRX and normalised to top-down.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class MediaFoundationClip : IVideoClip
    {
        private readonly int _sourceStride;
        private MediaFoundationInterop.IMFSourceReader? _reader;
        private bool _disposed;

        private MediaFoundationClip(
            MediaFoundationInterop.IMFSourceReader reader, VideoClipInfo info, int sourceStride)
        {
            _reader = reader;
            Info = info;
            _sourceStride = sourceStride;
        }

        public VideoClipInfo Info { get; }

        public int DecodedFrames { get; private set; }

        public bool Ended { get; private set; }

        public static CapabilityState TryOpen(string path, out IVideoClip? clip)
        {
            clip = null;
            MediaFoundationInterop.IMFAttributes? attributes = null;
            MediaFoundationInterop.IMFSourceReader? reader = null;
            MediaFoundationInterop.IMFAttributes? native = null;
            MediaFoundationInterop.IMFAttributes? target = null;
            MediaFoundationInterop.IMFAttributes? current = null;
            var owned = true;
            try
            {
                var hr = MediaFoundationInterop.MFCreateAttributes(out attributes, 1);
                if (hr != 0)
                {
                    return Refuse(VideoReasonCodes.VideoMediaStackUnavailable,
                        $"MFCreateAttributes returned 0x{hr:X8}");
                }

                // Without this the reader only offers the decoder's own output format, so a stream
                // whose decoder produces NV12 could not be converted to the surface's BGRX and
                // every ordinary camera-era clip would refuse.
                var processing = MediaFoundationInterop.ReaderEnableVideoProcessing;
                attributes.SetUINT32(ref processing, 1);

                hr = MediaFoundationInterop.MFCreateSourceReaderFromURL(path, attributes, out reader);
                if (hr != 0 || reader is null)
                {
                    return Refuse(VideoReasonCodes.VideoClipUnreadable,
                        $"MFCreateSourceReaderFromURL('{Path.GetFileName(path)}') returned 0x{hr:X8}: the operating "
                        + "system will not open this file — an unknown container, a codec this machine does not "
                        + "have, or a truncated file");
                }

                hr = reader.GetNativeMediaType(MediaFoundationInterop.FirstVideoStream, 0, out native);
                if (hr != 0)
                {
                    return Refuse(VideoReasonCodes.VideoClipUnreadable,
                        $"the operating system opened '{Path.GetFileName(path)}' and reports NO video stream in it "
                        + $"(GetNativeMediaType returned 0x{hr:X8}) — an audio-only file, or a container whose "
                        + "video track this machine has no decoder for");
                }

                hr = MediaFoundationInterop.MFCreateMediaType(out target);
                if (hr != 0)
                {
                    return Refuse(VideoReasonCodes.VideoMediaStackUnavailable, $"MFCreateMediaType returned 0x{hr:X8}");
                }

                var majorKey = MediaFoundationInterop.AttributeMajorType;
                var majorValue = MediaFoundationInterop.MediaTypeVideo;
                target.SetGUID(ref majorKey, ref majorValue);
                var subtypeKey = MediaFoundationInterop.AttributeSubtype;
                var subtypeValue = MediaFoundationInterop.VideoFormatRgb32;
                target.SetGUID(ref subtypeKey, ref subtypeValue);

                hr = reader.SetCurrentMediaType(MediaFoundationInterop.FirstVideoStream, IntPtr.Zero, target);
                if (hr != 0)
                {
                    return Refuse(VideoReasonCodes.VideoClipUnreadable,
                        $"the operating system will not hand '{Path.GetFileName(path)}' back as RGB32 "
                        + $"(SetCurrentMediaType returned 0x{hr:X8}), so its pictures cannot reach a surface");
                }

                reader.SetStreamSelection(MediaFoundationInterop.FirstVideoStream, true);

                hr = reader.GetCurrentMediaType(MediaFoundationInterop.FirstVideoStream, out current);
                if (hr != 0 || current is null)
                {
                    return Refuse(VideoReasonCodes.VideoClipUnreadable,
                        $"GetCurrentMediaType returned 0x{hr:X8} after the format was accepted");
                }

                var sizeKey = MediaFoundationInterop.AttributeFrameSize;
                current.GetUINT64(ref sizeKey, out var size);
                var width = (int)(size >> 32);
                var height = (int)(size & 0xFFFFFFFF);

                var interval = TimeSpan.Zero;
                var rateKey = MediaFoundationInterop.AttributeFrameRate;
                if (current.GetUINT64(ref rateKey, out var rate) == 0)
                {
                    var numerator = rate >> 32;
                    var denominator = rate & 0xFFFFFFFF;
                    if (numerator > 0)
                    {
                        interval = TimeSpan.FromSeconds(denominator / (double)numerator);
                    }
                }

                var stride = width * VideoFrame.BytesPerPixel;
                var strideKey = MediaFoundationInterop.AttributeDefaultStride;
                if (current.GetUINT32(ref strideKey, out var rawStride) == 0)
                {
                    var reported = unchecked((int)rawStride);
                    if (reported != 0)
                    {
                        stride = reported;
                    }
                }

                var info = new VideoClipInfo(true, width, height, interval, ReadDuration(reader), stride < 0);
                if (!info.HasPicture)
                {
                    return Refuse(VideoReasonCodes.VideoClipHasNoPicture,
                        $"the operating system reports a {width}x{height} picture for '{Path.GetFileName(path)}', "
                        + "so there is nothing to present");
                }

                clip = new MediaFoundationClip(reader, info, stride);
                owned = false;
                return new CapabilityState.Available(
                    $"the operating system opened '{Path.GetFileName(path)}' and reports a {width}x{height} video "
                    + $"stream it will hand back as BGRX{(info.BottomUp ? " (bottom-up)" : string.Empty)}");
            }
            finally
            {
                MediaFoundationInterop.Release(current);
                MediaFoundationInterop.Release(target);
                MediaFoundationInterop.Release(native);
                MediaFoundationInterop.Release(attributes);
                if (owned)
                {
                    MediaFoundationInterop.Release(reader);
                }
            }
        }

        public VideoFrame? ReadFrame()
        {
            if (_disposed || _reader is null || Ended)
            {
                return null;
            }

            MediaFoundationInterop.IMFSample? sample = null;
            MediaFoundationInterop.IMFMediaBuffer? buffer = null;
            var locked = false;
            try
            {
                var hr = _reader.ReadSample(
                    MediaFoundationInterop.FirstVideoStream, 0, out _, out var flags, out _, out sample);
                if (hr != 0)
                {
                    Ended = true;
                    return null;
                }

                if ((flags & MediaFoundationInterop.EndOfStream) != 0)
                {
                    Ended = true;
                    return null;
                }

                if (sample is null)
                {
                    // A stream tick or a format-change notification carries no sample. Not an end
                    // and not a fault; the caller asks again.
                    return null;
                }

                if (sample.ConvertToContiguousBuffer(out buffer) != 0 || buffer is null)
                {
                    return null;
                }

                if (buffer.Lock(out var data, out _, out var length) != 0 || data == IntPtr.Zero)
                {
                    return null;
                }

                locked = true;
                var frame = CopyOut(data, length);
                if (frame is not null)
                {
                    DecodedFrames++;
                }

                return frame;
            }
            finally
            {
                if (locked && buffer is not null)
                {
                    buffer.Unlock();
                }

                MediaFoundationInterop.Release(buffer);
                MediaFoundationInterop.Release(sample);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            MediaFoundationInterop.Release(_reader);
            _reader = null;
        }

        private static CapabilityState Refuse(string code, string detail) =>
            new CapabilityState.Unavailable(new CapabilityReason(code, detail));

        /// <summary>
        /// The presentation's duration, out of a PROPVARIANT. The value is a <c>VT_UI8</c> whose
        /// payload sits at offset 8, which is the layout PROPVARIANT has had since it was a VARIANT,
        /// and the buffer is cleared through <c>PropVariantClear</c> rather than simply freed.
        /// </summary>
        private static TimeSpan ReadDuration(MediaFoundationInterop.IMFSourceReader reader)
        {
            var buffer = Marshal.AllocCoTaskMem(24);
            try
            {
                for (var i = 0; i < 24; i++)
                {
                    Marshal.WriteByte(buffer, i, 0);
                }

                var key = MediaFoundationInterop.AttributePresentationDuration;
                var hr = reader.GetPresentationAttribute(MediaFoundationInterop.MediaSourceStream, ref key, buffer);
                if (hr != 0)
                {
                    return TimeSpan.Zero;
                }

                var hundredNanoseconds = Marshal.ReadInt64(buffer, 8);
                MediaFoundationInterop.PropVariantClear(buffer);
                return hundredNanoseconds > 0 ? TimeSpan.FromTicks(hundredNanoseconds) : TimeSpan.Zero;
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }

        /// <summary>
        /// Copy the OS's buffer into a top-down BGRX frame, flipping when the OS's stride is
        /// negative. <b>The flip is the whole reason this method exists</b> rather than one
        /// <c>Marshal.Copy</c> at the call site: Media Foundation reported <c>-1280</c> for the
        /// port's own fixture media, and a presenter that ignored the sign would show every video
        /// upside down while every solid-colour fixture passed.
        /// </summary>
        private VideoFrame? CopyOut(IntPtr data, uint length)
        {
            var width = Info.Width;
            var height = Info.Height;
            var rowBytes = width * VideoFrame.BytesPerPixel;
            var absoluteStride = Math.Abs(_sourceStride);
            if (absoluteStride < rowBytes || (long)absoluteStride * height > length)
            {
                return null;
            }

            var pixels = new byte[(long)rowBytes * height];
            for (var row = 0; row < height; row++)
            {
                // Bottom-up means the OS's first row is the picture's LAST row.
                var sourceRow = _sourceStride < 0 ? height - 1 - row : row;
                Marshal.Copy(data + (sourceRow * absoluteStride), pixels, row * rowBytes, rowBytes);
            }

            return new VideoFrame(width, height, pixels);
        }
    }
}
