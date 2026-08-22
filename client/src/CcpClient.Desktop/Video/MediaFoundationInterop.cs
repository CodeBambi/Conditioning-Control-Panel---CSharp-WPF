using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CcpClient.Desktop.Video;

/// <summary>
/// Media Foundation, raw. Windows-only; every caller guards on <c>OperatingSystem.IsWindows()</c> —
/// the convention <c>Tray/Win32TrayInterop.cs:7</c>, <c>Overlay/Win32OverlayInterop.cs</c>,
/// <c>Input/Win32InputInterop.cs</c> and <c>Audio/WasapiRenderReadback.cs</c> already follow.
///
/// <para><b>Every GUID and every vtable SLOT ORDER below was read out of the Windows SDK headers on
/// the machine this was written on (10.0.26100.0), not recalled.</b> The same discipline the audio capability
/// applied to the eight WASAPI GUIDs, and for the same reason: a wrong slot is a call into an
/// unrelated method that may still return S_OK. The header and line for each are on the member.</para>
///
/// <para><b>Why <c>[ComImport]</c> with the base interface's slots written out, and not function
/// pointers.</b> The port compiles without <c>AllowUnsafeBlocks</c> and
/// <c>Audio/WasapiRenderReadback.cs</c> already established the house shape for a raw COM read-back:
/// declared interfaces, <c>[PreserveSig]</c> everywhere so an HRESULT is a value rather than an
/// exception, and a base interface's methods spelled out in order (see its
/// <c>IAudioSessionManager2</c>, whose comment names the two inherited slots). Where a method is not
/// used its parameters are declared as <c>IntPtr</c> — the SLOT is what matters, and a slot that is
/// never called needs no marshalling.</para>
///
/// <para><b>Why the OS's own media stack and not a bundled decoder.</b> The shipping product carries
/// LibVLC (<c>Services/Video/VideoService.cs:308-316</c>) plus the whole native quarantine and
/// poisoning apparatus its own comments describe (<c>:790</c>, <c>:819</c>, <c>:1031</c>). This port
/// asks Windows instead: it is already on every target machine, its refusals are typed HRESULTs,
/// and — the part that matters — "the operating system decoded this file" is itself an OS-level
/// fact rather than a claim about a library this process shipped.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MediaFoundationInterop
{
    /// <summary><c>MF_VERSION = (MF_SDK_VERSION &lt;&lt; 16) | MF_API_VERSION</c>
    /// (<c>mfapi.h:31,39,40</c>).</summary>
    public const uint MfVersion = (0x0002 << 16) | 0x0070;

    /// <summary><c>MF_SOURCE_READER_FIRST_VIDEO_STREAM</c> (<c>mfreadwrite.h:330</c>).</summary>
    public const uint FirstVideoStream = 0xFFFFFFFC;

    /// <summary><c>MF_SOURCE_READER_MEDIASOURCE</c> (<c>mfreadwrite.h:331</c>) — the pseudo-stream
    /// index that reaches the SOURCE's own presentation attributes.</summary>
    public const uint MediaSourceStream = 0xFFFFFFFF;

    /// <summary><c>MF_SOURCE_READERF_ENDOFSTREAM</c> (<c>mfreadwrite.h:307</c>).</summary>
    public const uint EndOfStream = 0x2;

    /// <summary><c>MFMediaType_Video</c> (<c>mfapi.h:3654</c>).</summary>
    public static readonly Guid MediaTypeVideo =
        new(0x73646976, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    /// <summary><c>MFVideoFormat_RGB32</c> — <c>DEFINE_MEDIATYPE_GUID(D3DFMT_X8R8G8B8)</c>, and
    /// <c>D3DFMT_X8R8G8B8 = 22</c> (<c>mfapi.h:2173-2176,2204</c>, <c>d3d9types.h:1379</c>). The same
    /// BGRX layout <see cref="VideoFrame"/> and <c>Overlay.OverlayFrame</c> use, which is why the
    /// decode path needs no colour conversion of its own.</summary>
    public static readonly Guid VideoFormatRgb32 =
        new(22, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    /// <summary><c>MF_MT_MAJOR_TYPE</c> (<c>mfapi.h:2559</c>).</summary>
    public static readonly Guid AttributeMajorType =
        new(0x48EBA18E, 0xF8C9, 0x4687, 0xBF, 0x11, 0x0A, 0x74, 0xC9, 0xF9, 0x6A, 0x8F);

    /// <summary><c>MF_MT_SUBTYPE</c> (<c>mfapi.h:2563</c>).</summary>
    public static readonly Guid AttributeSubtype =
        new(0xF7E34C9A, 0x42E8, 0x4714, 0xB7, 0x4B, 0xCB, 0x29, 0xD7, 0x2C, 0x35, 0xE5);

    /// <summary><c>MF_MT_FRAME_SIZE</c> (<c>mfapi.h:3019</c>) — a UINT64 of width&lt;&lt;32|height.</summary>
    public static readonly Guid AttributeFrameSize =
        new(0x1652C33D, 0xD6B2, 0x4012, 0xB8, 0x34, 0x72, 0x03, 0x08, 0x49, 0xA3, 0x7D);

    /// <summary><c>MF_MT_FRAME_RATE</c> (<c>mfapi.h:3023</c>) — UINT64 of numerator&lt;&lt;32|denominator.</summary>
    public static readonly Guid AttributeFrameRate =
        new(0xC459A2E8, 0x3D2C, 0x4E44, 0xB1, 0x32, 0xFE, 0xE5, 0x15, 0x6C, 0x7B, 0xB0);

    /// <summary><c>MF_MT_DEFAULT_STRIDE</c> (<c>mfapi.h:3225</c>) — SIGNED; negative means the OS
    /// hands the picture back bottom-up, measured as -1280 for the port's own fixture media.</summary>
    public static readonly Guid AttributeDefaultStride =
        new(0x644B4E48, 0x1E02, 0x4516, 0xB0, 0xEB, 0xC0, 0x1C, 0xA9, 0xD4, 0x9A, 0xC6);

    /// <summary><c>MF_PD_DURATION</c> (<c>mfidl.h:6195</c>) — 100 ns units, delivered as a
    /// <c>VT_UI8</c> PROPVARIANT.</summary>
    public static readonly Guid AttributePresentationDuration =
        new(0x6C990D33, 0xBB8E, 0x477A, 0x85, 0x98, 0x0D, 0x5D, 0x96, 0xFC, 0xD8, 0x8A);

    /// <summary><c>MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING</c> (<c>mfreadwrite.h:294</c>) — lets the
    /// reader insert the OS's own video processor, which is what makes RGB32 available for a stream
    /// whose decoder would otherwise only produce its native chroma format.</summary>
    public static readonly Guid ReaderEnableVideoProcessing =
        new(0xFB394F3D, 0xCCF1, 0x42EE, 0xBB, 0xB3, 0xF9, 0xB8, 0x45, 0xD5, 0x68, 0x1D);

    [DllImport("mfplat.dll")]
    public static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll")]
    public static extern int MFCreateMediaType(out IMFAttributes mediaType);

    [DllImport("mfplat.dll")]
    public static extern int MFCreateAttributes(out IMFAttributes attributes, uint initialSize);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
    public static extern int MFCreateSourceReaderFromURL(
        string url, IMFAttributes? attributes, out IMFSourceReader reader);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(IntPtr propVariant);

    /// <summary>Release a runtime callable wrapper without caring whether it is one.</summary>
    public static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    /// <summary>
    /// <c>IMFAttributes</c> (<c>mfobjects.h:310</c>). <b>Also used for every <c>IMFMediaType</c> in
    /// this file</b>: <c>IMFMediaType</c> derives from it (<c>mfobjects.h:2123-2124</c>), so a
    /// <c>QueryInterface</c> for this IID on a media type succeeds and every attribute getter this
    /// port needs lives here. Nothing in the port calls an <c>IMFMediaType</c>-specific method, so
    /// declaring that interface would be thirty-eight lines of slots to reach none of them.
    ///
    /// <para>Slots in header order: <c>GetItem</c>, <c>GetItemType</c>, <c>CompareItem</c>,
    /// <c>Compare</c>, <c>GetUINT32</c>, <c>GetUINT64</c>, <c>GetDouble</c>, <c>GetGUID</c>,
    /// <c>GetStringLength</c>, <c>GetString</c>, <c>GetAllocatedString</c>, <c>GetBlobSize</c>,
    /// <c>GetBlob</c>, <c>GetAllocatedBlob</c>, <c>GetUnknown</c>, <c>SetItem</c>,
    /// <c>DeleteItem</c>, <c>DeleteAllItems</c>, <c>SetUINT32</c>, <c>SetUINT64</c>,
    /// <c>SetDouble</c>, <c>SetGUID</c>, <c>SetString</c>, <c>SetBlob</c>, <c>SetUnknown</c>,
    /// <c>LockStore</c>, <c>UnlockStore</c>, <c>GetCount</c>, <c>GetItemByIndex</c>,
    /// <c>CopyAllItems</c>.</para>
    /// </summary>
    [ComImport]
    [Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);

        [PreserveSig] int GetItemType(ref Guid key, out int type);

        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);

        [PreserveSig] int Compare(IMFAttributes other, int matchType, out bool result);

        [PreserveSig] int GetUINT32(ref Guid key, out uint value);

        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);

        [PreserveSig] int GetDouble(ref Guid key, out double value);

        [PreserveSig] int GetGUID(ref Guid key, out Guid value);

        [PreserveSig] int GetStringLength(ref Guid key, out uint length);

        [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, IntPtr length);

        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);

        [PreserveSig] int GetBlobSize(ref Guid key, out uint size);

        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, uint size, IntPtr written);

        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);

        [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr value);

        [PreserveSig] int SetItem(ref Guid key, IntPtr value);

        [PreserveSig] int DeleteItem(ref Guid key);

        [PreserveSig] int DeleteAllItems();

        [PreserveSig] int SetUINT32(ref Guid key, uint value);

        [PreserveSig] int SetUINT64(ref Guid key, ulong value);

        [PreserveSig] int SetDouble(ref Guid key, double value);

        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);

        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);

        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, uint size);

        [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);

        [PreserveSig] int LockStore();

        [PreserveSig] int UnlockStore();

        [PreserveSig] int GetCount(out uint count);

        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);

        [PreserveSig] int CopyAllItems(IMFAttributes destination);
    }

    /// <summary>
    /// <c>IMFSample</c> (<c>mfobjects.h:938-939</c>), which derives from <see cref="IMFAttributes"/>
    /// — so its own thirty slots come FIRST and are written out here as unused stubs. The one method
    /// the port calls is <c>ConvertToContiguousBuffer</c>, the ninth of the sample's own.
    /// </summary>
    [ComImport]
    [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample
    {
        // ---- the thirty IMFAttributes slots, in header order --------------------------------
        [PreserveSig] int GetItem(ref Guid key, IntPtr value);

        [PreserveSig] int GetItemType(ref Guid key, out int type);

        [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out bool result);

        [PreserveSig] int Compare(IntPtr other, int matchType, out bool result);

        [PreserveSig] int GetUINT32(ref Guid key, out uint value);

        [PreserveSig] int GetUINT64(ref Guid key, out ulong value);

        [PreserveSig] int GetDouble(ref Guid key, out double value);

        [PreserveSig] int GetGUID(ref Guid key, out Guid value);

        [PreserveSig] int GetStringLength(ref Guid key, out uint length);

        [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, IntPtr length);

        [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);

        [PreserveSig] int GetBlobSize(ref Guid key, out uint size);

        [PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, uint size, IntPtr written);

        [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out uint size);

        [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr value);

        [PreserveSig] int SetItem(ref Guid key, IntPtr value);

        [PreserveSig] int DeleteItem(ref Guid key);

        [PreserveSig] int DeleteAllItems();

        [PreserveSig] int SetUINT32(ref Guid key, uint value);

        [PreserveSig] int SetUINT64(ref Guid key, ulong value);

        [PreserveSig] int SetDouble(ref Guid key, double value);

        [PreserveSig] int SetGUID(ref Guid key, ref Guid value);

        [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);

        [PreserveSig] int SetBlob(ref Guid key, IntPtr buffer, uint size);

        [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);

        [PreserveSig] int LockStore();

        [PreserveSig] int UnlockStore();

        [PreserveSig] int GetCount(out uint count);

        [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);

        [PreserveSig] int CopyAllItems(IntPtr destination);

        // ---- IMFSample's own, in header order ------------------------------------------------
        [PreserveSig] int GetSampleFlags(out uint flags);

        [PreserveSig] int SetSampleFlags(uint flags);

        [PreserveSig] int GetSampleTime(out long time);

        [PreserveSig] int SetSampleTime(long time);

        [PreserveSig] int GetSampleDuration(out long duration);

        [PreserveSig] int SetSampleDuration(long duration);

        [PreserveSig] int GetBufferCount(out uint count);

        [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer buffer);

        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
    }

    /// <summary><c>IMFMediaBuffer</c> (<c>mfobjects.h:798-799</c>).</summary>
    [ComImport]
    [Guid("045FA593-8799-42b8-BC8D-8968C6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out uint maxLength, out uint currentLength);

        [PreserveSig] int Unlock();

        [PreserveSig] int GetCurrentLength(out uint length);

        [PreserveSig] int SetCurrentLength(uint length);

        [PreserveSig] int GetMaxLength(out uint length);
    }

    /// <summary><c>IMFSourceReader</c> (<c>mfreadwrite.h:356-357</c>, vtable at <c>:461-547</c>).</summary>
    [ComImport]
    [Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSourceReader
    {
        [PreserveSig] int GetStreamSelection(uint stream, out bool selected);

        [PreserveSig] int SetStreamSelection(uint stream, bool selected);

        [PreserveSig] int GetNativeMediaType(uint stream, uint mediaTypeIndex, out IMFAttributes mediaType);

        [PreserveSig] int GetCurrentMediaType(uint stream, out IMFAttributes mediaType);

        [PreserveSig] int SetCurrentMediaType(uint stream, IntPtr reserved, IMFAttributes mediaType);

        [PreserveSig] int SetCurrentPosition(ref Guid timeFormat, IntPtr position);

        [PreserveSig] int ReadSample(
            uint stream, uint controlFlags, out uint actualStream, out uint streamFlags,
            out long timestamp, out IMFSample? sample);

        [PreserveSig] int Flush(uint stream);

        [PreserveSig] int GetServiceForStream(uint stream, ref Guid service, ref Guid iid, out IntPtr instance);

        [PreserveSig] int GetPresentationAttribute(uint stream, ref Guid key, IntPtr value);
    }
}
