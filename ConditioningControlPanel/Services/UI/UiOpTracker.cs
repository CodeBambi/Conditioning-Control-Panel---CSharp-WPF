using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Names the dispatcher operation the UI thread is currently inside.
///
/// WHY (ccp-bugs #1189 / #1179 / #1159 / #984): every report in the sudden-freeze family shows a
/// Send-priority dispatcher operation that has been running for 71-137 seconds with
/// <c>lockCardRunning=True</c> and no stack. "A dispatcher op is stuck" is not an actionable fact;
/// "LockCardWindow.EmergencyRecoveryCleanup has been stuck for 92s at Send priority" is. This class
/// is the cheap half of that answer (<see cref="UiThreadStacks"/> is the expensive half).
///
/// HOW: one subscription to <see cref="DispatcherHooks"/> covers all ~440 BeginInvoke/Invoke call
/// sites at once, which no per-call-site change could. OperationStarted fires on the dispatcher
/// thread immediately before the delegate runs and OperationCompleted/Aborted after, so a push/pop
/// pair leaves the WEDGED operation on top of the stack exactly when the watchdog reads it. Nested
/// dispatcher frames nest correctly because Finish pops back to the matching entry.
///
/// COST CONTRACT (this runs on EVERY dispatcher operation, so it must be ~free): Start is one
/// array store plus a volatile int write, Finish a scan of an at-most-32-entry array. No
/// reflection, no allocation, no string work on the hot path - the delegate's name is resolved
/// lazily in <see cref="Describe"/>, once per hang, on the watchdog thread.
/// </summary>
public static class UiOpTracker
{
    /// <summary>Real nesting is 1-3 (a nested pump inside a modal); the cap only exists so a
    /// pathological re-entry cannot grow this into a leak.</summary>
    private const int MaxDepth = 32;

    private struct Slot
    {
        public DispatcherOperation? Op;
        public long StartedTick;
    }

    private static readonly Slot[] _stack = new Slot[MaxDepth];
    private static int _depth;          // occupied slots; published with release semantics
    private static int _overflowed;     // ops dropped because the stack was full (should stay 0)
    private static bool _installed;

    /// <summary>Explicit label for the work the UI thread is doing right now (see
    /// <see cref="Scope"/>). Wins over the delegate name, which for a lambda reads as
    /// <c>&lt;Tick&gt;b__42_0</c>. Same shape as <see cref="VideoDiag.UiScope"/>: two writes,
    /// no allocation.</summary>
    private static string? _label;

    /// <summary>Subscribe to the dispatcher hooks; called once from App startup. Idempotent and
    /// never throws: this runs before MainWindow exists, and a diagnostic that can break startup
    /// is worse than no diagnostic.</summary>
    public static void Install(Dispatcher dispatcher)
    {
        try
        {
            if (_installed || dispatcher == null) return;
            _installed = true;
            var hooks = dispatcher.Hooks;
            hooks.OperationStarted += OnStarted;
            hooks.OperationCompleted += OnFinished;
            hooks.OperationAborted += OnFinished;
        }
        catch (Exception ex)
        {
            // Leaves Describe() reporting "(untracked)" — the rest of the hang report is unaffected.
            try { App.Logger?.Warning("[WATCHDOG] dispatcher op tracking unavailable: {E}", ex.Message); } catch { }
        }
    }

    private static void OnStarted(object? sender, DispatcherHookEventArgs e)
    {
        try
        {
            int d = _depth;
            if ((uint)d >= MaxDepth) { _overflowed++; return; }
            _stack[d].Op = e.Operation;
            _stack[d].StartedTick = Environment.TickCount64;
            // Release write: the slot above must be visible to the watchdog thread before the
            // depth that makes it readable is.
            Volatile.Write(ref _depth, d + 1);
        }
        catch { }
    }

    private static void OnFinished(object? sender, DispatcherHookEventArgs e)
    {
        try
        {
            var op = e.Operation;
            int d = _depth;
            if ((uint)d > MaxDepth) return;
            for (int i = d - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_stack[i].Op, op)) continue;
                // Pop this entry AND anything above it. An operation that completes while entries
                // sit on top of it means we missed their completion (an aborted nested pump); the
                // alternative is a stack that only ever grows and then reports a stale name.
                for (int j = d - 1; j >= i; j--) _stack[j].Op = null;
                Volatile.Write(ref _depth, i);
                return;
            }
        }
        catch { }
    }

    /// <summary><c>using var _ = UiOpTracker.Scope("LockCard.Tick");</c> — a short human name
    /// instead of the compiler's lambda spelling. Restores the previous label on dispose.</summary>
    public static OpScope Scope(string label) => new(label);

    public readonly struct OpScope : IDisposable
    {
        private readonly string? _previous;
        internal OpScope(string label) { _previous = _label; _label = label; }
        public void Dispose() => _label = _previous;
    }

    /// <summary>
    /// One line for the hang report: which operation the UI thread is inside, how long it has been
    /// there and at what priority. Runs on the WATCHDOG thread while the UI thread is presumed
    /// dead, so it takes no lock and touches no DispatcherObject — only already-published fields
    /// and (lazily, once) reflection over the operation's delegate. Never throws.
    /// </summary>
    public static string Describe()
    {
        try
        {
            string? label = _label;
            int d = Volatile.Read(ref _depth);
            if ((uint)d > MaxDepth) d = 0;
            if (d <= 0)
                return label ?? (_installed ? "(none running)" : "(untracked)");

            var slot = _stack[d - 1];
            var op = slot.Op;
            long ageSec = Math.Max(0, Environment.TickCount64 - slot.StartedTick) / 1000;

            string name = label ?? NameOf(op);
            string priority = "?";
            try { if (op != null) priority = op.Priority.ToString(); } catch { }

            var text = string.Concat(
                name, " (", ageSec.ToString(CultureInfo.InvariantCulture), "s, ", priority, ")");
            if (d > 1) text += " nested=" + d.ToString(CultureInfo.InvariantCulture);
            if (_overflowed > 0) text += " overflowed=" + _overflowed.ToString(CultureInfo.InvariantCulture);
            return text;
        }
        catch { return "(unavailable)"; }
    }

    // ── Delegate name resolution (watchdog thread only, never on the hot path) ───────────────

    private static FieldInfo? _methodField;
    private static bool _methodFieldTried;

    /// <summary><see cref="DispatcherOperation"/> does not expose the delegate it runs, so the name
    /// comes from its internal <c>_method</c> field. Resolved once and cached; if a future WPF
    /// renames it the report degrades to "(unnamed op)" rather than failing.</summary>
    private static string NameOf(DispatcherOperation? op)
    {
        if (op == null) return "(unnamed op)";
        try
        {
            if (!_methodFieldTried)
            {
                _methodFieldTried = true;
                _methodField = typeof(DispatcherOperation)
                    .GetField("_method", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (_methodField?.GetValue(op) is not Delegate del) return "(unnamed op)";
            var method = del.Method;
            return ShortName(method.DeclaringType, method.Name);
        }
        catch { return "(unnamed op)"; }
    }

    /// <summary>
    /// Turn a compiler-mangled lambda into something greppable:
    /// <c>LockCardWindow+&lt;&gt;c__DisplayClass7_0.&lt;Tick&gt;b__0</c> becomes <c>LockCardWindow.Tick</c>.
    /// Internal so the shape the report prints can be pinned by a test.
    /// </summary>
    internal static string ShortName(Type? declaringType, string? methodName)
    {
        string method = UnmangleMethod(methodName);

        var type = declaringType;
        // Closure/state-machine types are named "<>c", "<>c__DisplayClass7_0", "<Foo>d__12" — all
        // nested inside the type the reader actually cares about. Walk out to the first real one.
        while (type != null && type.Name.StartsWith("<", StringComparison.Ordinal))
            type = type.DeclaringType;

        string typeName = type?.Name ?? declaringType?.Name ?? "?";
        return string.IsNullOrEmpty(method) ? typeName : typeName + "." + method;
    }

    private static string UnmangleMethod(string? methodName)
    {
        if (string.IsNullOrEmpty(methodName)) return string.Empty;
        // "<Tick>b__0" / "<MoveNext>d__3" -> "Tick" / "MoveNext".
        if (methodName![0] == '<')
        {
            int close = methodName.IndexOf('>');
            if (close > 1) return methodName.Substring(1, close - 1);
            return methodName;
        }
        return methodName;
    }

    /// <summary>Test seam: drop any tracked state so one test cannot leak into the next.</summary>
    internal static void ResetForTests()
    {
        _label = null;
        _overflowed = 0;
        for (int i = 0; i < MaxDepth; i++) _stack[i].Op = null;
        Volatile.Write(ref _depth, 0);
    }
}
