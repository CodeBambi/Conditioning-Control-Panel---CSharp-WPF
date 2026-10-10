using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Tours
{
    /// <summary>
    /// THE NARRATION SEAM (WPF 7.1.5 Services/EmiDesk/EmiTourNarrator.cs). Turns a running tutorial into
    /// moments on EMI Desk's bus, and does nothing else.
    ///
    /// <para><b>Narration is additive and never load-bearing.</b> EMI missing, disabled, muted or
    /// throwing means the tour runs exactly as it does without her: she never blocks a step, never
    /// holds one open, never delays one, and never raises an ask during a tour.</para>
    ///
    /// <list type="bullet">
    /// <item>tour starts: summon her if she is enabled and away, then <c>tourStarted</c></item>
    /// <item>each step: <c>tour.&lt;stepId&gt;</c>, or <c>tourStep</c> when that pool does not exist</item>
    /// <item>last card: <c>tourFinished</c>; abandoned: <c>tourSkipped</c></item>
    /// </list>
    ///
    /// <para>The done latch (EmiState.ToursDone) is the tutorial service's own completion store, and the
    /// book offer one beat behind the ending line is EmiDeskService.Beats (CoreTutorial.Finished): neither
    /// is repeated here. Lives in the head because the tutorial service does.</para>
    /// </summary>
    internal sealed class EmiTourNarrator : IDisposable
    {
        /// <summary>Prefix for the per-step pools. The step id is appended verbatim.</summary>
        public const string StepMomentPrefix = "tour.";
        /// <summary>The per-step fallback, used whenever <c>tour.&lt;stepId&gt;</c> is not a moment.</summary>
        public const string StepFallbackMoment = "tourStep";

        /// <summary>Test seams: is she out, and bring her out. A tour in a headless test never opens her window.</summary>
        internal static Func<bool> DeskOut { get; set; } = () => EmiDeskService.Instance.IsOut;
        internal static Func<Task> SummonDesk { get; set; } = () => EmiDeskService.Instance.Summon("tour");

        private readonly TutorialService _service;
        private bool _disposed;
        private bool _narrating;

        /// <summary>The narrator currently attached, if any. One at a time: two would double every line.</summary>
        public static EmiTourNarrator? Active { get; private set; }

        /// <summary>Attach to a tutorial service. Idempotent for the same service. Safe with null.</summary>
        public static void Attach(TutorialService? service)
        {
            if (service == null) return;
            try
            {
                if (Active != null && ReferenceEquals(Active._service, service) && !Active._disposed) return;
                Detach();
                Active = new EmiTourNarrator(service);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour narrator could not attach"); }
        }

        /// <summary>Detach whatever is attached. Idempotent; safe when nothing is.</summary>
        public static void Detach()
        {
            try { Active?.Dispose(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour narrator could not detach"); }
            finally { Active = null; }
        }

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

        private void OnStarted(object? sender, EventArgs e)
        {
            if (_disposed) return;
            _narrating = true;
            try
            {
                // Summon FIRST, then speak: a moment fired while she is away is dropped (only holds
                // survive the away path). The summon is a task on this head, so the line rides behind
                // it; the tour itself never waits for either.
                if (!DeskOut() && CoreSettings.Current?.EmiDeskEnabled == true)
                {
                    Task summon;
                    try { summon = SummonDesk(); }
                    catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour summon failed"); summon = Task.CompletedTask; }
                    if (!summon.IsCompleted)
                    {
                        summon.ContinueWith(_ => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (_narrating && !_disposed) EmiDeskBus.Fire("tourStarted");
                        }), TaskScheduler.Default);
                        return;
                    }
                }
                EmiDeskBus.Fire("tourStarted");
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tourStarted narration failed"); }   // a narrator never takes the tour with it
        }

        private void OnStepChanged(object? sender, TutorialStep step)
        {
            if (_disposed || !_narrating) return;
            try
            {
                var id = step?.Id;
                if (string.IsNullOrWhiteSpace(id)) return;
                var moment = StepMomentPrefix + id;
                // No pool of its own: the tour rides the generic step moment.
                EmiDeskBus.Fire(HasMoment(moment) ? moment : StepFallbackMoment, null);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] step narration failed for {Step}", step?.Id); }
        }

        private void OnFinished(object? sender, TutorialFinishedEventArgs e)
        {
            if (_disposed || !_narrating) return;
            _narrating = false;
            try
            {
                if (e.Completed) EmiDeskBus.Fire("tourFinished");
                else EmiDeskBus.Fire("tourSkipped");
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] tour ending narration failed"); }
        }

        /// <summary>Does the shipped lines file carry this moment? An engine that is not loaded answers no,
        /// and the fallback covers it.</summary>
        internal static Func<string, bool> HasMoment { get; set; } = momentId =>
        {
            try { return EmiLineEngine.Instance.MomentIds.Contains(momentId, StringComparer.Ordinal); }
            catch { return false; }
        };
    }
}
