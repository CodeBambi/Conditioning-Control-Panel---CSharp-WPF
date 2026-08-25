using System.Runtime.InteropServices;
using CcpClient.Desktop.Overlay;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The product says what coordinate space it lives in, and the space is the one two files
/// already document.</b>
///
/// <para><c>Overlay/OverlayDisplays.cs</c> ("One display as the operating system describes it, in
/// physical pixels") and <c>Effects/PrimaryDisplayPlacement.cs</c> ("in physical pixels") are the
/// contract every overlay rectangle in this build is computed against — the flash, spiral,
/// subliminal, pink-filter, bouncing-text and video surfaces, the lock card, the bubble field and
/// the pop quiz all place themselves through one of those two. That sentence is only TRUE of a
/// process that has declared DPI awareness. A DPI-unaware process is handed a virtualized desktop
/// scaled by the PRIMARY display's DPI, which is silently self-consistent on a one-monitor desk and
/// wrong on a mixed-scaling one.</para>
///
/// <para><b>Measured on this machine (one display, 175%) while these facts were written:</b> an
/// unmanifested <c>net10.0</c> apphost reads the primary monitor as <c>1646x1029</c>; the same
/// binary under <c>PER_MONITOR_AWARE_V2</c> reads <c>2880x1800</c>, which is what
/// <c>EnumDisplaySettings</c> reports as the display's mode either way. The port was already
/// per-monitor-aware at RUNTIME because <c>Avalonia.Win32</c> calls
/// <c>SetProcessDpiAwarenessContext</c> when its platform initialises — but that is phase 4, a
/// dependency's default, and it is not the product speaking. It says so itself now
/// (<c>client/src/CcpClient.Desktop/app.manifest</c>), with the shipping product's own values
/// (<c>ConditioningControlPanel/app.manifest:6-7</c>).</para>
///
/// <para><b>What these facts are NOT.</b> They are not a claim about pixels, a window, or anything
/// rendered: nothing here opens a surface. They do not run on Linux, where none of this exists.
/// And the second one deliberately does not put the whole TEST HOST into the product's awareness —
/// see its own note.</para>
/// </summary>
public class DpiAwarenessTests
{
    /// <summary>Per-monitor v2 — the context Windows applies to a manifest declaring it, and the
    /// one <c>Avalonia.Win32</c> asks for at platform init.</summary>
    private static readonly nint PerMonitorAwareV2 = -4;

    /// <summary>The unaware context: the virtualized desktop, and what this test host is in.</summary>
    private static readonly nint Unaware = -1;

    private const int EnumCurrentSettings = -1;

    /// <summary>
    /// <b>The declaration is in the binary that ships, not only in a source file.</b> Two halves
    /// have to hold for a manifest to reach a process — the XML has to say it, and the project has
    /// to embed it in the apphost — and the SECOND half lives in a file this row does not own. So
    /// this reads the built <c>.exe</c> rather than the manifest source: if
    /// <c>&lt;ApplicationManifest&gt;</c> is ever dropped from the csproj the declaration silently
    /// stops existing, the product silently goes back to inheriting a package default, and nothing
    /// else in this suite would notice.
    ///
    /// <para>Both elements are required together, which is the shipping product's own shape
    /// (<c>ConditioningControlPanel/app.manifest:6-7</c>): Windows 10 1607 and later read
    /// <c>dpiAwareness</c>, and older releases only understand <c>dpiAware</c>. Declaring one is a
    /// process whose awareness depends on which Windows it lands on.</para>
    ///
    /// <para>The SOURCE half is checked on every platform — it is a file in this repository and its
    /// content does not depend on where the suite runs — and only the EMBEDDED half is Windows-only,
    /// because that is the only platform where an apphost carries a manifest at all. Neither arm is
    /// silent.</para>
    /// </summary>
    [Fact]
    public void TheShippedApphostCarriesTheProductsOwnPerMonitorV2Declaration()
    {
        string[] declarations = [">PerMonitorV2<", ">true/pm<"];

        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "client", "src", "CcpClient.Desktop", "app.manifest"));
        foreach (var declaration in declarations)
        {
            Assert.True(
                source.Contains(declaration, StringComparison.Ordinal),
                $"client/src/CcpClient.Desktop/app.manifest no longer declares '{declaration}', so the "
                + "product is back to inheriting Avalonia's runtime default and every rectangle "
                + "Overlay/OverlayDisplays.cs documents as physical is virtualized until phase 4");
        }

        if (!OperatingSystem.IsWindows())
        {
            // A property of the OS: only a Windows apphost embeds an application manifest, so the
            // second half of the wiring cannot be read here. The source half above already ran.
            return;
        }

        var apphost = Path.Combine(DesktopOutputDirectory(), "CcpClient.Desktop.exe");
        Assert.True(
            File.Exists(apphost),
            $"no built apphost at {apphost} — this fact reads the shipped binary, and the floor "
            + "builds client/CcpClient.sln -c Debug before it runs");

        // Asserted through a message rather than Assert.Contains: the haystack is a 165 KB binary,
        // and a failure that dumps the front of a PE header names nothing a reader can act on.
        var embedded = File.ReadAllText(apphost, System.Text.Encoding.UTF8);
        foreach (var declaration in declarations)
        {
            Assert.True(
                embedded.Contains(declaration, StringComparison.Ordinal),
                $"the built apphost {apphost} carries no '{declaration}' while the manifest source "
                + "does — the csproj stopped embedding it (<ApplicationManifest>), so the "
                + "declaration exists in the repository and not in the product");
        }
    }

    /// <summary>
    /// <b>In the awareness the product runs in, every display it enumerates is the display's own
    /// physical mode</b> — <see cref="OverlayDisplays"/>'s documented contract, checked against an
    /// oracle that does not depend on DPI awareness at all: <c>EnumDisplaySettings</c> with
    /// <c>ENUM_CURRENT_SETTINGS</c> reports the mode the adapter is actually driving, and it reads
    /// the same in an unaware process as in an aware one (measured).
    ///
    /// <para><b>And the differential, so this cannot go vacuous on an unscaled desk.</b> The same
    /// enumeration is taken a second time in the UNAWARE context and the primary display's two
    /// answers must differ exactly when the desktop is scaled — <c>dmLogPixels != 96</c>. On a 100%
    /// monitor the two agree and the fact says so; on this 175% one they must not, which is what
    /// makes the aware read load-bearing rather than a coincidence. Only the PRIMARY display gets
    /// the differential: an unaware process scales EVERY monitor by the primary's DPI, so a
    /// second monitor's own scale says nothing about what its virtualized rectangle will be.</para>
    ///
    /// <para><b>Why the awareness is set per THREAD and not on the process.</b> Putting this whole
    /// test host into <c>PER_MONITOR_AWARE_V2</c> would move the coordinate space under every other
    /// real-window suite here — the overlay, glyph, input and pointer probes and the real-desktop
    /// preflight all create windows and hit-test them at screen coordinates — to buy no additional
    /// proof, because each of those is a self-consistent round trip that reads the same space it
    /// wrote. The claim being checked is about the space the PRODUCT computes in, and
    /// <c>SetThreadDpiAwarenessContext</c> puts exactly this call into exactly that space and
    /// restores it afterwards.</para>
    /// </summary>
    [Fact]
    public void UnderTheProductsOwnDpiAwareness_TheDisplayEnumerationIsTheOperatingSystemsPhysicalMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            // A property of the OS, and an ASSERTED one rather than a silent exit: off Windows the
            // product enumerates nothing at all, by its own design and its own doc comment.
            Assert.Empty(OverlayDisplays.Enumerate());
            return;
        }

        // No version predicate: .NET 10 itself requires Windows 10 1607 or later, which is the same
        // release that introduced SetThreadDpiAwarenessContext — so on any Windows this assembly can
        // run on, the call exists. Enumerate() asserts that it was honoured rather than assuming it.
        var aware = Enumerate(PerMonitorAwareV2);
        var unaware = Enumerate(Unaware);
        if (aware.Count == 0)
        {
            // No interactive desktop on this machine — a property of the session, and the
            // enumeration's own documented answer for it.
            Assert.Empty(unaware);
            return;
        }

        var compared = 0;
        foreach (var display in aware)
        {
            if (!Mode(display.DeviceName, out var width, out var height, out _))
            {
                continue;   // a device the adapter will not describe; counted by never comparing it
            }

            compared++;

            // Compared as an unordered pair: a rotated display reports its mode the way the adapter
            // drives it, and which of the two numbers is "width" is not this fact's business.
            Assert.Equal(
                new[] { width, height }.Order(),
                new[] { display.Bounds.Width, display.Bounds.Height }.Order());
        }

        Assert.True(
            compared > 0,
            $"none of the {aware.Count} enumerated display(s) could be cross-checked against a "
            + "display mode, so nothing above was actually compared");

        // The differential, on the primary — aware[0] is primary-first by OverlayDisplays' own sort.
        Assert.True(Mode(aware[0].DeviceName, out _, out _, out var dpi));
        var virtualized = unaware.Single(d => d.DeviceName == aware[0].DeviceName);
        var agree = virtualized.Bounds.Width == aware[0].Bounds.Width
            && virtualized.Bounds.Height == aware[0].Bounds.Height;
        Assert.True(
            agree == (dpi == 96),
            $"the primary display reads {aware[0].Bounds.Width}x{aware[0].Bounds.Height} aware and "
            + $"{virtualized.Bounds.Width}x{virtualized.Bounds.Height} unaware at {dpi} DPI. Those two "
            + "must differ on a scaled desktop and agree on an unscaled one; that they do not means "
            + "either the awareness context did not take (and the physical-mode check above proved "
            + "nothing) or this machine's scaling is not what its display mode reports");
    }

    /// <summary>Runs the PRODUCT's own enumeration in a named awareness context and puts the thread
    /// back where it found it. The restore is not tidiness: xunit reuses threads, and a leaked
    /// context would silently move the coordinate space under whatever ran next.</summary>
    private static IReadOnlyList<OverlayDisplay> Enumerate(nint context)
    {
        var previous = SetThreadDpiAwarenessContext(context);
        Assert.True(
            previous != 0,
            "SetThreadDpiAwarenessContext was refused, so nothing below would have been measured in "
            + "the context it names");

        try
        {
            return OverlayDisplays.Enumerate();
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>The mode the adapter is driving for one device, and its DPI. A SECOND, independent
    /// declaration of the interop rather than the product's — <see cref="OverlayWindowProbe"/>'s
    /// rule and its reason: a fact that measured the product through the product's own P/Invokes
    /// could be fooled by one edit to those declarations.</summary>
    private static bool Mode(string deviceName, out int width, out int height, out int dpi)
    {
        var mode = new Devmode { dmSize = (ushort)Marshal.SizeOf<Devmode>() };
        if (!EnumDisplaySettingsW(deviceName, EnumCurrentSettings, ref mode))
        {
            width = height = dpi = 0;
            return false;
        }

        width = (int)mode.dmPelsWidth;
        height = (int)mode.dmPelsHeight;
        dpi = mode.dmLogPixels;
        return true;
    }

    /// <summary>Where the desktop project's own build output is, derived from this test binary's —
    /// both projects build to <c>bin/&lt;configuration&gt;/net10.0</c> under the same repository.</summary>
    private static string DesktopOutputDirectory()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = here.Parent?.Name
            ?? throw new InvalidOperationException($"cannot read a configuration from {here.FullName}");

        return Path.Combine(
            RepoRoot(), "client", "src", "CcpClient.Desktop", "bin", configuration, here.Name);
    }

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }

        return root?.FullName
            ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Devmode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string deviceName, int mode, ref Devmode devmode);
}
