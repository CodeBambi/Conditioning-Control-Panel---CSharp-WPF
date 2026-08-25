using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Camera;

/// <summary>
/// <b>The WINDOWS capture route: Media Foundation's <c>IMFSourceReader</c>, opened by device
/// symbolic link and read frame by frame into a buffer that never leaves the method it was
/// allocated in.</b>
///
/// <para><b>Why Media Foundation and not upstream's API.</b> Upstream opens cameras through OpenCV's
/// <c>VideoCapture</c> with a DirectShow-first / MediaFoundation-fallback ladder
/// (<c>Services/Webcam/WebcamTrackingService.cs:167-172</c>). OpenCvSharp plus its native
/// <c>OpenCvSharpExtern.dll</c> is a new third-party dependency, and <c>client/port.txt</c> reserves
/// new dependencies to an advisor review that has not run for this row. Media Foundation is in the
/// operating system, needs no package, and is the API family slice 1 already named as the capture
/// slice's business (<c>Camera/DirectShowCameraDeviceSource.cs</c>, the WinRT-fallback paragraph).
/// The ladder's SHAPE and its acceptance rule are ported exactly; the transport underneath is not.</para>
///
/// <para><b>THE UPSTREAM DIVERGENCE THIS CLOSES, and the one it does not.</b> Upstream's four rungs
/// exist for two independent reasons, and only one of them survives the transport change:</para>
///
/// <list type="bullet">
/// <item><b>The MSMF rungs are no longer a fallback — they are the whole route.</b> Upstream keeps
/// Media Foundation <i>"as a fallback for the MF-only / 32-bit-only devices the WinRT enumerator
/// catches"</i> (<c>Services/Webcam/WebcamTrackingService.cs:159-161</c>, issues #282/#279/#291).
///
/// <para><b>That sentence has TWO halves and only the second one is closed by the transport
/// change, which this file's first version got wrong and said so out loud.</b> It claimed a build
/// whose only capture path IS Media Foundation "cannot miss an MF-only camera, so that entire
/// class of failure is closed" — true of the OPEN, false of the PRODUCT, because the open is only
/// ever reached for a device the ROSTER already named and the roster came from DirectShow alone.
/// Upstream needs both halves for a reason: MSMF capture rungs to OPEN such a camera, and a
/// separate WinRT/MF ENUMERATOR to SEE it at all
/// (<c>Services/Webcam/WebcamWinRtEnumerator.cs:13-17</c>). The enumeration half is now closed
/// too, by <see cref="EnumerateDevices"/> — the same <c>MFEnumDeviceSources</c> walk
/// <see cref="FindActivate"/> already ran to MATCH a device, used to LIST them — and it is
/// reached the way upstream reaches its own fallback: only when the DirectShow list comes back
/// empty (<c>Services/Webcam/WebcamTrackingService.cs:1120-1134</c>).</para>
///
/// <para>What is LOST is upstream's stated preference for
/// DirectShow on Elgato and UVC webcams (<c>:157-159</c>): if such a camera behaves worse under MF
/// than under DSHOW, this port has no second transport to fall back to. That is a real, named
/// user-visible risk and it is not hidden behind a claim of parity.</para></item>
/// <item><b>The MJPG escalation IS ported, because its cause is the camera and not the API.</b>
/// One camera hands back frames that are non-empty and contain no face under default format
/// negotiation (<c>:163-166</c>, BUG-F2XJE2E7X9), and asking for MJPG instead fixes it. The default
/// rung still runs FIRST for upstream's own reason — <i>"so cameras that already work are
/// untouched"</i> (<c>:162-163</c>) — and MJPG is escalated to only after the default feed has been
/// rejected by the probe.</item>
/// </list>
///
/// <para><b>Identity is a device path, never an index.</b> Upstream opens an integer that its own
/// comments warn is not guaranteed to mean the same camera the dropdown showed
/// (<c>Services/Webcam/WebcamDeviceEnumerator.cs:15-21</c>), and carries a runtime warning for when
/// the remembered index and the remembered name disagree
/// (<c>Services/Webcam/WebcamTrackingService.cs:1243-1249</c>). Here the enumerated DirectShow
/// moniker and the Media Foundation symbolic link are reduced to the same hardware instance by
/// <see cref="CameraHardwareKey"/>, and a device with no match is REFUSED rather than approximated.
/// There is no index in this file.</para>
///
/// <para><b>The privacy contract, kept structurally.</b> Pixels exist in exactly two places: a pair
/// of <c>byte[]</c> locals inside <see cref="WarmUp"/>, which the CLR reclaims when it returns, and
/// a locked native buffer inside <see cref="ReadFrame"/> that is measured for LENGTH and never
/// copied at all. This class declares no field, property, parameter or return that can hold a frame,
/// so there is nothing here for a log line, a settings file or a crash report to reach. Upstream
/// writes the same rule as a comment — <i>"Log per-frame numbers (gaze X/Y, eye-state, etc.) — only
/// state strings and counts"</i> (<c>Services/Webcam/WebcamTrackingService.cs:28-29</c>) — and this
/// class has no per-frame number to log even if it wanted to.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MediaFoundationCameraCapture : ICameraCaptureSource
{
    private const int MfVersion = 0x00020070;
    private const int MfStartupLite = 1;

    // MF_SOURCE_READER_FIRST_VIDEO_STREAM. Tried FIRST and then abandoned if the source rejects
    // it — see ResolveVideoStream for the machine that made that necessary.
    private const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
    private const int AllStreams = unchecked((int)0xFFFFFFFE);

    /// <summary>No stream on this source can describe itself. Never a valid index.</summary>
    private const int NoVideoStream = -1;

    /// <summary>How far the stream walk goes. A capture device with more streams than this is not a
    /// webcam, and an unbounded walk over a misbehaving driver is a hang.</summary>
    private const int MaxStreamsWalked = 16;

    /// <summary>How many native media types are listed when a format request has to be explained.
    /// A UVC camera offers a handful; the bound stops an unterminated driver enumeration.</summary>
    private const int MaxNativeTypesWalked = 64;

    private const int SourceReaderErrorFlag = 0x00000001;
    private const int SourceReaderEndOfStreamFlag = 0x00000002;

    // EVERY GUID BELOW WAS READ OUT OF THE WINDOWS SDK HEADERS ON THE MACHINE THIS WAS PROVED ON
    // (Include/10.0.26100.0/um: mfapi.h, mfidl.h, mfreadwrite.h, mfobjects.h), NOT FROM MEMORY.
    // That is not diligence theatre. MF_MT_SUBTYPE was first written from memory as
    // f7e34b9a-4d5b-4b10-a163-a89f0c22b0d1, which is not a Media Foundation constant at all, and the
    // symptom was a camera that enumerated ten native formats and then reported that none of them had
    // a subtype — indistinguishable from a broken driver, and the reason this capture path spent an
    // afternoon "not working" on hardware that was fine. A wrong GUID does not fail loudly: it asks a
    // real object a question about a key nobody set, and gets a perfectly valid "not found".
    private static Guid DevSourceAttributeSourceType = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
    private static Guid DevSourceVideoCaptureGuid = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
    private static Guid DevSourceSymbolicLink = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
    private static Guid DevSourceFriendlyName = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
    private static Guid SourceReaderEnableAdvancedVideoProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
    private static Guid MediaTypeMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static Guid MediaTypeSubType = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static Guid MediaTypeFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static Guid MajorTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    private static Guid VideoFormatRgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    private static Guid VideoFormatMotionJpeg = new("47504a4d-0000-0010-8000-00aa00389b71");
    private static Guid IidMediaSource = new("279a808d-aec7-40c8-9c6b-a6b492c78a66");

    private readonly List<string> _attempted = [];
    private IMFSourceReader? _reader;
    private IMFMediaSource? _source;
    private int _stream = FirstVideoStream;
    private bool _platformStarted;
    private bool _disposed;

    /// <inheritdoc/>
    public string Backend => "Media Foundation IMFSourceReader (RGB32)";

    /// <inheritdoc/>
    public bool IsOpen => _reader is not null;

    /// <inheritdoc/>
    public int Width { get; private set; }

    /// <inheritdoc/>
    public int Height { get; private set; }

    /// <inheritdoc/>
    public string? AdoptedRung { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<string> AttemptedRungs => _attempted;

    /// <inheritdoc/>
    public int FramesRead { get; private set; }

    /// <summary>
    /// Open <paramref name="device"/> and warm it up. The ladder is
    /// <see cref="CameraCaptureLadder.Order"/>: default format first, MJPG second, each with its own
    /// <see cref="CameraFrameProbe.WarmupMilliseconds"/> budget — upstream's per-attempt budget
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1262-1268</c>), which it accepts deliberately
    /// because a slow-to-start camera is indistinguishable from a dead one until the budget elapses.
    /// </summary>
    public CapabilityState? Open(CameraDevice device, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        Close();
        _attempted.Clear();

        IMFActivate? activate = null;
        try
        {
            if (!StartPlatform(out var startupFailure))
            {
                _attempted.Add("no rung: Media Foundation would not start on this Windows installation");
                return startupFailure;
            }

            activate = FindActivate(device, out var matchFailure);
            if (activate is null)
            {
                // A line even though no format was tried. AttemptedRungs is what makes "this launch
                // never asked a camera to open" checkable at the seam, so it must be non-empty after
                // EVERY call — including one that walked the whole device list and matched nothing,
                // which has still asked Windows for this user's cameras.
                _attempted.Add("no rung: no Media Foundation device is the camera the roster named");
                return matchFailure;
            }

            var openedAtAll = false;
            foreach (var rung in CameraCaptureLadder.Order)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryRung(activate, rung, ref openedAtAll, cancellationToken))
                {
                    AdoptedRung = rung;
                    FramesRead = 0;
                    return null;
                }
            }

            // Upstream's own split, and the reason it keeps `anyOpened`: a device that never opened
            // on any backend is a different problem from one that opened and never produced a usable
            // frame, and the second is usually antivirus webcam shielding or another application
            // holding the feed (Services/Webcam/WebcamTrackingService.cs:1273-1321).
            return openedAtAll
                ? new CapabilityState.Degraded(
                    "a camera that this process can open",
                    new CapabilityReason(
                        CameraReasonCodes.CameraNoUsableFrame,
                        "the camera opened but delivered no usable frame on any format within "
                        + $"{CameraFrameProbe.WarmupMilliseconds}ms per attempt. It was reading frames that were "
                        + "empty, or solid enough that nothing could be seen in them — most often another "
                        + "application, or antivirus webcam shielding, is holding the feed. NOTHING FROM THOSE "
                        + "FRAMES WAS KEPT: they were measured and dropped"))
                : new CapabilityState.Faulted(new CapabilityReason(
                    CameraReasonCodes.CameraOpenFailed,
                    "the camera could not be opened on any format. It was named by the device enumeration a moment "
                    + "ago, so it has been unplugged since, or another application holds it exclusively, or the "
                    + "operating system refused this process the device"));
        }
        catch (OperationCanceledException)
        {
            Close();
            throw;
        }
        catch (Exception ex)
        {
            Close();
            // Type name only. A Media Foundation HRESULT message on this path can carry the device
            // symbolic link, which is hardware identity and belongs in nobody's log by accident —
            // the rule Camera/DirectShowCameraDeviceSource.cs already follows.
            return new CapabilityState.Faulted(new CapabilityReason(
                CameraReasonCodes.CameraCaptureFailed,
                $"the Media Foundation capture path failed with {ex.GetType().Name}; no camera is open and no "
                + "claim is made about whether one could be"));
        }
        finally
        {
            Release(activate);
        }
    }

    /// <summary>
    /// Read one frame and drop it. <b>Nothing is copied</b>: the native buffer is locked, its current
    /// length is read so that "pixels arrived" is a fact rather than an assumption, and it is
    /// unlocked again. There is no managed buffer on this path at all.
    /// </summary>
    public bool ReadFrame(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_reader is not { } reader)
        {
            return false;
        }

        IMFSample? sample = null;
        IMFMediaBuffer? buffer = null;
        var locked = false;
        try
        {
            // Synchronous ReadSample BLOCKS until the device produces something, which is why the
            // caller runs this off the UI thread. A null sample with no error flag is a stream tick
            // or a gap, and upstream tolerates a run of those before calling the device lost
            // (Services/Webcam/WebcamTrackingService.cs:120).
            if (reader.ReadSample(_stream, 0, out _, out var flags, out _, out sample) != 0)
            {
                return false;
            }

            if ((flags & (SourceReaderErrorFlag | SourceReaderEndOfStreamFlag)) != 0 || sample is null)
            {
                return false;
            }

            if (sample.ConvertToContiguousBuffer(out buffer) != 0 || buffer is null)
            {
                return false;
            }

            if (buffer.Lock(out _, out _, out var length) != 0)
            {
                return false;
            }

            locked = true;
            if (length <= 0)
            {
                return false;
            }

            FramesRead++;
            return true;
        }
        catch (COMException)
        {
            // A device pulled mid-read throws here. It is a read that did not happen, not a fact
            // about every future read, so the caller's consecutive-failure budget decides.
            return false;
        }
        finally
        {
            if (locked && buffer is not null)
            {
                try
                {
                    buffer.Unlock();
                }
                catch (COMException)
                {
                    // The buffer went away with the device. There is nothing left to unlock.
                }
            }

            Release(buffer);
            Release(sample);
        }
    }

    /// <summary>
    /// Release the device: the reader first, then the media source's own <c>Shutdown</c>, then the
    /// Media Foundation platform.
    ///
    /// <para><b>The order and the Shutdown call are the whole point.</b> Dropping the last managed
    /// reference is not enough — <c>IMFMediaSource::Shutdown</c> is what makes the driver let go, and
    /// a camera whose indicator stays lit after the user stopped it is the worst outcome this
    /// capability has available. <c>Marshal.FinalReleaseComObject</c> is used rather than
    /// <c>ReleaseComObject</c> for the same reason: it drives the runtime-callable wrapper's count to
    /// zero now, instead of leaving the device held until a garbage collection nobody scheduled.</para>
    /// </summary>
    public void Close()
    {
        var reader = _reader;
        var source = _source;
        _reader = null;
        _source = null;
        AdoptedRung = null;
        Width = 0;
        Height = 0;

        Release(reader);

        if (source is not null)
        {
            try
            {
                source.Shutdown();
            }
            catch (COMException)
            {
                // MF_E_SHUTDOWN: the reader already shut it down. Releasing below is all that is left.
            }

            Release(source);
        }

        StopPlatform();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Close();
        _disposed = true;
    }

    // =========================================================================================
    //  The ladder
    // =========================================================================================

    /// <summary>
    /// One rung: activate the source, build a reader over it, ask for the rung's format, and warm up.
    /// A rung that opens but never delivers an acceptable frame is TORN DOWN before the next rung is
    /// tried — upstream disposes for the same reason, so the loop falls through <i>"instead of
    /// locking in a feed that reads fine but contains no detectable face"</i>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1256-1261</c>).
    /// </summary>
    private bool TryRung(IMFActivate activate, string rung, ref bool openedAtAll, CancellationToken cancellationToken)
    {
        IMFMediaSource? source = null;
        IMFSourceReader? reader = null;
        try
        {
            // The HRESULT is carried into the rung string on purpose. It is a diagnostic NUMBER and
            // carries no device path, no symbolic link and no user identity — the thing this file
            // refuses to log is an exception MESSAGE, which on this path can carry all three. Without
            // it, "the device would not activate" is unactionable: E_ACCESSDENIED (0x80070005, the
            // Windows camera privacy setting) and MF_E_HW_MFT_FAILED_START_STREAMING (0xC00D3E85,
            // another application holding the feed) are completely different repairs.
            var activation = activate.ActivateObject(ref IidMediaSource, out var sourceObject);
            if (activation != 0)
            {
                _attempted.Add($"{rung}: the device would not activate (0x{activation:X8})");
                return false;
            }

            if (sourceObject is not IMFMediaSource activated)
            {
                Release(sourceObject);
                _attempted.Add($"{rung}: the activated object is not a media source");
                return false;
            }

            source = activated;
            if (MFCreateAttributes(out var readerAttributes, 1) != 0 || readerAttributes is null)
            {
                _attempted.Add($"{rung}: no reader attribute store");
                return false;
            }

            try
            {
                // Let Media Foundation insert its video processor so an RGB32 output type can be
                // requested over whatever the camera natively produces. Without this, only formats
                // the device itself offers can be selected and every camera would need its own
                // colour conversion here.
                readerAttributes.SetUINT32(ref SourceReaderEnableAdvancedVideoProcessing, 1);
                var created = MFCreateSourceReaderFromMediaSource(source, readerAttributes, out reader);
                if (created != 0 || reader is null)
                {
                    _attempted.Add($"{rung}: no source reader over the device (0x{created:X8})");
                    return false;
                }
            }
            finally
            {
                Release(readerAttributes);
            }

            openedAtAll = true;
            _stream = ResolveVideoStream(reader);
            if (_stream == NoVideoStream)
            {
                _attempted.Add($"{rung}: the device exposes no video stream this reader can describe");
                return false;
            }

            reader.SetStreamSelection(AllStreams, false);
            reader.SetStreamSelection(_stream, true);

            if (rung == CameraCaptureLadder.MotionJpeg && !SelectMotionJpegNativeType(reader, _stream))
            {
                _attempted.Add($"{rung}: the camera offers no MJPG format");
                return false;
            }

            var output = RequestRgb32(reader, _stream);
            if (output is not null)
            {
                // The formats the device DOES offer go in the message. A FOURCC is a device
                // capability, not anything a camera saw, and without it "would not deliver RGB32" is
                // a dead end for whoever has to work out why one machine's webcam will not start.
                _attempted.Add($"{rung}: the camera would not deliver RGB32 ({output}); it offers {NativeFormats(reader, _stream)}");
                return false;
            }

            ReadFrameSize(reader);
            var accepted = WarmUp(reader, rung, cancellationToken);
            if (!accepted)
            {
                return false;
            }

            _reader = reader;
            _source = source;
            reader = null;
            source = null;
            return true;
        }
        catch (COMException ex)
        {
            _attempted.Add($"{rung}: failed with COMException 0x{ex.HResult:X8}");
            return false;
        }
        finally
        {
            // Whatever this rung still owns did not win. Shut the device down before the next rung
            // asks for it, or the second rung opens a camera the first one is still holding.
            Release(reader);
            if (source is not null)
            {
                try
                {
                    source.Shutdown();
                }
                catch (COMException)
                {
                    // Already shut down by the reader that was just released.
                }

                Release(source);

                // AND DETACH, or the next rung gets this same corpse. IMFActivate CACHES the object
                // it created: a second ActivateObject hands back the identical media source, which
                // this rung has just shut down, and MFCreateSourceReaderFromMediaSource then fails
                // over it. That is exactly what this machine's camera did — the MJPG rung reported
                // "no source reader over the device" for a device that was never the problem.
                // DetachObject clears the cache so the next rung activates a fresh source, which is
                // upstream's shape too: it constructs a new VideoCapture per attempt
                // (Services/Webcam/WebcamTrackingService.cs:1349).
                try
                {
                    activate.DetachObject();
                }
                catch (COMException)
                {
                    // Some activation objects do not implement it. The next rung will then fail on
                    // the stale source and say so, which is still an honest answer.
                }
            }
        }
    }

    /// <summary>
    /// Upstream's warm-up loop, ported to a blocking reader
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1385-1430</c>). The two <c>byte[]</c> locals here
    /// are the only managed pixel buffers this product ever allocates, and they die with this call.
    ///
    /// <para><b>No sleep, and that is a transport difference rather than a behaviour change.</b>
    /// Upstream sleeps <c>ProbeReadIntervalMs</c> between reads (<c>:1429</c>) because OpenCV's
    /// <c>Read</c> returns immediately with an empty Mat and a tight loop would spin a core.
    /// <c>IMFSourceReader.ReadSample</c> blocks until the device produces something, so the loop is
    /// paced by the camera itself. The budget it runs against is upstream's, unchanged.</para>
    /// </summary>
    private bool WarmUp(IMFSourceReader reader, string rung, CancellationToken cancellationToken)
    {
        byte[]? current = null;
        byte[]? previous = null;
        var previousLength = 0;
        var reads = 0;
        var budget = Stopwatch.StartNew();

        while (budget.ElapsedMilliseconds < CameraFrameProbe.WarmupMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reads++;

            var length = CopyNextFrame(reader, _stream, ref current);
            if (length <= 0 || current is null)
            {
                continue;
            }

            var spatial = CameraFrameProbe.MaxChannelStdDev(current.AsSpan(0, length));

            // The two frames must be the SAME SIZE or the difference is meaningless — upstream
            // compares prevProbe.Size() and prevProbe.Type() for exactly this reason
            // (Services/Webcam/WebcamTrackingService.cs:1403). A format renegotiation mid-warm-up
            // would otherwise read as enormous motion and adopt a feed nobody has looked at.
            var temporal = previous is not null && previousLength == length
                ? CameraFrameProbe.MaxChannelMeanAbsoluteDifference(
                    current.AsSpan(0, length), previous.AsSpan(0, length))
                : 0;

            if (CameraFrameProbe.Accepts(spatial, temporal))
            {
                _attempted.Add($"{rung}: adopted after {reads} read(s)");
                return true;
            }

            // Non-empty but degenerate — remember it and keep probing inside the budget, in case
            // this is a slow-warming or dim-but-live feed (upstream at :1420-1427). The measured
            // numbers are NOT recorded anywhere: they are per-frame derivatives.
            if (previous is null || previous.Length < length)
            {
                previous = new byte[length];
            }

            Array.Copy(current, previous, length);
            previousLength = length;
        }

        _attempted.Add(
            $"{rung}: opened, but no usable frame in {CameraFrameProbe.WarmupMilliseconds}ms ({reads} read(s))");
        return false;
    }

    /// <summary>
    /// Read one sample into <paramref name="buffer"/>, growing it when the frame does not fit, and
    /// return the byte count. Zero for a tick, a gap or an error.
    ///
    /// <para>Private, and it takes the buffer by reference, because the buffer must not become a
    /// FIELD: a scratch buffer on this object would be the one place in the build where the last
    /// frame the camera saw outlives the call that read it.</para>
    /// </summary>
    private static int CopyNextFrame(IMFSourceReader reader, int stream, ref byte[]? buffer)
    {
        IMFSample? sample = null;
        IMFMediaBuffer? mediaBuffer = null;
        var locked = false;
        try
        {
            if (reader.ReadSample(stream, 0, out _, out var flags, out _, out sample) != 0
                || (flags & (SourceReaderErrorFlag | SourceReaderEndOfStreamFlag)) != 0
                || sample is null)
            {
                return 0;
            }

            if (sample.ConvertToContiguousBuffer(out mediaBuffer) != 0 || mediaBuffer is null)
            {
                return 0;
            }

            if (mediaBuffer.Lock(out var pointer, out _, out var length) != 0)
            {
                return 0;
            }

            locked = true;
            if (length <= 0 || pointer == IntPtr.Zero)
            {
                return 0;
            }

            if (buffer is null || buffer.Length < length)
            {
                buffer = new byte[length];
            }

            Marshal.Copy(pointer, buffer, 0, length);
            return length;
        }
        catch (COMException)
        {
            return 0;
        }
        finally
        {
            if (locked && mediaBuffer is not null)
            {
                try
                {
                    mediaBuffer.Unlock();
                }
                catch (COMException)
                {
                    // The device went away holding the lock; there is nothing left to unlock.
                }
            }

            Release(mediaBuffer);
            Release(sample);
        }
    }

    // =========================================================================================
    //  Format negotiation
    // =========================================================================================

    /// <summary>
    /// Ask the device for an MJPG native type before the reader's output type is set — the port of
    /// upstream's <c>cap.Set(VideoCaptureProperties.FourCC, …"MJPG")</c>
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1366-1369</c>). False when the camera offers no
    /// MJPG format at all, which is the YUY2-only case upstream's comment names
    /// (<c>:163-166</c>) and is a reason to stop this rung rather than to fail the open.
    /// </summary>
    /// <summary>
    /// <b>Which stream index this reader's video really lives on.</b>
    ///
    /// <para>Media Foundation publishes a sentinel for exactly this question —
    /// <c>MF_SOURCE_READER_FIRST_VIDEO_STREAM</c> — and this method tries it first. It exists
    /// because THE SENTINEL DOES NOT ALWAYS RESOLVE: the integrated camera on the machine this
    /// slice was proved against rejects it with <c>MF_E_INVALIDSTREAMNUMBER</c> (0xC00D36B9) on
    /// every call, and every operation keyed to it — describe the formats, request a format, read a
    /// sample — therefore failed against a device that was working perfectly. Trusting the sentinel
    /// alone would have shipped a capture path that opens no camera on a mainstream laptop.</para>
    ///
    /// <para>The fallback is the direct question the sentinel is shorthand for: walk the real stream
    /// indices and take the first one whose native type says <c>MFMediaType_Video</c>. If no stream
    /// declares a major type at all, the first stream that can describe ANY native type is taken,
    /// because the source came from the VIDEO capture device category and there is nothing else it
    /// could be.</para>
    /// </summary>
    private static int ResolveVideoStream(IMFSourceReader reader)
    {
        if (reader.GetNativeMediaType(FirstVideoStream, 0, out var sentinelType) == 0 && sentinelType is not null)
        {
            Release(sentinelType);
            return FirstVideoStream;
        }

        var firstDescribable = NoVideoStream;
        for (var stream = 0; stream < MaxStreamsWalked; stream++)
        {
            if (reader.GetNativeMediaType(stream, 0, out var nativeType) != 0 || nativeType is null)
            {
                continue;
            }

            try
            {
                if (firstDescribable == NoVideoStream)
                {
                    firstDescribable = stream;
                }

                if (nativeType.GetGUID(ref MediaTypeMajorType, out var major) == 0 && major == MajorTypeVideo)
                {
                    return stream;
                }
            }
            finally
            {
                Release(nativeType);
            }
        }

        return firstDescribable;
    }

    private static bool SelectMotionJpegNativeType(IMFSourceReader reader, int stream)
    {
        for (var index = 0; index < MaxNativeTypesWalked; index++)
        {
            if (reader.GetNativeMediaType(stream, index, out var nativeType) != 0 || nativeType is null)
            {
                return false;
            }

            try
            {
                if (nativeType.GetGUID(ref MediaTypeSubType, out var subtype) == 0
                    && subtype == VideoFormatMotionJpeg
                    && reader.SetCurrentMediaType(stream, IntPtr.Zero, nativeType) == 0)
                {
                    return true;
                }
            }
            finally
            {
                Release(nativeType);
            }
        }

        // The walk ran to its bound without finding MJPG. Not a failure of the device: a
        // YUY2-only camera is normal and is exactly the case upstream's comment protects
        // (Services/Webcam/WebcamTrackingService.cs:163-166).
        return false;
    }

    /// <summary>
    /// Request RGB32 output. A PARTIAL media type — major type and subtype only — which is how the
    /// reader is told to negotiate everything else and to convert through the video processor
    /// enabled in <see cref="TryRung"/>. RGB32 is asked for explicitly so
    /// <see cref="CameraFrameProbe"/> can never be handed a planar or subsampled buffer whose
    /// channels it would measure wrongly.
    /// </summary>
    private static string? RequestRgb32(IMFSourceReader reader, int stream)
    {
        var created = MFCreateMediaType(out var output);
        if (created != 0 || output is null)
        {
            return $"MFCreateMediaType 0x{created:X8}";
        }

        try
        {
            var major = output.SetGUID(ref MediaTypeMajorType, ref MajorTypeVideo);
            if (major != 0)
            {
                return $"SetGUID(MAJOR_TYPE) 0x{major:X8}";
            }

            var subtype = output.SetGUID(ref MediaTypeSubType, ref VideoFormatRgb32);
            if (subtype != 0)
            {
                return $"SetGUID(SUBTYPE) 0x{subtype:X8}";
            }

            var applied = reader.SetCurrentMediaType(stream, IntPtr.Zero, output);
            return applied == 0 ? null : $"SetCurrentMediaType 0x{applied:X8}";
        }
        finally
        {
            Release(output);
        }
    }

    /// <summary>
    /// The FOURCCs this device's stream really offers, as a short list. Device CAPABILITIES, never
    /// anything a camera observed — the same class of fact as the friendly name upstream logs
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1136-1141</c>).
    /// </summary>
    private static string NativeFormats(IMFSourceReader reader, int stream)
    {
        var formats = new List<string>();
        var walked = 0;
        for (var index = 0; index < MaxNativeTypesWalked; index++)
        {
            if (reader.GetNativeMediaType(stream, index, out var nativeType) != 0 || nativeType is null)
            {
                break;
            }

            try
            {
                walked++;
                if (nativeType.GetGUID(ref MediaTypeSubType, out var subtype) == 0)
                {
                    var name = FourCharacterCode(subtype);
                    if (!formats.Contains(name))
                    {
                        formats.Add(name);
                    }
                }
            }
            finally
            {
                Release(nativeType);
            }
        }

        return formats.Count == 0
            ? $"no format it would name ({walked} type(s) walked)"
            : $"{string.Join(", ", formats)} ({walked} type(s))";
    }

    /// <summary>A video subtype GUID's first field IS its FOURCC for every format that has one, which
    /// is why <c>MFVideoFormat_NV12</c> and friends all end <c>-0000-0010-8000-00AA00389B71</c>.</summary>
    private static string FourCharacterCode(Guid subtype)
    {
        var data1 = subtype.ToByteArray();
        var code = new string(
        [
            (char)data1[0], (char)data1[1], (char)data1[2], (char)data1[3],
        ]);

        return code.All(character => character is >= ' ' and <= '~')
            ? code
            : subtype.ToString("D");
    }

    /// <summary>The negotiated frame size, read back from the reader rather than assumed. Silent on
    /// failure: an unknown size is reported as 0x0 and never as a guess.</summary>
    private void ReadFrameSize(IMFSourceReader reader)
    {
        if (reader.GetCurrentMediaType(_stream, out var current) != 0 || current is null)
        {
            return;
        }

        try
        {
            if (current.GetUINT64(ref MediaTypeFrameSize, out var packed) == 0)
            {
                Width = (int)(packed >> 32);
                Height = (int)(packed & 0xFFFFFFFF);
            }
        }
        finally
        {
            Release(current);
        }
    }

    // =========================================================================================
    //  Device enumeration — the SECOND look, when the DirectShow roster comes back empty
    // =========================================================================================

    /// <summary>The route <see cref="EnumerateDevices"/> speaks for. Named, never an index.</summary>
    public const string EnumerationRoute =
        "Media Foundation MFEnumDeviceSources over MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID";

    /// <summary>
    /// <b>The video-capture devices Media Foundation can see, for the machines where DirectShow can
    /// see none.</b>
    ///
    /// <para><b>Why this lives on the capture type.</b> Upstream needs TWO things to serve an
    /// MF-only or 32-bit-only camera, and they are separate: MSMF capture rungs to OPEN it
    /// (<c>Services/Webcam/WebcamTrackingService.cs:167-172</c>) and a WinRT enumerator to SEE it,
    /// because <i>"a 64-bit process misses cameras that register only 32-bit DirectShow filters or
    /// are Media-Foundation-only, so DirectShow returns an empty list even though Discord / Windows
    /// Camera / OpenCV-MSMF open the device fine"</i>
    /// (<c>Services/Webcam/WebcamWinRtEnumerator.cs:13-17</c>, issues #282/#279/#291). Slice 1 named
    /// that gap and deferred it here in as many words — <i>"closing that needs a Media Foundation
    /// IMFActivate enumeration through mfplat/mf.dll, which is the capture slice's business because
    /// that is the API family the capture path would use anyway"</i>
    /// (<c>Camera/DirectShowCameraDeviceSource.cs</c>) — and this is that enumeration. It is the walk
    /// <see cref="FindActivate"/> already performs to MATCH one device, with the match predicate
    /// removed, so the interop underneath it is code that has run against real hardware rather than
    /// a second hand-laid copy of it.</para>
    ///
    /// <para><b>NOTHING HERE OPENS A CAMERA and no indicator lights.</b> <c>MFEnumDeviceSources</c>
    /// hands back ACTIVATION objects, which are descriptions; a device is opened only by
    /// <c>IMFActivate::ActivateObject</c>, which this method never calls and which lives in
    /// <see cref="TryRung"/> behind the consent gate. That is what lets an enumeration route call it
    /// at all — <see cref="ICameraDeviceSource"/> promises that enumerating touches no device.</para>
    ///
    /// <para><b>A device with no symbolic link is SKIPPED rather than offered under a friendly
    /// name.</b> Every identity this port hands out has to survive being persisted
    /// (<c>client/docs/capability-inventory.md</c>: <i>"Never use only a transient camera index"</i>),
    /// and the symbolic link is the only durable one Media Foundation has. Upstream's WinRT fallback
    /// keeps such a device under an <c>(int Index, string Name)</c> pair
    /// (<c>Services/Webcam/WebcamWinRtEnumerator.cs:44-50</c>); that pair is exactly the identity
    /// this port refuses to build a roster from.</para>
    /// </summary>
    public static CameraInventory EnumerateDevices()
    {
        if (!StartPlatformForEnumeration())
        {
            return CameraInventory.Refusing(EnumerationRoute, new CapabilityState.DependencyMissing(
                "the Media Foundation platform (mfplat.dll)",
                new CapabilityReason(
                    CameraReasonCodes.CameraEnumerationUnsupported,
                    "Media Foundation would not start on this Windows installation, so its video-capture device "
                    + "list could not be read. That list is the SECOND look — the DirectShow enumeration is the "
                    + "first — so a camera only Media Foundation can see would be missed here. This is usually an "
                    + "N or KN edition of Windows without the Media Feature Pack installed. NO CAMERA WAS OPENED")));
        }

        IMFAttributes? attributes = null;
        var list = IntPtr.Zero;
        var count = 0;
        try
        {
            if (MFCreateAttributes(out attributes, 1) != 0 || attributes is null
                || attributes.SetGUID(ref DevSourceAttributeSourceType, ref DevSourceVideoCaptureGuid) != 0
                || MFEnumDeviceSources(attributes, out list, out count) != 0)
            {
                return CameraInventory.Refusing(
                    EnumerationRoute, EnumerationFaulted("the device list could not be built"));
            }

            var devices = new List<CameraDevice>(count);
            for (var index = 0; index < count; index++)
            {
                var entry = Marshal.ReadIntPtr(list, index * IntPtr.Size);
                if (entry == IntPtr.Zero)
                {
                    continue;
                }

                var activate = Marshal.GetObjectForIUnknown(entry) as IMFActivate;
                Marshal.Release(entry);
                if (activate is null)
                {
                    continue;
                }

                try
                {
                    if (activate.GetAllocatedString(ref DevSourceSymbolicLink, out var link, out _) != 0
                        || string.IsNullOrWhiteSpace(link))
                    {
                        continue;
                    }

                    // Upstream's own placeholder for a device the OS named nothing
                    // (Services/Webcam/WebcamWinRtEnumerator.cs:48). A blank row in a camera picker is
                    // worse than an honest one.
                    var named = activate.GetAllocatedString(ref DevSourceFriendlyName, out var name, out _) == 0
                        && !string.IsNullOrWhiteSpace(name)
                        ? name
                        : "(unnamed device)";

                    devices.Add(new CameraDevice(link, named, IdentityIsStable: true));
                }
                finally
                {
                    Release(activate);
                }
            }

            return CameraInventory.Named(EnumerationRoute, devices);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type name only, for the reason Camera/DirectShowCameraDeviceSource.cs gives: an HRESULT
            // message on this path can carry a device symbolic link.
            return CameraInventory.Refusing(
                EnumerationRoute, EnumerationFaulted($"it failed with {ex.GetType().Name}"));
        }
        finally
        {
            Release(attributes);
            if (list != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(list);
            }

            StopPlatformForEnumeration();
        }
    }

    private static CapabilityState EnumerationFaulted(string what) =>
        new CapabilityState.Faulted(new CapabilityReason(
            CameraReasonCodes.CameraEnumerationFailed,
            $"the Media Foundation device enumeration ran and {what}, so no claim is made about whether a "
            + "camera is attached. NO CAMERA WAS OPENED"));

    /// <summary>Start the platform for ONE enumeration. Separate from <see cref="StartPlatform"/>
    /// because that one owns an INSTANCE's lifetime and this method has no instance;
    /// <c>MFStartup</c>/<c>MFShutdown</c> are reference-counted per process, so the two nest
    /// safely.</summary>
    private static bool StartPlatformForEnumeration()
    {
        try
        {
            return MFStartup(MfVersion, MfStartupLite) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void StopPlatformForEnumeration()
    {
        try
        {
            MFShutdown();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Unreachable once MFStartup has succeeded, and swallowed for the reason StopPlatform
            // swallows it: a teardown that throws would mask the outcome being reported.
        }
    }

    // =========================================================================================
    //  Device selection
    // =========================================================================================

    /// <summary>
    /// The Media Foundation video-capture device whose symbolic link is the same hardware instance
    /// as <paramref name="device"/>, or null with the reason it is null.
    ///
    /// <para><b>VIDEO ONLY.</b> The attribute store asks for
    /// <c>MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID</c> and nothing else, so there is no code
    /// path here that could enumerate, let alone open, an audio endpoint — the same property slice 1
    /// gave the enumeration route (<c>Services/Webcam/WebcamTrackingService.cs:30</c>).</para>
    /// </summary>
    private IMFActivate? FindActivate(CameraDevice device, out CapabilityState failure)
    {
        failure = new CapabilityState.Faulted(new CapabilityReason(
            CameraReasonCodes.CameraCaptureFailed,
            "the Media Foundation video-capture device list could not be built, so no camera was opened"));

        if (MFCreateAttributes(out var attributes, 1) != 0 || attributes is null)
        {
            return null;
        }

        var list = IntPtr.Zero;
        var count = 0;
        try
        {
            if (attributes.SetGUID(ref DevSourceAttributeSourceType, ref DevSourceVideoCaptureGuid) != 0
                || MFEnumDeviceSources(attributes, out list, out count) != 0)
            {
                return null;
            }

            IMFActivate? match = null;
            for (var index = 0; index < count; index++)
            {
                var entry = Marshal.ReadIntPtr(list, index * IntPtr.Size);
                if (entry == IntPtr.Zero)
                {
                    continue;
                }

                var candidate = Marshal.GetObjectForIUnknown(entry) as IMFActivate;
                Marshal.Release(entry);
                if (candidate is null)
                {
                    continue;
                }

                if (match is null && MatchesDevice(candidate, device))
                {
                    match = candidate;
                    continue;
                }

                Release(candidate);
            }

            if (match is null)
            {
                failure = new CapabilityState.DependencyMissing(
                    "a Media Foundation view of the camera the device enumeration named",
                    new CapabilityReason(
                        CameraReasonCodes.CameraDeviceNotMatched,
                        $"the device enumeration named this camera, but none of the {count} video-capture device(s) "
                        + "Media Foundation reports is the same hardware. It has most likely been unplugged since "
                        + "the roster was taken. NO CAMERA WAS OPENED, and none was opened speculatively: this "
                        + "product never picks a camera by position"));
            }

            return match;
        }
        finally
        {
            Release(attributes);
            if (list != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(list);
            }
        }
    }

    /// <summary>Whether an activation object is the camera the roster named. The symbolic link is
    /// tried first because it is the durable identity; the friendly name is a fallback for device
    /// classes whose two views share no path (software and virtual cameras).</summary>
    private static bool MatchesDevice(IMFActivate activate, CameraDevice device)
    {
        if (activate.GetAllocatedString(ref DevSourceSymbolicLink, out var link, out _) == 0
            && CameraHardwareKey.Matches(device.StableId, link))
        {
            return true;
        }

        return activate.GetAllocatedString(ref DevSourceFriendlyName, out var name, out _) == 0
            && !string.IsNullOrWhiteSpace(name)
            && string.Equals(name, device.DisplayName, StringComparison.Ordinal);
    }

    // =========================================================================================
    //  Platform lifetime and COM release
    // =========================================================================================

    private bool StartPlatform(out CapabilityState failure)
    {
        failure = new CapabilityState.DependencyMissing(
            "the Media Foundation platform (mfplat.dll)",
            new CapabilityReason(
                CameraReasonCodes.CameraCaptureUnsupported,
                "Media Foundation would not start on this Windows installation, so no camera could be opened. "
                + "This is usually an N or KN edition of Windows without the Media Feature Pack installed"));

        if (_platformStarted)
        {
            return true;
        }

        try
        {
            if (MFStartup(MfVersion, MfStartupLite) != 0)
            {
                return false;
            }
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }

        _platformStarted = true;
        return true;
    }

    private void StopPlatform()
    {
        if (!_platformStarted)
        {
            return;
        }

        _platformStarted = false;
        try
        {
            MFShutdown();
        }
        catch (DllNotFoundException)
        {
            // It never started, so there is nothing to shut down.
        }
    }

    /// <summary>
    /// Release ONE reference — the one the interop marshaller took on our behalf — right now,
    /// instead of waiting for a garbage collection nobody scheduled. A camera released "eventually"
    /// is a camera the user cannot take back.
    ///
    /// <para><b>NOT <c>Marshal.FinalReleaseComObject</c>.</b> It was written that way first, on the
    /// reasoning that driving the count to zero is the strongest form of "let go". The runtime caches
    /// ONE runtime-callable wrapper per native pointer, and Media Foundation does hand the same
    /// media-type objects back from repeated <c>GetNativeMediaType</c> calls, so a final release
    /// severs a wrapper other code still holds. That hazard was not measured to have caused a failure
    /// here — the bug being hunted at the time turned out to be a wrong GUID constant — but a
    /// balanced release is both correct and sufficient, and the strong form buys nothing over it.</para>
    ///
    ///
    /// <para><b><c>IMFActivate.ShutdownObject</c> is deliberately NEVER called from anywhere in this
    /// file.</b> It shuts down the media source the activation object created — which, after a rung
    /// has been adopted, is the camera this process is currently streaming from. Releasing the
    /// activation object is the correct and only teardown here; the source's own
    /// <c>Shutdown</c> is called by <see cref="Close"/> and by the failure path in
    /// <see cref="TryRung"/>, which are the two places that know the device is finished with.</para>
    /// </summary>
    private static void Release(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(comObject);
        }
        catch (ArgumentException)
        {
            // Not a runtime-callable wrapper, or already released — the same swallow
            // Camera/DirectShowCameraDeviceSource.cs makes, for the same reason.
        }
    }

    // =========================================================================================
    //  Interop — the operating system's signatures, not this port's design
    // =========================================================================================

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out IMFAttributes? attributes, int initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType? mediaType);

    [DllImport("mf.dll", ExactSpelling = true)]
    private static extern int MFEnumDeviceSources(IMFAttributes attributes, out IntPtr activates, out int count);

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    private static extern int MFCreateSourceReaderFromMediaSource(
        IMFMediaSource source, IMFAttributes attributes, out IMFSourceReader? reader);


    // -------------------------------------------------------------------------------------
    //  THE VTABLES ARE FLATTENED, AND THAT IS A CORRECTION RATHER THAN A STYLE.
    //
    //  These were first written the way the COM headers read — IMFActivate : IMFAttributes,
    //  IMFSample : IMFAttributes — and this machine's camera then refused every open with
    //  MF_E_ATTRIBUTENOTFOUND (0xC00D36E6) out of IMFActivate::ActivateObject. That HRESULT is what
    //  IMFAttributes::GetItem returns for a key it does not hold, and IMFAttributes::GetItem is
    //  SLOT 1: the runtime was indexing the derived interface's methods from the base's first slot,
    //  so ActivateObject's call landed on GetItem with IID_IMFMediaSource read as an attribute key.
    //  It failed cleanly, which is the dangerous kind of wrong — a plausible error code for a
    //  completely fictional cause, and one no amount of reading the C# would have found.
    //
    //  So every interface below declares EVERY slot it has and inherits nothing. It is longer, and
    //  it cannot be silently mis-indexed by an edit to a different interface. Unused slots are named
    //  for what they really are, because a COM vtable is positional and a missing declaration
    //  silently calls the neighbouring function.
    // -------------------------------------------------------------------------------------

    /// <summary><c>IMFAttributes</c>: 30 slots. This file calls <c>GetAllocatedString</c>,
    /// <c>SetUINT32</c> and <c>SetGUID</c> on it.</summary>
    [ComImport]
    [Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        [PreserveSig] int Slot01_GetItem();
        [PreserveSig] int Slot02_GetItemType();
        [PreserveSig] int Slot03_CompareItem();
        [PreserveSig] int Slot04_Compare();
        [PreserveSig] int Slot05_GetUINT32();
        [PreserveSig] int Slot06_GetUINT64();
        [PreserveSig] int Slot07_GetDouble();
        [PreserveSig] int Slot08_GetGUID();
        [PreserveSig] int Slot09_GetStringLength();
        [PreserveSig] int Slot10_GetString();
        [PreserveSig] int Slot11_GetAllocatedString();
        [PreserveSig] int Slot12_GetBlobSize();
        [PreserveSig] int Slot13_GetBlob();
        [PreserveSig] int Slot14_GetAllocatedBlob();
        [PreserveSig] int Slot15_GetUnknown();
        [PreserveSig] int Slot16_SetItem();
        [PreserveSig] int Slot17_DeleteItem();
        [PreserveSig] int Slot18_DeleteAllItems();
        [PreserveSig] int SetUINT32(ref Guid key, int value);
        [PreserveSig] int Slot20_SetUINT64();
        [PreserveSig] int Slot21_SetDouble();
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int Slot23_SetString();
        [PreserveSig] int Slot24_SetBlob();
        [PreserveSig] int Slot25_SetUnknown();
        [PreserveSig] int Slot26_LockStore();
        [PreserveSig] int Slot27_UnlockStore();
        [PreserveSig] int Slot28_GetCount();
        [PreserveSig] int Slot29_GetItemByIndex();
        [PreserveSig] int Slot30_CopyAllItems();
    }

    /// <summary><c>IMFActivate</c>: the 30 <c>IMFAttributes</c> slots, then the three activation
    /// members. <c>ShutdownObject</c> is declared and DELIBERATELY never called — it shuts down the
    /// media source this activate created, which after a successful open is the camera this process
    /// is streaming from.</summary>
    [ComImport]
    [Guid("7fee9e9a-4a89-47a6-899c-b6a53a70fb67")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFActivate
    {
        [PreserveSig] int Slot01_GetItem();
        [PreserveSig] int Slot02_GetItemType();
        [PreserveSig] int Slot03_CompareItem();
        [PreserveSig] int Slot04_Compare();
        [PreserveSig] int Slot05_GetUINT32();
        [PreserveSig] int Slot06_GetUINT64();
        [PreserveSig] int Slot07_GetDouble();
        [PreserveSig] int Slot08_GetGUID();
        [PreserveSig] int Slot09_GetStringLength();
        [PreserveSig] int Slot10_GetString();
        [PreserveSig] int GetAllocatedString(
            ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] out string value, out int length);
        [PreserveSig] int Slot12_GetBlobSize();
        [PreserveSig] int Slot13_GetBlob();
        [PreserveSig] int Slot14_GetAllocatedBlob();
        [PreserveSig] int Slot15_GetUnknown();
        [PreserveSig] int Slot16_SetItem();
        [PreserveSig] int Slot17_DeleteItem();
        [PreserveSig] int Slot18_DeleteAllItems();
        [PreserveSig] int Slot19_SetUINT32();
        [PreserveSig] int Slot20_SetUINT64();
        [PreserveSig] int Slot21_SetDouble();
        [PreserveSig] int Slot22_SetGUID();
        [PreserveSig] int Slot23_SetString();
        [PreserveSig] int Slot24_SetBlob();
        [PreserveSig] int Slot25_SetUnknown();
        [PreserveSig] int Slot26_LockStore();
        [PreserveSig] int Slot27_UnlockStore();
        [PreserveSig] int Slot28_GetCount();
        [PreserveSig] int Slot29_GetItemByIndex();
        [PreserveSig] int Slot30_CopyAllItems();
        [PreserveSig] int ActivateObject(
            ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object? activated);
        [PreserveSig] int Slot32_ShutdownObject();
        [PreserveSig] int DetachObject();
    }

    /// <summary><c>IMFMediaType</c>: the 30 <c>IMFAttributes</c> slots, then five this file never
    /// calls. Only the attribute members are used — a media type IS an attribute store.</summary>
    [ComImport]
    [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        [PreserveSig] int Slot01_GetItem();
        [PreserveSig] int Slot02_GetItemType();
        [PreserveSig] int Slot03_CompareItem();
        [PreserveSig] int Slot04_Compare();
        [PreserveSig] int Slot05_GetUINT32();
        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
        [PreserveSig] int Slot07_GetDouble();
        [PreserveSig] int GetGUID(ref Guid key, out Guid value);
        [PreserveSig] int Slot09_GetStringLength();
        [PreserveSig] int Slot10_GetString();
        [PreserveSig] int Slot11_GetAllocatedString();
        [PreserveSig] int Slot12_GetBlobSize();
        [PreserveSig] int Slot13_GetBlob();
        [PreserveSig] int Slot14_GetAllocatedBlob();
        [PreserveSig] int Slot15_GetUnknown();
        [PreserveSig] int Slot16_SetItem();
        [PreserveSig] int Slot17_DeleteItem();
        [PreserveSig] int Slot18_DeleteAllItems();
        [PreserveSig] int Slot19_SetUINT32();
        [PreserveSig] int Slot20_SetUINT64();
        [PreserveSig] int Slot21_SetDouble();
        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        [PreserveSig] int Slot23_SetString();
        [PreserveSig] int Slot24_SetBlob();
        [PreserveSig] int Slot25_SetUnknown();
        [PreserveSig] int Slot26_LockStore();
        [PreserveSig] int Slot27_UnlockStore();
        [PreserveSig] int Slot28_GetCount();
        [PreserveSig] int Slot29_GetItemByIndex();
        [PreserveSig] int Slot30_CopyAllItems();
        [PreserveSig] int Slot31_GetMajorType();
        [PreserveSig] int Slot32_IsCompressedFormat();
        [PreserveSig] int Slot33_IsEqual();
        [PreserveSig] int Slot34_GetRepresentation();
        [PreserveSig] int Slot35_FreeRepresentation();
    }

    /// <summary><c>IMFSample</c>: the 30 <c>IMFAttributes</c> slots, then the sample members. Only
    /// <c>ConvertToContiguousBuffer</c> is called.</summary>
    [ComImport]
    [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        [PreserveSig] int Slot01_GetItem();
        [PreserveSig] int Slot02_GetItemType();
        [PreserveSig] int Slot03_CompareItem();
        [PreserveSig] int Slot04_Compare();
        [PreserveSig] int Slot05_GetUINT32();
        [PreserveSig] int Slot06_GetUINT64();
        [PreserveSig] int Slot07_GetDouble();
        [PreserveSig] int Slot08_GetGUID();
        [PreserveSig] int Slot09_GetStringLength();
        [PreserveSig] int Slot10_GetString();
        [PreserveSig] int Slot11_GetAllocatedString();
        [PreserveSig] int Slot12_GetBlobSize();
        [PreserveSig] int Slot13_GetBlob();
        [PreserveSig] int Slot14_GetAllocatedBlob();
        [PreserveSig] int Slot15_GetUnknown();
        [PreserveSig] int Slot16_SetItem();
        [PreserveSig] int Slot17_DeleteItem();
        [PreserveSig] int Slot18_DeleteAllItems();
        [PreserveSig] int Slot19_SetUINT32();
        [PreserveSig] int Slot20_SetUINT64();
        [PreserveSig] int Slot21_SetDouble();
        [PreserveSig] int Slot22_SetGUID();
        [PreserveSig] int Slot23_SetString();
        [PreserveSig] int Slot24_SetBlob();
        [PreserveSig] int Slot25_SetUnknown();
        [PreserveSig] int Slot26_LockStore();
        [PreserveSig] int Slot27_UnlockStore();
        [PreserveSig] int Slot28_GetCount();
        [PreserveSig] int Slot29_GetItemByIndex();
        [PreserveSig] int Slot30_CopyAllItems();
        [PreserveSig] int Slot31_GetSampleFlags();
        [PreserveSig] int Slot32_SetSampleFlags();
        [PreserveSig] int Slot33_GetSampleTime();
        [PreserveSig] int Slot34_SetSampleTime();
        [PreserveSig] int Slot35_GetSampleDuration();
        [PreserveSig] int Slot36_SetSampleDuration();
        [PreserveSig] int Slot37_GetBufferCount();
        [PreserveSig] int Slot38_GetBufferByIndex();
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer? buffer);
        [PreserveSig] int Slot40_AddBuffer();
        [PreserveSig] int Slot41_RemoveBufferByIndex();
        [PreserveSig] int Slot42_RemoveAllBuffers();
        [PreserveSig] int Slot43_GetTotalLength();
        [PreserveSig] int Slot44_CopyToBuffer();
    }

    /// <summary><c>IMFMediaSource</c>: the four <c>IMFMediaEventGenerator</c> slots, then the source
    /// members. <c>Shutdown</c> is the one that matters — it is what makes the driver let go.</summary>
    [ComImport]
    [Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaSource
    {
        [PreserveSig] int Slot01_GetEvent();
        [PreserveSig] int Slot02_BeginGetEvent();
        [PreserveSig] int Slot03_EndGetEvent();
        [PreserveSig] int Slot04_QueueEvent();
        [PreserveSig] int Slot05_GetCharacteristics();
        [PreserveSig] int Slot06_CreatePresentationDescriptor();
        [PreserveSig] int Slot07_Start();
        [PreserveSig] int Slot08_Stop();
        [PreserveSig] int Slot09_Pause();
        [PreserveSig] int Shutdown();
    }

    /// <summary><c>IMFSourceReader</c>, synchronous mode: 10 slots, inherits nothing.</summary>
    [ComImport]
    [Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSourceReader
    {
        [PreserveSig] int Slot01_GetStreamSelection();
        [PreserveSig] int SetStreamSelection(int streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);
        [PreserveSig] int GetNativeMediaType(int streamIndex, int mediaTypeIndex, out IMFMediaType? mediaType);
        [PreserveSig] int GetCurrentMediaType(int streamIndex, out IMFMediaType? mediaType);
        [PreserveSig] int SetCurrentMediaType(int streamIndex, IntPtr reserved, IMFMediaType mediaType);
        [PreserveSig] int Slot06_SetCurrentPosition();
        [PreserveSig] int ReadSample(
            int streamIndex, int controlFlags, out int actualStreamIndex, out int streamFlags,
            out long timestamp, out IMFSample? sample);
        [PreserveSig] int Slot08_Flush();
        [PreserveSig] int Slot09_GetServiceForStream();
        [PreserveSig] int Slot10_GetPresentationAttribute();
    }

    /// <summary><c>IMFMediaBuffer</c>. <c>Lock</c>'s <c>IntPtr</c> is the operating system's own
    /// signature: it is where the pixels are, and this file measures its LENGTH or copies it into a
    /// method local — never into anything that survives the call.</summary>
    [ComImport]
    [Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int Slot03_GetCurrentLength();
        [PreserveSig] int Slot04_SetCurrentLength();
        [PreserveSig] int Slot05_GetMaxLength();
    }
}
