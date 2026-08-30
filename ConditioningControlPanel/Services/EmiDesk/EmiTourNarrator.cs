using System;
using System.Linq;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// THE NARRATION SEAM (Ask EMI, wave 1). Turns a running tutorial into moments on EMI Desk's bus,
/// and does nothing else.
///
/// <para>Everything about this class is arranged around one law:
/// <b>narration is additive and never load-bearing.</b> EMI missing, disabled, muted, unloaded or
/// throwing means the tour runs exactly as it does today, at exactly the same pace, showing exactly
/// the same cards. She never blocks a step, never holds one open, never delays one, and never
/// raises an ask during a tour - <c>EmiOffers</c>'s feasibility check refuses one while an overlay
/// is up, and this class adds no second route to one.</para>
///
/// <para>The mapping is the contract's (docs/emi-desk/WAVE1-CONTRACT.md):</para>
/// <list type="bullet">
/// <item>tour starts -> summon her if she is available and away, then <c>tourStarted</c></item>
/// <item>each step -> <c>tour.&lt;stepId&gt;</c>, or <c>tourStep</c> when that pool does not exist</item>
/// <item>last card -> <c>tourFinished</c></item>
/// <item>abandoned -> <c>tourSkipped</c></item>
/// </list>
///
/// <para>Everything goes through <c>App.EmiDesk?.Fire(...)</c>. Nothing here reaches into her
/// window, her line engine or her state: <c>Fire</c> is the one funnel that already knows how to be
/// silent (she is away, she is muted, the pool is empty, the odds missed), and a second route into
/// her would be a second thing to keep silent.</para>
/// </summary>
public sealed class EmiTourNarrator : IDisposable
{
    // ---- moment ids -------------------------------------------------------------
    // Literals, deliberately: Tests/EmiMomentIdWiringTests reads Fire("...") call sites out of the
    // source and fails the build for an id the lines file does not carry. A const would compile,
    // run, cost nothing and say nothing, forever.

    /// <summary>Prefix for the per-step pools. The step id is appended verbatim.</summary>
    public const string StepMomentPrefix = "tour.";

    /// <summary>The per-step fallback, used whenever <c>tour.&lt;stepId&gt;</c> is not a moment.</summary>
    public const string StepFallbackMoment = "tourStep";

    private readonly TutorialService _service;
    private bool _disposed;

    // The tour that is currently running, so a step arriving after an ending (or from a tutorial
    // some other window started) can be told apart from one belonging to this narration.
    private bool _narrating;

    /// <summary>
    /// The narrator currently attached, if any. One at a time: two would double every line.
    /// </summary>
    public static EmiTourNarrator? Active { get; private set; }

    /// <summary>
    /// Attach the narrator to a tutorial service. Idempotent for the same service, so a caller may
    /// arm it on every <c>StartTutorial</c> without bookkeeping. Safe to call with null.
    /// </summary>
    public static void Attach(TutorialService? service)
    {
        if (service == null) return;
        try
        {
            if (Active != null && ReferenceEquals(Active._service, service) && !Active._disposed) return;
            Detach();
            Active = new EmiTourNarrator(service);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] tour narrator could not attach");
        }
    }

    /// <summary>Detach whatever is attached. Idempotent; safe when nothing is.</summary>
    public static void Detach()
    {
        try
        {
            Active?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] tour narrator could not detach");
        }
        finally
        {
            Active = null;
        }
    }

    /// <summary>
    /// Subscribes on construction. Public for the app's own wiring and for tests that want to drive
    /// a service directly; prefer <see cref="Attach"/>, which keeps the single-instance rule.
    /// </summary>
    public EmiTourNarrator(TutorialService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _service.TutorialStarted += OnStarted;
        _service.StepChanged += OnStepChanged;
        _service.TutorialFinished += OnFinished;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _narrating = false;
        try { _service.TutorialStarted -= OnStarted; } catch { }
        try { _service.StepChanged -= OnStepChanged; } catch { }
        try { _service.TutorialFinished -= OnFinished; } catch { }
    }

    // =================================================================================
    //  the three hooks
    // =================================================================================

    private void OnStarted(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _narrating = true;
        try
        {
            var desk = App.EmiDesk;
            if (desk == null) return;

            // Summon FIRST, then speak. Fire() on a moment nobody is out for is dropped after the
            // bus notification (only holds survive the away path), so the order matters - and
            // Summon is synchronous on the dispatcher, which is where a tour always runs.
            //
            // Both guards below are also inside Summon; they are repeated here so the ordinary
            // "she is already out" case costs nothing and reads plainly at the call site.
            if (!desk.IsOut && App.Settings?.Current?.EmiDeskEnabled == true)
            {
                desk.Summon("tour");
            }

            desk.Fire("tourStarted");
        }
        catch (Exception ex)
        {
            // A narrator that throws would take the tour with it. It does not get to.
            Log.Debug(ex, "[EmiDesk] tourStarted narration failed");
        }
    }

    private void OnStepChanged(object? sender, TutorialStep step)
    {
        if (_disposed || !_narrating) return;
        try
        {
            var desk = App.EmiDesk;
            if (desk == null) return;

            var id = step?.Id;
            if (string.IsNullOrWhiteSpace(id)) return;

            var moment = StepMomentPrefix + id;
            if (HasMoment(moment))
            {
                desk.Fire(moment, null);
                return;
            }

            // No pool of its own: every other tour in the app (nine of them, ~90 steps) rides the
            // generic step moment rather than needing ninety pools written for it.
            desk.Fire(StepFallbackMoment, null);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] step narration failed for {Step}", step?.Id);
        }
    }

    private void OnFinished(object? sender, TutorialFinishedEventArgs e)
    {
        if (_disposed) return;
        if (!_narrating) return;
        _narrating = false;
        try
        {
            var desk = App.EmiDesk;
            if (desk == null) return;

            if (e.Completed) desk.Fire("tourFinished");
            else desk.Fire("tourSkipped");

            // THE BOOK, one beat behind her ending line and never on top of it. Offered on both
            // endings on purpose: a walker has just been shown the app and is the likeliest reader
            // there will ever be, and a skipper has just said they would rather not be walked,
            // which is what a manual is for. The moment carries limit ever/1, so this is once in
            // the life of an account however many tours are run; EmiCodex holds the rest of the
            // brakes (no book in the build, already open, already read).
            EmiCodex.MaybeOfferSoon(e.Completed ? "tourFinished" : "tourSkipped");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] tour ending narration failed");
        }
    }

    // =================================================================================
    //  helpers
    // =================================================================================

    /// <summary>
    /// Does the shipped lines file carry this moment? Asking is what makes a missing per-step pool
    /// fall back to <c>tourStep</c> instead of a silent card - <c>Fire</c> on an unknown id is
    /// dropped without a word, which is right for a typo and wrong for a step nobody wrote lines
    /// for yet. An engine that is not loaded answers NO, and the fallback covers it.
    /// </summary>
    private static bool HasMoment(string momentId)
    {
        try
        {
            var ids = EmiLineEngine.Instance.MomentIds;
            return ids.Contains(momentId, StringComparer.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
