using System.Reflection;
using System.Runtime.InteropServices;
using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The WebView2 permission DENY hook (<see cref="WebViewPermissionDeny"/>), which closes
/// D250: the port could neither grant nor refuse, so WebView2's own prompt was the residual.
///
/// <para><b>THE DEFECT THIS FILE EXISTS TO CATCH IS "IT COMPILED".</b> A COM event handler attached
/// at the wrong vtable slot compiles perfectly, calls something else, and silently never runs — in
/// the one place where the consequence is a microphone. So nothing here asserts that the code
/// builds. A fake <c>ICoreWebView2</c> is constructed in native memory with all 61 slots filled by
/// distinct thunks that record THEIR OWN INDEX when called; the product is pointed at it; and the
/// tests assert which slots were actually entered. The handler pointer the product handed to slot 23
/// is then INVOKED through its own CCW vtable — a real unmanaged call into the real callback — and
/// the deny is asserted at <c>put_State</c>.</para>
///
/// <para><b>WHY THE FAKE IS NOT MERELY SELF-CONSISTENT.</b> Its layout is
/// <see cref="PublishedCoreWebView2VtableOrder"/>, transcribed from the published
/// <c>ICoreWebView2Vtbl</c> (Microsoft.Web.WebView2 <c>build/native/include/WebView2.h</c>,
/// identical in 1.0.2535.41 <c>:3287-3618</c> and 1.0.3179.45 <c>:2875-3206</c>, 61 slots).
/// <see cref="PublishedVtableOrder_PutsPermissionRequestedAt23And24_AndReproducesTheShippingProcessFailedSlots"/>
/// asserts that the SAME transcription predicts the slot constants of
/// <see cref="DtrhProcessFailed"/>, which have been shipping in this tree since the DTRH host landed and were not
/// chosen here. So any insertion, deletion or shift at or before slot 25 moves index 25 off
/// <c>add_ProcessFailed</c> and reddens this file. Avalonia 12.0.1 corroborates independently, from
/// metadata that is not a positional count: its generated
/// <c>Avalonia.Controls.Win.WebView2.Interop.ICoreWebView2</c> vtable names its fields
/// <c>add_PermissionRequested_23</c> / <c>remove_PermissionRequested_24</c> /
/// <c>add_ProcessFailed_25</c> and carries them at those field indices.</para>
///
/// <para><b>WHY THE ANSWER AND THE SUBSCRIPTION ARE TESTED SEPARATELY.</b> The decision half is pure
/// vtable traffic and runs on EVERY platform, so the deny facts below are cross-platform facts, not
/// Windows-only ones. Only the subscription needs a CCW (<c>GetComInterfaceForObject</c>), which is
/// a Windows COM facility; the three facts that exercise it say so and assert the product's typed
/// refusal elsewhere.</para>
///
/// <para><b>WHAT THESE FACTS DO NOT ESTABLISH.</b> That a real Chromium raises
/// <c>PermissionRequested</c> for <c>getUserMedia</c>, that it honours the deny, or that no prompt is
/// painted. Those need a headed Windows run on the Goon voice screen and are NOT discharged here
/// (<c>client/docs/verification-harness.md:56-64</c>: this is neither <c>draw-verified</c> nor
/// <c>presentation-verified</c>). What IS established is that the subscription lands on slot 23 and
/// nothing else, that the handler runs when that subscription is exercised, and that it writes
/// <c>DENY</c>.</para>
/// </summary>
public class WebViewPermissionTests
{
    // ---------- the published interface, transcribed (never counted by eye) ----------

    /// <summary>ICoreWebView2Vtbl in declaration order — 61 entries, IUnknown's three first.
    /// Transcribed from WebView2.h (1.0.2535.41 :3287-3618; identical order in
    /// 1.0.3179.45 :2875-3206).</summary>
    private static readonly string[] PublishedCoreWebView2VtableOrder =
    [
        "QueryInterface", "AddRef", "Release",
        "get_Settings", "get_Source", "Navigate", "NavigateToString",
        "add_NavigationStarting", "remove_NavigationStarting",
        "add_ContentLoading", "remove_ContentLoading",
        "add_SourceChanged", "remove_SourceChanged",
        "add_HistoryChanged", "remove_HistoryChanged",
        "add_NavigationCompleted", "remove_NavigationCompleted",
        "add_FrameNavigationStarting", "remove_FrameNavigationStarting",
        "add_FrameNavigationCompleted", "remove_FrameNavigationCompleted",
        "add_ScriptDialogOpening", "remove_ScriptDialogOpening",
        "add_PermissionRequested", "remove_PermissionRequested",
        "add_ProcessFailed", "remove_ProcessFailed",
        "AddScriptToExecuteOnDocumentCreated", "RemoveScriptToExecuteOnDocumentCreated",
        "ExecuteScript", "CapturePreview", "Reload",
        "PostWebMessageAsJson", "PostWebMessageAsString",
        "add_WebMessageReceived", "remove_WebMessageReceived",
        "CallDevToolsProtocolMethod", "get_BrowserProcessId",
        "get_CanGoBack", "get_CanGoForward", "GoBack", "GoForward",
        "GetDevToolsProtocolEventReceiver", "Stop",
        "add_NewWindowRequested", "remove_NewWindowRequested",
        "add_DocumentTitleChanged", "remove_DocumentTitleChanged", "get_DocumentTitle",
        "AddHostObjectToScript", "RemoveHostObjectFromScript", "OpenDevToolsWindow",
        "add_ContainsFullScreenElementChanged", "remove_ContainsFullScreenElementChanged",
        "get_ContainsFullScreenElement",
        "add_WebResourceRequested", "remove_WebResourceRequested",
        "AddWebResourceRequestedFilter", "RemoveWebResourceRequestedFilter",
        "add_WindowCloseRequested", "remove_WindowCloseRequested",
    ];

    /// <summary>ICoreWebView2PermissionRequestedEventArgs3Vtbl (WebView2.h:39805-39880) — the args
    /// shape a current runtime hands the handler.</summary>
    private const int ArgsSlotCount = 13;
    private const int GetPermissionKindSlot = 4;   // WebView2.h:39534-39584
    private const int PutStateSlot = 7;            // same
    private const int PutSavesInProfileSlot = 12;  // WebView2.h:39805-39880

    /// <summary>ComImport + InterfaceIsIUnknown ⇒ Invoke at slot 3 (handler IID
    /// 15e1c6a3-c72a-4df3-91d7-d097fbec6bfd, WebView2.h:39951).</summary>
    private const int HandlerInvokeSlot = 3;

    private const int AddSlot = 23;
    private const int RemoveSlot = 24;

    private static readonly Guid PermissionRequestedEventArgs3Iid =
        new("e61670bc-3dce-4177-86d2-c629ae3cb6ac");

    /// <summary>The published constants, transcribed from WebView2.h:1984-2004 (identical in both
    /// SDKs) — asserted against the OBSERVED write, never against the product's own constant. Flip
    /// <c>WebViewPermissionDeny.DenyState</c> to Allow and every deny fact below reddens; compare
    /// against the product constant instead and none of them would.</summary>
    private const int PublishedDenyState = 2;
    private const int PublishedAutoplayKind = 9;

    private const int MicrophoneKind = 1;
    private const int SHr = 0;
    private const int EFail = unchecked((int)0x80004005);
    private const int ENotImpl = unchecked((int)0x80004001);
    private const int ENoInterface = unchecked((int)0x80004002);
    private const long DefaultToken = 0x5135;

    // ---------- the slot table itself ----------

    [Fact]
    public void PublishedVtableOrder_PutsPermissionRequestedAt23And24_AndReproducesTheShippingProcessFailedSlots()
    {
        Assert.Equal(61, PublishedCoreWebView2VtableOrder.Length);

        var addPermission = Array.IndexOf(PublishedCoreWebView2VtableOrder, "add_PermissionRequested");
        var removePermission = Array.IndexOf(PublishedCoreWebView2VtableOrder, "remove_PermissionRequested");
        Assert.Equal(AddSlot, addPermission);
        Assert.Equal(RemoveSlot, removePermission);
        Assert.Equal(addPermission, PrivateSlot(typeof(WebViewPermissionDeny), "AddPermissionRequestedSlot"));
        Assert.Equal(removePermission, PrivateSlot(typeof(WebViewPermissionDeny), "RemovePermissionRequestedSlot"));

        // THE ANCHOR: the same transcription must predict the slots this tree has been SHIPPING since
        // the DTRH host landed, which nothing in this packet chose. Any drift at or before 25 reds here.
        Assert.Equal(25, Array.IndexOf(PublishedCoreWebView2VtableOrder, "add_ProcessFailed"));
        Assert.Equal(26, Array.IndexOf(PublishedCoreWebView2VtableOrder, "remove_ProcessFailed"));
        Assert.Equal(25, PrivateSlot(typeof(DtrhProcessFailed), "AddProcessFailedSlot"));
        Assert.Equal(26, PrivateSlot(typeof(DtrhProcessFailed), "RemoveProcessFailedSlot"));

        // The args slots the answer writes through, and the two published constants it writes.
        Assert.Equal(GetPermissionKindSlot, PrivateSlot(typeof(WebViewPermissionDeny), "GetPermissionKindSlot"));
        Assert.Equal(PutStateSlot, PrivateSlot(typeof(WebViewPermissionDeny), "PutStateSlot"));
        Assert.Equal(PutSavesInProfileSlot, PrivateSlot(typeof(WebViewPermissionDeny), "PutSavesInProfileSlot"));
        Assert.Equal(PublishedDenyState, WebViewPermissionDeny.DenyState);
        Assert.Equal(PublishedAutoplayKind, WebViewPermissionDeny.AutoplayKind);
    }

    // ---------- the answer: what the handler DOES, on every platform ----------

    [Fact]
    public void TheAnswer_DeniesTheMicrophone()
    {
        using var args = new FakeArgs(MicrophoneKind);

        var decisions = Answer(args.Pointer);

        Assert.Equal([PublishedDenyState], args.StatesWritten);
        Assert.Contains(GetPermissionKindSlot, args.Com.Calls);
        Assert.Contains(PutStateSlot, args.Com.Calls);
        Assert.Equal([(MicrophoneKind, WebViewPermissionDeny.Decision.Denied)], decisions);
    }

    [Fact]
    public void TheAnswer_LeavesAutoplayAtTheBrowserDefault()
    {
        using var args = new FakeArgs(PublishedAutoplayKind);

        var decisions = Answer(args.Pointer);

        // Untouched: the state write never happens, so Chromium's own autoplay policy — which every
        // WebView2 host here already sets with --autoplay-policy=no-user-gesture-required — still
        // decides, and the pages keep their audio and video.
        Assert.Empty(args.StatesWritten);
        Assert.DoesNotContain(PutStateSlot, args.Com.Calls);
        Assert.Equal(
            [(PublishedAutoplayKind, WebViewPermissionDeny.Decision.LeftAtBrowserDefault)],
            decisions);
    }

    [Fact]
    public void TheAnswer_DeniesEveryOtherPublishedKind_AndKindsThisBuildHasNeverSeen()
    {
        // 0..12 are the published COREWEBVIEW2_PERMISSION_KIND values (WebView2.h:1984-1996) minus
        // autoplay; 99 and int.MaxValue stand for a kind a future runtime adds. Deny is the DEFAULT
        // arm, so a kind nobody has enumerated is refused rather than prompted.
        int[] kinds = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 99, int.MaxValue];
        Assert.NotEmpty(kinds);

        foreach (var kind in kinds)
        {
            using var args = new FakeArgs(kind);
            var decisions = Answer(args.Pointer);
            Assert.Equal([PublishedDenyState], args.StatesWritten);
            Assert.Equal([(kind, WebViewPermissionDeny.Decision.Denied)], decisions);
        }
    }

    [Fact]
    public void TheAnswer_UnbanksItself_BySettingSavesInProfileFalse()
    {
        using var args = new FakeArgs(MicrophoneKind, offersArgs3: true);

        Answer(args.Pointer);

        // GoonHostService.cs:440-446: a banked answer is served from the profile WITHOUT raising the
        // event at all, so an unbanked deny is the difference between a policy this code owns and a
        // file in browser_data_* that outlives it. Upstream writes the same flag at :499.
        Assert.Equal([0], args.SavesInProfileWritten);
        Assert.Equal([PublishedDenyState], args.StatesWritten);
        // The args3 pointer QI handed out was released — no reference leaks per event.
        Assert.Equal(1, args.Com.RefCount);
    }

    [Fact]
    public void TheAnswer_OnARuntimeWithoutArgs3_StillDenies()
    {
        using var args = new FakeArgs(MicrophoneKind, offersArgs3: false);

        var decisions = Answer(args.Pointer);

        // E_NOINTERFACE is the EXPECTED answer on an older runtime, not an error. The consequence is
        // named rather than implied away: the deny still lands, but it is then banked in the profile
        // and the event may stop being raised — strictly no weaker than the deny.
        Assert.Equal([PublishedDenyState], args.StatesWritten);
        Assert.Empty(args.SavesInProfileWritten);
        Assert.Equal([(MicrophoneKind, WebViewPermissionDeny.Decision.Denied)], decisions);
    }

    [Fact]
    public void TheAnswer_WhenTheKindCannotBeRead_Denies()
    {
        using var args = new FakeArgs(MicrophoneKind, getKindHr: EFail);

        var decisions = Answer(args.Pointer);

        // An unreadable question is not a reason to hand the decision back to the browser.
        Assert.Equal([PublishedDenyState], args.StatesWritten);
        Assert.Equal([(WebViewPermissionDeny.UnreadableKind, WebViewPermissionDeny.Decision.Denied)], decisions);
    }

    [Fact]
    public void TheAnswer_WhenTheStateWriteFails_SaysSo_RatherThanReportingADenyThatDidNotHappen()
    {
        using var args = new FakeArgs(MicrophoneKind, putStateHr: EFail);

        var decisions = Answer(args.Pointer);

        Assert.Equal([(MicrophoneKind, WebViewPermissionDeny.Decision.WriteFailed)], decisions);
        Assert.Empty(args.SavesInProfileWritten);   // nothing is unbanked for an answer never given
    }

    [Fact]
    public void TheAnswer_ToleratesNullArgs()
    {
        var decisions = Answer(IntPtr.Zero);

        Assert.Empty(decisions);
    }

    // ---------- typed outcomes ----------

    [Fact]
    public void TryAttach_ZeroPointer_IsTypedUnavailable_NeverThrows_NeverFakes()
    {
        var outcome = WebViewPermissionDeny.TryAttach(IntPtr.Zero, (_, _) => { });

        var unavailable = Assert.IsType<WebViewPermissionDeny.AttachOutcome.Unavailable>(outcome);
        Assert.False(string.IsNullOrWhiteSpace(unavailable.Detail));
        // Both platform arms are refusals with a stable code; which one is asserted by
        // TheSubscription_LandsOnSlot23_AndTypesEveryFailure (Windows) and by the Linux arm's own
        // detail text. Never "attach-failed": nothing was attempted.
        Assert.NotEqual("attach-failed", unavailable.Code);
    }

    // ---------- the subscription: Windows COM, because a CCW is a Windows facility ----------

    [Fact]
    public void TheSubscription_LandsOnSlot23_AndTypesEveryFailure()
    {
        if (!WindowsOrTypedUnsupported())
        {
            return;
        }

        using var core = NewCore();
        var attach = Attach(core);

        var attached = Assert.IsType<WebViewPermissionDeny.AttachOutcome.Attached>(attach.Outcome);
        // AddRef (slot 1) then add_PermissionRequested (slot 23). Nothing else was entered: a wrong
        // constant shows up here as a different index, not as a silent no-op.
        Assert.Equal([1, AddSlot], core.Calls);
        attached.Signal.Dispose();

        // A zero pointer on Windows is the invalid-handle arm, not the platform arm.
        var zero = Assert.IsType<WebViewPermissionDeny.AttachOutcome.Unavailable>(
            WebViewPermissionDeny.TryAttach(IntPtr.Zero, null));
        Assert.Equal("invalid-handle", zero.Code);

        // And a subscription the browser refuses is typed, named and released — never swallowed and
        // never reported as attached.
        using var failing = NewCore();
        var failed = Assert.IsType<WebViewPermissionDeny.AttachOutcome.Unavailable>(
            Attach(failing, addHr: EFail).Outcome);
        Assert.Equal("attach-failed", failed.Code);
        Assert.Contains("0x80004005", failed.Detail);
        Assert.Equal(1, failing.RefCount);
    }

    [Fact]
    public void TheHandlerAttachedAtSlot23_Fires_AndDeniesTheMicrophone()
    {
        if (!WindowsOrTypedUnsupported())
        {
            return;
        }

        using var core = NewCore();
        var decisions = new List<(int Kind, WebViewPermissionDeny.Decision Decision)>();
        var attach = Attach(core, (k, d) => decisions.Add((k, d)));
        var attached = Assert.IsType<WebViewPermissionDeny.AttachOutcome.Attached>(attach.Outcome);
        Assert.NotEqual(IntPtr.Zero, attach.HandlerPtr);

        // THE WHOLE POINT: the pointer handed to slot 23 is invoked through its own CCW vtable, the
        // way WebView2 would invoke it, and the deny comes out the other side.
        using var args = new FakeArgs(MicrophoneKind);
        var hr = InvokeHandler(attach.HandlerPtr, core.Pointer, args.Pointer);

        Assert.Equal(SHr, hr);
        Assert.Equal([PublishedDenyState], args.StatesWritten);
        Assert.Equal([(MicrophoneKind, WebViewPermissionDeny.Decision.Denied)], decisions);
        Assert.Equal([0], args.SavesInProfileWritten);
        attached.Signal.Dispose();
    }

    [Fact]
    public void Dispose_DetachesAtSlot24_ToleratesAZombieBrowser_AndIsIdempotent()
    {
        if (!WindowsOrTypedUnsupported())
        {
            return;
        }

        using var core = NewCore();
        var remove = new RemoveImpl { Core = core, Hr = SHr };
        core.Set(RemoveSlot, ProductDelegate("RemovePermissionRequestedDelegate", remove, nameof(RemoveImpl.Call)));

        var attached = Assert.IsType<WebViewPermissionDeny.AttachOutcome.Attached>(
            Attach(core, token: DefaultToken).Outcome);
        core.ClearCalls();

        attached.Signal.Dispose();
        // remove_PermissionRequested (24) with OUR token, then Release (2) of the ref TryAttach took.
        Assert.Equal([RemoveSlot, 2], core.Calls);
        Assert.Equal(DefaultToken, remove.TokenSeen);
        Assert.Equal(1, core.RefCount);

        core.ClearCalls();
        attached.Signal.Dispose();
        attached.Signal.Dispose();
        Assert.Empty(core.Calls);   // idempotent: a dead browser is never talked to twice

        // A remove that FAILS is the expected zombie-browser case: tolerated, never thrown.
        using var zombie = NewCore();
        zombie.Set(RemoveSlot, ProductDelegate("RemovePermissionRequestedDelegate",
            new RemoveImpl { Core = zombie, Hr = EFail }, nameof(RemoveImpl.Call)));
        var zombieSignal = Assert.IsType<WebViewPermissionDeny.AttachOutcome.Attached>(Attach(zombie).Outcome);
        zombieSignal.Signal.Dispose();
        Assert.Equal(1, zombie.RefCount);
    }

    // ---------- the wiring: attached SOMEWHERE, and the uncovered hosts named ----------

    [Fact]
    public void EveryWebView2HostIsCensused_AndTheThreeInScopeAttachAndDisposeTheDenyHook()
    {
        var src = Path.Combine(FindRepoRoot(), "client", "src", "CcpClient.Desktop");

        // The census first: a NEW WebView2 host must not be able to appear without this guard
        // noticing, because a host with no deny hook is exactly the open residual D250 describes.
        string[] expectedHosts =
        [
            Path.Combine("Features", "Chaos", "ChaosTunnelWindow.cs"),
            Path.Combine("Features", "Dtrh", "DtrhHostWindow.axaml.cs"),
            Path.Combine("Features", "Dtrh", "DtrhLoomWindow.axaml.cs"),
            Path.Combine("Features", "Goon", "GoonHostWindow.axaml.cs"),
            Path.Combine("Features", "Intake", "IntakeHostWindow.axaml.cs"),
        ];
        var actualHosts = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("new NativeWebView()", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(src, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedHosts.OrderBy(f => f, StringComparer.Ordinal).ToArray(), actualHosts);

        // COVERED BY THIS PACKET: the two DTRH surfaces and the Goon practice host — the surface D250 was
        // named for. NOT covered, and stated here so it cannot be read off the green: Intake and the
        // Chaos tunnel embed the same WebView2 and were outside this packet's File Scope, so the
        // browser still owns the prompt there.
        string[] covered =
        [
            Path.Combine("Features", "Dtrh", "DtrhHostWindow.axaml.cs"),
            Path.Combine("Features", "Dtrh", "DtrhLoomWindow.axaml.cs"),
            Path.Combine("Features", "Goon", "GoonHostWindow.axaml.cs"),
        ];
        Assert.NotEmpty(covered);
        foreach (var host in covered)
        {
            var text = File.ReadAllText(Path.Combine(src, host));
            Assert.Contains("WebViewPermissionDeny.TryAttach(wv2.CoreWebView2", text);
            // AND IT IS CALLED. Measured, not assumed: deleting only the call site from the
            // AdapterCreated wiring left every other fact in this file green, which is a hook that
            // exists and never runs — this session's signature defect, in the guard meant to catch it.
            Assert.Contains("AttachPermissionDeny();", text);
            Assert.Contains("_permissionDenySignal = attached.Signal;", text);
            Assert.Contains("_permissionDenySignal?.Dispose();", text);
            // A failed attach must stay LOUD: the port may not become quieter about this than it was
            // when it had no handler at all.
            Assert.Contains("DENY hook UNAVAILABLE", text);
        }
    }

    // ---------- harness ----------

    /// <summary>Drive the product's decision half directly (private static, reached the same way
    /// this file reaches the private slot constants). No CCW is involved, so this runs everywhere.</summary>
    private static List<(int Kind, WebViewPermissionDeny.Decision Decision)> Answer(IntPtr args)
    {
        var method = typeof(WebViewPermissionDeny).GetMethod("AnswerRequest", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "WebViewPermissionDeny.AnswerRequest not found — the deny decision moved and this file must move with it");
        var decisions = new List<(int, WebViewPermissionDeny.Decision)>();
        var onDecision = (Action<int, WebViewPermissionDeny.Decision>)((k, d) => decisions.Add((k, d)));
        method.Invoke(null, [args, onDecision]);
        return decisions;
    }

    /// <summary>
    /// WHY THE FAKE'S SLOTS ARE BUILT FROM THE PRODUCT'S OWN PRIVATE DELEGATE TYPES, AND NOT FROM
    /// TYPES DECLARED HERE. Measured, not assumed: <c>Marshal.GetFunctionPointerForDelegate</c>
    /// registers the managed delegate against the stub address it hands back, and
    /// <c>Marshal.GetDelegateForFunctionPointer&lt;T&gt;</c> on that address returns THE SAME
    /// DELEGATE OBJECT, then type-checks it — so a fake vtable filled with locally declared delegate
    /// types makes the product's own read fail with
    /// <c>InvalidCastException: Unable to cast object of type 'AddDelegate' to type
    /// 'AddPermissionRequestedDelegate'</c> before any slot arithmetic is exercised. Building each
    /// slot as the product's own type through <see cref="Delegate.CreateDelegate(Type, object,
    /// MethodInfo)"/> is what lets the real read run. It also pins the product's ABI: rename or
    /// reshape one of those delegates and this file says so by name.
    ///
    /// <para>THE FILLER SLOTS ARE THE SAME TYPE AS <c>add_PermissionRequested</c> ON PURPOSE. That
    /// makes a wrong ADD slot — the load-bearing drift — record its own index and fail with the
    /// number rather than throwing. A wrong slot for any of the other typed reads throws the cast
    /// above instead, which is still red, just less specific. IUnknown's AddRef/Release (slots 1-2)
    /// are entered by <c>Marshal.AddRef</c>/<c>Release</c> as genuine native calls, so an ordinary
    /// managed delegate is a valid callee there.</para>
    /// </summary>
    private static Delegate ProductDelegate(string delegateTypeName, object target, string method)
    {
        var type = typeof(WebViewPermissionDeny).GetNestedType(delegateTypeName, BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"WebViewPermissionDeny.{delegateTypeName} not found — the interop shape this fake imitates has " +
                "moved, and the fake must move with it rather than testing a shape nobody ships");
        var info = target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{method} not found");
        return Delegate.CreateDelegate(type, target, info);
    }

    /// <summary>True on Windows. Elsewhere the honest named limit is ASSERTED (never skipped): the
    /// CCW these three facts need is a Windows COM facility, and the product refuses first. The
    /// DECISION half above carries no such predicate and runs on every platform.</summary>
    private static bool WindowsOrTypedUnsupported()
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var outcome = WebViewPermissionDeny.TryAttach(new IntPtr(0x1000), (_, _) => { });
        var unavailable = Assert.IsType<WebViewPermissionDeny.AttachOutcome.Unavailable>(outcome);
        Assert.Equal("unsupported-platform", unavailable.Code);
        Assert.Contains("WebKitGTK", unavailable.Detail);
        return false;
    }

    /// <summary>The FindRepoRoot precedent (ChaosTunnelLoopbackTests.cs:404-418): walk up to the
    /// solution, never skip when it is missing.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} — the host census refuses to skip");
    }

    private static int PrivateSlot(Type type, string field)
    {
        var info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{type.Name}.{field} not found — the slot guard refuses to skip");
        return (int)(info.GetRawConstantValue() ?? throw new InvalidOperationException($"{type.Name}.{field} is not a constant"));
    }

    private static FakeCom NewCore() => new(PublishedCoreWebView2VtableOrder.Length);

    private static (WebViewPermissionDeny.AttachOutcome Outcome, IntPtr HandlerPtr) Attach(
        FakeCom core,
        Action<int, WebViewPermissionDeny.Decision>? onDecision = null,
        int addHr = SHr,
        long token = DefaultToken)
    {
        var add = new AddImpl { Core = core, Token = token, Hr = addHr };
        core.Set(AddSlot, ProductDelegate("AddPermissionRequestedDelegate", add, nameof(AddImpl.Call)));

        var outcome = WebViewPermissionDeny.TryAttach(core.Pointer, onDecision);
        return (outcome, add.HandlerPtr);
    }

    /// <summary>Call the handler's own <c>Invoke</c> through its CCW vtable — an unmanaged call into
    /// the real callback, exactly as WebView2 would make it. (This pointer comes from a real CCW, not
    /// from a managed delegate, so the runtime builds a fresh stub for it.)</summary>
    private static int InvokeHandler(IntPtr handlerPtr, IntPtr sender, IntPtr args)
    {
        var vtable = Marshal.ReadIntPtr(handlerPtr);
        var fn = Marshal.ReadIntPtr(vtable, HandlerInvokeSlot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<InvokeDelegate>(fn)(handlerPtr, sender, args);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint RefCountDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InvokeDelegate(IntPtr self, IntPtr sender, IntPtr args);

    // ---------- slot implementations (signatures must match the product's delegates exactly) ----------

    private sealed class SlotRecorder
    {
        public FakeCom Core { get; init; } = null!;

        public int Slot { get; init; }

        public int Call(IntPtr self, IntPtr a, out long token)
        {
            Core.Record(Slot);
            token = 0;
            return ENotImpl;
        }
    }

    private sealed class AddImpl
    {
        public FakeCom Core { get; init; } = null!;

        public long Token { get; init; }

        public int Hr { get; init; }

        public IntPtr HandlerPtr { get; private set; }

        public int Call(IntPtr self, IntPtr handler, out long token)
        {
            Core.Record(AddSlot);
            HandlerPtr = handler;
            token = Token;
            return Hr;
        }
    }

    private sealed class RemoveImpl
    {
        public FakeCom Core { get; init; } = null!;

        public int Hr { get; init; }

        public long TokenSeen { get; private set; }

        public int Call(IntPtr self, long token)
        {
            Core.Record(RemoveSlot);
            TokenSeen = token;
            return Hr;
        }
    }

    private sealed class QueryInterfaceImpl
    {
        public FakeCom Core { get; init; } = null!;

        public Func<Guid, IntPtr> Answer { get; init; } = _ => IntPtr.Zero;

        public int Call(IntPtr self, ref Guid iid, out IntPtr ppv)
        {
            Core.Record(0);
            ppv = Answer(iid);
            if (ppv == IntPtr.Zero)
            {
                return ENoInterface;
            }

            Core.AddRefFromQueryInterface();
            return SHr;
        }
    }

    private sealed class GetKindImpl
    {
        public FakeCom Core { get; init; } = null!;

        public int Kind { get; init; }

        public int Hr { get; init; }

        public int Call(IntPtr self, out int kind)
        {
            Core.Record(GetPermissionKindSlot);
            kind = Kind;
            return Hr;
        }
    }

    private sealed class PutStateImpl
    {
        public FakeCom Core { get; init; } = null!;

        public int Hr { get; init; }

        public List<int> Written { get; } = [];

        public int Call(IntPtr self, int state)
        {
            Core.Record(PutStateSlot);
            if (Hr >= 0)
            {
                Written.Add(state);
            }

            return Hr;
        }
    }

    private sealed class PutSavesImpl
    {
        public FakeCom Core { get; init; } = null!;

        public List<int> Written { get; } = [];

        public int Call(IntPtr self, int value)
        {
            Core.Record(PutSavesInProfileSlot);
            Written.Add(value);
            return SHr;
        }
    }

    /// <summary>A COM object shaped like the published interface: one vtable of N slots, every slot a
    /// DISTINCT thunk that records its own index. That is what makes "which slot did the product
    /// enter" an observation instead of an inference.</summary>
    private sealed class FakeCom : IDisposable
    {
        private readonly IntPtr _vtable;
        private readonly IntPtr _self;
        private readonly List<Delegate> _roots = [];
        private readonly List<int> _calls = [];

        public FakeCom(int slots)
        {
            _vtable = Marshal.AllocHGlobal(slots * IntPtr.Size);
            _self = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_self, _vtable);

            for (var i = 0; i < slots; i++)
            {
                var recorder = new SlotRecorder { Core = this, Slot = i };
                Set(i, ProductDelegate("AddPermissionRequestedDelegate", recorder, nameof(SlotRecorder.Call)));
            }

            Set(0, ProductDelegate("QueryInterfaceDelegate",
                new QueryInterfaceImpl { Core = this, Answer = iid => OnQueryInterface?.Invoke(iid) ?? IntPtr.Zero },
                nameof(QueryInterfaceImpl.Call)));
            Set(1, (RefCountDelegate)(_ =>
            {
                _calls.Add(1);
                return (uint)++RefCount;
            }));
            Set(2, (RefCountDelegate)(_ =>
            {
                _calls.Add(2);
                return (uint)--RefCount;
            }));
        }

        public IReadOnlyList<int> Calls => _calls;

        public IntPtr Pointer => _self;

        public int RefCount { get; private set; } = 1;

        public Func<Guid, IntPtr>? OnQueryInterface { get; set; }

        public void Set(int slot, Delegate impl)
        {
            _roots.Add(impl);
            Marshal.WriteIntPtr(_vtable, slot * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(impl));
        }

        public void Record(int slot) => _calls.Add(slot);

        public void AddRefFromQueryInterface() => RefCount++;

        public void ClearCalls() => _calls.Clear();

        public void Dispose()
        {
            Marshal.FreeHGlobal(_self);
            Marshal.FreeHGlobal(_vtable);
            GC.KeepAlive(_roots);
        }
    }

    /// <summary>An ICoreWebView2PermissionRequestedEventArgs3-shaped object, recording what the
    /// answer wrote to it.</summary>
    private sealed class FakeArgs : IDisposable
    {
        private readonly PutStateImpl _putState;
        private readonly PutSavesImpl _putSaves;

        public FakeArgs(int kind = MicrophoneKind, int getKindHr = SHr, int putStateHr = SHr, bool offersArgs3 = true)
        {
            Com = new FakeCom(ArgsSlotCount);

            // The args3 pointer QI hands out is this same object (a real runtime returns the same COM
            // identity for a derived interface).
            Com.OnQueryInterface = iid => offersArgs3 && iid == PermissionRequestedEventArgs3Iid
                ? Com.Pointer
                : IntPtr.Zero;

            Com.Set(GetPermissionKindSlot, ProductDelegate("GetPermissionKindDelegate",
                new GetKindImpl { Core = Com, Kind = kind, Hr = getKindHr }, nameof(GetKindImpl.Call)));

            _putState = new PutStateImpl { Core = Com, Hr = putStateHr };
            Com.Set(PutStateSlot, ProductDelegate("PutStateDelegate", _putState, nameof(PutStateImpl.Call)));

            _putSaves = new PutSavesImpl { Core = Com };
            Com.Set(PutSavesInProfileSlot, ProductDelegate("PutSavesInProfileDelegate", _putSaves, nameof(PutSavesImpl.Call)));
        }

        public FakeCom Com { get; }

        public IntPtr Pointer => Com.Pointer;

        public IReadOnlyList<int> StatesWritten => _putState.Written;

        public IReadOnlyList<int> SavesInProfileWritten => _putSaves.Written;

        public void Dispose() => Com.Dispose();
    }
}
