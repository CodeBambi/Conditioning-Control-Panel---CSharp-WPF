using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Overlay;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The emergency stop, measured against the exact situation it was built for.</b>
///
/// <para>On 2026-08-23 a user could not close this application. Nineteen of its surfaces were
/// visible, three of them the full size of the monitor, and the process had to be killed for him
/// from outside. Every affordance that ends a session lives inside the Avalonia shell, and every
/// effect surface goes up <c>HWND_TOPMOST</c> over it — so the question this file answers is not
/// "does a hotkey work" but <b>"does the gesture still arrive when a full-monitor topmost window of
/// this build's own type is over the screen and the foreground belongs to something else"</b>.</para>
///
/// <para><b>The negative control is a SECOND LIVE KEY, not a timeout.</b> Proving that a disposed
/// panic key stops receiving presses by waiting for nothing to happen would be a wall-clock wait
/// with no signal in it, which this suite bans. Instead the disposed key's successor is armed and
/// the SAME injected chord is waited for on IT: one delivery, two readings — the live key saw it,
/// the dead one did not. That also proves the release half, which matters on its own: a panic key
/// that never gave the chord back would keep <c>Ctrl+Alt+Esc</c> away from every other application
/// on the machine for the rest of the session.</para>
///
/// <para><b>WINDOWS EVIDENCE ONLY.</b> Every reading here is USER32's — <c>RegisterHotKey</c>,
/// <c>SendInput</c>, <c>IsWindow</c>, <c>WindowFromPoint</c>, and a real window procedure receiving
/// a real <c>WM_HOTKEY</c>. There is no system-wide hotkey mechanism on X11 or Wayland in this
/// build and none is claimed. The two facts refuse differently and deliberately: the first has a
/// meaningful off-Windows answer (the capability must say Unavailable rather than pretend) so it is
/// KEYED to the platform, while the second injects real input and would be vacuous with nothing to
/// inject, so it GATES on the machine and refuses by name.</para>
///
/// <para><b>The wedged case USED to be named here as untested, and now it is a fact.</b> That
/// paragraph said the message "would be delivered the moment the pump ran again" — which is exactly
/// the problem, because the thread whose pump had to run was the UI thread, and a measurement on
/// this product at maximum settings recorded that thread failing to answer its message loop for
/// 607-1734 ms at a stretch, peaking past a 2000 ms probe ceiling. The panic key now owns its own
/// thread and its own pump, and the third fact below drives a real injected chord past a thread that
/// never pumps again.</para>
///
/// <para>What a green run still does not prove: that a human sees anything (headed), and what
/// happens after the press reaches the shell — the answer to a UI thread that never takes it is
/// <c>PanicWatchdog</c>'s, pinned in <see cref="EmergencyStopTests"/>.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class PanicKeyTests : RealDesktopFacts
{
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkEscape = 0x1B;

    /// <summary>True on the host that has the mechanism at all. The first fact is keyed to this —
    /// the convention <see cref="TrayCapabilityTests"/> uses — so that off Windows it asserts the
    /// TYPED REFUSAL rather than skipping.</summary>
    private static bool WindowsHost => OperatingSystem.IsWindows();

    /// <summary>
    /// <b>USER32's answer, asked only where USER32 exists.</b> The three facts below read
    /// <c>IsWindow</c> directly against the panic key's own window, and the first of them is KEYED
    /// rather than gated — so it runs off Windows, where a <c>[DllImport("user32.dll")]</c> call is
    /// not a false reading but a <c>DllNotFoundException</c> that never considered the platform at
    /// all. That is what it did on Linux on 2026-08-25. The short-circuit is the convention every
    /// probe in this suite already follows (<c>PointerWindowProbe.WindowExists</c>,
    /// <c>SurfaceTeardownObservations.Os.IsWindowHandle</c>), and the <c>window != 0</c> half closes
    /// the other end of the same defect: <c>IsWindow(0)</c> would be a question about a window that
    /// was never created.
    /// </summary>
    private static bool IsARealWindow(nint window) => WindowsHost && window != 0 && IsWindow(window);

    [Fact]
    public void TheChordIsHeldSystemWide_AndTheOperatingSystemConfirmsTheWindowItIsPostedTo()
    {
        using var key = new Win32PanicKey();
        var state = key.Arm();

        Assert.True(state is CapabilityState.Available == WindowsHost,
            $"on this host (Windows = {WindowsHost}) arming the panic key produced {state}. A Windows host must "
            + "really hold the chord; anywhere else the capability must refuse in type rather than pretend");
        Assert.Equal(WindowsHost, key.IsArmed);

        // The claim is checked against USER32 rather than against the object's own bool: WM_HOTKEY is
        // posted to a WINDOW, so a claim of Available over a window that does not exist would be a
        // panic key with nowhere for the press to land.
        Assert.True(key.OwnerWindow != 0 == WindowsHost,
            "the panic key claims to hold the chord but owns no window for the OS to post WM_HOTKEY to");
        Assert.True(IsARealWindow(key.OwnerWindow) == WindowsHost,
            $"USER32 does not recognise 0x{key.OwnerWindow:X} as a window, so nothing could ever be delivered to it");

        // And the release. A hotkey held past its owner's life is a chord taken away from the whole
        // machine for the rest of the session.
        var window = key.OwnerWindow;
        key.Dispose();
        Assert.False(key.IsArmed);
        Assert.False(IsARealWindow(window),
            "the panic key's owner window survived Dispose, so the chord may still be registered to it");
    }

    /// <summary>
    /// The refusal this fact takes rather than running vacuously. Every reading below is USER32's,
    /// and off Windows — or in a Windows session with no interactive desktop — every one of them is
    /// absent rather than false: no window is created, no chord is granted, and <c>SendInput</c> is
    /// refused. Comparing two absent things is <c>0 == 0</c>, which PASSES having measured nothing.
    /// So the machine question is asked ONCE, as a gate, and everything after it is unconditional —
    /// the idiom <see cref="OverlayTaskSwitcherTests"/> established and <c>floor.json</c>'s
    /// <c>allowedSkips</c> pins by name.
    /// </summary>
    private const string RefusalReason =
        "this run puts a real full-monitor topmost click sink on the interactive desktop, asks USER32 to grant a "
        + "system-wide hotkey, and injects a real Ctrl+Alt+Esc through SendInput. None of RegisterHotKey, "
        + "WM_HOTKEY or SendInput exists off Windows, and none of them can be exercised in a Windows session with "
        + "no desktop — every reading would be about a window that was never created and a chord nobody holds, "
        + "which is a PASS with nothing behind it. This refuses by name instead. The X11 and Wayland halves of "
        + "this invariant are unmeasured and no green run here says anything about them";

    [Fact]
    public void ThePressARRIVESThroughAFullMonitorTopmostClickSink_AndTheDISPOSEDKeyMissesTheSameDelivery()
    {
        Assert.SkipUnless(PointerWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        using var overlay = new Win32OverlayPresence();
        var bounds = OverlayDisplays.Enumerate() is [var primary, ..]
            ? primary.Bounds
            : new OverlayBounds(0, 0, 640, 480);

        // The surface the owner was actually trapped under: the full size of a monitor, topmost, and
        // ClickThrough:false — the port's own click-ABSORBING arm, the one the board records as
        // deliberate for the pointer target, the lock card and the mandatory video. With this up,
        // there is no pointer route to the shell at all, so a POINTER affordance could not be the
        // escape even in principle. That is the whole reason the escape has to be a key.
        var presented = overlay.Present(new OverlaySurfaceRequest(bounds, 1.0, ClickThrough: false));
        Assert.True(presented is CapabilityState.Available,
            $"the click sink could not be put up ({presented}); with nothing over the screen this fact would be "
            + "about an unobstructed desktop and would prove nothing about the situation it names");

        var centre = bounds.Centre;
        Assert.True(WindowFromPoint(new Point { X = centre.X, Y = centre.Y }) != 0,
            "no window owns the centre of the display, so the hit-test instrument is not reading anything");

        // Pressed is raised on the panic key's OWN thread now, so the counter crosses a thread
        // boundary and is read as one: the instrument does not carry the defect it measures.
        using var live = new Win32PanicKey();
        var pressesOnLive = 0;
        live.Pressed += () => Interlocked.Increment(ref pressesOnLive);
        var armed = live.Arm();
        Assert.True(armed is CapabilityState.Available, $"the chord was not granted ({armed})");

        Assert.True(InjectChord(), "the OS refused the injection, so nothing below measures a delivery");

        TestWait.UntilSync(
            () => { Pump(); return Volatile.Read(ref pressesOnLive) > 0; },
            "the panic chord to reach the app with a full-monitor topmost click sink over the screen",
            () => $"presses={Volatile.Read(ref pressesOnLive)}");

        Assert.Equal(1, Volatile.Read(ref pressesOnLive));

        // ---- the negative control: one delivery, read by a live key and a dead one ----
        live.Dispose();

        using var successor = new Win32PanicKey();
        var pressesOnSuccessor = 0;
        successor.Pressed += () => Interlocked.Increment(ref pressesOnSuccessor);
        var reclaimed = successor.Arm();
        Assert.True(reclaimed is CapabilityState.Available,
            $"the chord could not be re-claimed after the first key was disposed ({reclaimed}) — the disposed key "
            + "never gave it back, so Ctrl+Alt+Esc is now gone from the whole machine");

        var pressesOnLiveBefore = Volatile.Read(ref pressesOnLive);
        Assert.True(InjectChord());

        TestWait.UntilSync(
            () => { Pump(); return Volatile.Read(ref pressesOnSuccessor) > 0; },
            "the re-claimed chord to reach the successor key",
            () => $"successor={Volatile.Read(ref pressesOnSuccessor)} disposed={Volatile.Read(ref pressesOnLive)}");

        Assert.Equal(pressesOnLiveBefore, Volatile.Read(ref pressesOnLive));
        Assert.Equal(1, Volatile.Read(ref pressesOnSuccessor));

        overlay.Withdraw();
    }

    /// <summary>The same gate, for the same reason, on the fact that matters most. Named separately
    /// rather than shared with <see cref="RefusalReason"/> so a reader of either refusal sees what
    /// THAT run would have measured.</summary>
    private const string WedgeRefusalReason =
        "this run asks USER32 for a system-wide hotkey and injects a real Ctrl+Alt+Esc through SendInput while the "
        + "thread that armed the key is deliberately never pumped again. Neither RegisterHotKey nor SendInput "
        + "exists off Windows, and neither can be exercised in a Windows session with no desktop — a green run "
        + "there would be a wedged thread that nothing was ever sent to, which measures nothing at all";

    [Fact]
    public void ThePressARRIVESWhileTheThreadThatARMEDTheKeyNEVERPumpsAgain()
    {
        Assert.SkipUnless(PointerWindowProbe.MachineHasInteractiveDesktop, WedgeRefusalReason);

        // THE STATE THE OWNER WAS ACTUALLY IN. A measurement on this product at maximum settings
        // recorded the UI thread failing to answer its message loop for 607-1734 ms at a stretch,
        // peaking past a 2000 ms probe ceiling, with one core pegged and fifteen idle. WM_HOTKEY is
        // POSTED to the queue of the thread that registered the hotkey — so while that thread was
        // the UI thread, a press during a stall could not be OBSERVED at all, and the escape hatch
        // was asleep exactly when it was needed.
        //
        // This fact arms the key on THIS thread and then never pumps this thread again: the wait
        // below is the approved helper's sleep loop, which dispatches nothing. Before the panic key
        // owned its own thread, that made the press undeliverable and this fact would spend its
        // whole window and red with CONDITION-NEVER-TRUE.
        using var key = new Win32PanicKey();
        var presses = 0;
        key.Pressed += () => Interlocked.Increment(ref presses);
        var armed = key.Arm();
        Assert.True(armed is CapabilityState.Available, $"the chord was not granted ({armed})");

        // Non-vacuity: the window really exists and really belongs to somebody, so "it arrived"
        // below is about a delivery rather than about an object that was never built.
        Assert.True(IsARealWindow(key.OwnerWindow),
            $"USER32 does not recognise 0x{key.OwnerWindow:X} as a window, so nothing could be posted to it");

        Assert.True(InjectChord(), "the OS refused the injection, so nothing below measures a delivery");

        TestWait.UntilSync(
            () => Volatile.Read(ref presses) > 0,
            "the panic chord to be delivered while the thread that armed the key never pumps a message again",
            () => $"presses={Volatile.Read(ref presses)}, ownerWindow=0x{key.OwnerWindow:X}");

        Assert.Equal(1, Volatile.Read(ref presses));
    }

    /// <summary>
    /// Ctrl down, Alt down, Esc down, Esc up, Alt up, Ctrl up — one <c>SendInput</c> batch so
    /// nothing can land between the modifiers and the key. Declared here rather than borrowed from
    /// <see cref="PointerWindowProbe"/> for the reason that probe gives for re-declaring everything:
    /// independent instruments, so two readings are never one code path.
    /// </summary>
    private static bool InjectChord()
    {
        if (!WindowsHost)
        {
            return false;
        }

        var inputs = new Input[6];
        inputs[0] = Down(VkControl);
        inputs[1] = Down(VkMenu);
        inputs[2] = Down(VkEscape);
        inputs[3] = Up(VkEscape);
        inputs[4] = Up(VkMenu);
        inputs[5] = Up(VkControl);
        return SendInput(6, inputs, Marshal.SizeOf<Input>()) == 6;
    }

    private static Input Down(ushort key) =>
        new() { type = InputKeyboard, ki = new KeybdInput { wVk = key } };

    private static Input Up(ushort key) =>
        new() { type = InputKeyboard, ki = new KeybdInput { wVk = key, dwFlags = KeyeventfKeyup } };

    /// <summary>
    /// Drain this thread's message queue. <c>WM_HOTKEY</c> is POSTED, not called back, so nothing
    /// arrives until somebody dispatches it — and WHICH thread that is has changed: the panic key
    /// now owns the window, so it pumps its own queue and this pump is no longer the delivery path.
    /// It is kept because it costs nothing and because a fact that stopped pumping would be silently
    /// asserting something different from the one above it; the fact that the arming thread's pump
    /// is NOT needed is proved separately, by wedging it.
    /// </summary>
    private static void Pump()
    {
        if (!WindowsHost)
        {
            return;
        }

        while (PeekMessageW(out var message, 0, 0, 0, PmRemove))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint PmRemove = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    /// <summary>
    /// <c>INPUT</c> sized for the keyboard arm. The native union is as wide as <c>MOUSEINPUT</c>
    /// (the largest member), so the two trailing words pad this to the 40 bytes
    /// <c>SendInput</c>'s <c>cbSize</c> must be on x64. They are never read.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public KeybdInput ki;
        public int unionPad1;
        public int unionPad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public Point pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out Msg message, nint window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessageW(ref Msg message);
}
