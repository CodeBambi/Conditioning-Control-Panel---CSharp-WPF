using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services.Speech
{
    /// <summary>What a head supplies to <see cref="SpokenMantra.RunAsync"/>.</summary>
    public sealed class SpokenMantraHost
    {
        /// <summary>One listen for the target phrase (SpeechEngine.RecognizePhraseAsync).</summary>
        public Func<string, RecognizeOptions, Task<PhraseResult>> Recognize = (_, _) => Task.FromResult(PhraseResult.NotAvailable);
        /// <summary>The tube says a line: text, then the voiced clip or null (GigglePriority, not AI).</summary>
        public Action<string, string?> Say = (_, _) => { };
        public Func<bool> IsSpeaking = () => false;
        /// <summary>Length of a resolved clip, null = unknown (the 1.4 s settle is used).</summary>
        public Func<string?, TimeSpan?> AudioDuration = _ => null;
        /// <summary>Credit a mic-verified mantra (MantraService TryCompleteMantra, else CreditExternalMantra).</summary>
        public Action Credit = () => { };
        public Action<string> PromptStarted = _ => { };
        public Action<PhraseResult> PromptFinished = _ => { };
        /// <summary>The tube's "listening" dots while the mic is open (WPF ShowListeningBubble, ccp-bugs #841).</summary>
        public Action<string> ShowListening = _ => { };
        public Action HideListening = () => { };
        public Func<TimeSpan, CancellationToken, Task> Delay = Task.Delay;
        /// <summary>Completes when the clip of the last <see cref="Say"/> has finished playing. Awaited
        /// (capped) before every listen so the recognizer never hears her. Null (WPF) = timings only.</summary>
        public Func<CancellationToken, Task>? WaitSpoken;
    }

    /// <summary>
    /// Spoken Mantra (extracted from WPF AutonomyService.RunSpokenMantraAsync, shared by both heads):
    /// she voices an in-theme line that asks you to repeat a phrase, opens the mic once she's finished
    /// speaking, and answers with the entry's bespoke response. Per-mod content comes from
    /// <see cref="MantraVoiceService"/>. Cancelling <paramref name="ct"/> (panic, stop listening)
    /// ends it before the next listen.
    /// </summary>
    public static class SpokenMantra
    {
        public static async Task RunAsync(MantraVoiceService voice, SpokenMantraHost host, CancellationToken ct = default)
        {
            try
            {
                var mantra = voice.NextMantra();
                if (mantra == null)
                {
                    Log.Information("AutonomyService: SpokenMantra — no mantra to ask");
                    return;
                }

                var phrase = mantra.Phrase;
                try { host.PromptStarted(phrase); } catch { }

                // She delivers the whole prompt (voiced if the clip ships, else text-only).
                var promptAudio = voice.ResolveAudio(mantra.PromptAudio);
                host.Say(mantra.PromptText, promptAudio);

                // CRITICAL: open the mic only AFTER she finishes saying the phrase — otherwise the
                // recognizer hears her own delivery and self-matches. Wait for the clip's measured
                // duration (+ a beat), then spin briefly until the avatar reports it's done speaking.
                var dur = host.AudioDuration(promptAudio);
                var settleMs = dur.HasValue ? (int)dur.Value.TotalMilliseconds + 600 : 1400;
                await host.Delay(TimeSpan.FromMilliseconds(settleMs), ct).ConfigureAwait(false);
                for (int i = 0; i < 40 && host.IsSpeaking(); i++)
                    await host.Delay(TimeSpan.FromMilliseconds(75), ct).ConfigureAwait(false);

                // A beat longer than the old 8s so it's easier to get the whole phrase out in time.
                var listenWindow = TimeSpan.FromSeconds(10);

                await WaitSpokenAsync().ConfigureAwait(false);
                var result = await ListenAsync(phrase, listenWindow).ConfigureAwait(false);

                // One gentle retry on ANY non-match — too quiet, misheard, or nothing said — as long as
                // the engine is still available. The prompt fits the reason.
                if (!result.Matched && !result.Unavailable && !ct.IsCancellationRequested)
                {
                    var retryLine = result.LoudEnough == false
                        ? "Louder for me~ say it like you mean it."
                        : result.TimedOut && string.IsNullOrWhiteSpace(result.Transcript)
                            ? "Take your time~ say it again for me."
                            : "Mmm, almost~ say it once more, just for me.";
                    SayLine(voice.GetRetry(), retryLine);
                    await host.Delay(TimeSpan.FromMilliseconds(900), ct).ConfigureAwait(false);
                    for (int i = 0; i < 40 && host.IsSpeaking(); i++)
                        await host.Delay(TimeSpan.FromMilliseconds(75), ct).ConfigureAwait(false);
                    await WaitSpokenAsync().ConfigureAwait(false);
                    result = await ListenAsync(phrase, listenWindow).ConfigureAwait(false);
                }

                if (result.Unavailable || ct.IsCancellationRequested)
                {
                    Log.Information("AutonomyService: SpokenMantra — speech went unavailable mid-action");
                    return;
                }

                try { host.PromptFinished(result); } catch { }

                if (result.Matched)
                {
                    // Bespoke voiced success response; no custom response = neutral praise with the
                    // pet name from the active mod via {petname}.
                    var respAudio = voice.ResolveAudio(mantra.ResponseAudio);
                    host.Say(string.IsNullOrWhiteSpace(mantra.Response)
                        ? Localization.VocabTokens.Apply("Perfect, {petname}~")
                        : mantra.Response, respAudio);
                    host.Credit();
                    Log.Information("AutonomyService: SpokenMantra matched ({Chars} chars, score={Score:0.00}, conf={Conf:0.00})",
                        (phrase ?? "").Length, result.Score, result.Confidence);
                }
                else if (result.TimedOut && string.IsNullOrWhiteSpace(result.Transcript))
                {
                    SayLine(voice.GetTimeout(), "Too shy? I'll ask again later~");
                    Log.Information("AutonomyService: SpokenMantra timed out, no speech ({Chars} chars)", (phrase ?? "").Length);
                }
                else
                {
                    SayLine(voice.GetRetry(), "Mmm, not quite. Next time say it just for me~");
                    // Never the transcript: that is a recording of the user's own voice in text form.
                    Log.Information("AutonomyService: SpokenMantra miss ({Chars} chars, heard {HeardChars} chars, score={Score:0.00})",
                        (phrase ?? "").Length, (result.Transcript ?? "").Length, result.Score);
                }
            }
            catch (OperationCanceledException) { Log.Information("AutonomyService: SpokenMantra cancelled"); }
            catch (Exception ex)
            {
                Log.Warning("AutonomyService: SpokenMantra failed: {Error}", ex.Message);
            }

            // Show the "listening" cue while the mic is open (ccp-bugs #841): without it the player
            // cannot tell when to start saying the phrase. Hide no-ops once a real bubble took over.
            async Task<PhraseResult> ListenAsync(string phrase, TimeSpan window)
            {
                try { host.ShowListening(phrase); } catch { }
                try { return await host.Recognize(phrase, new RecognizeOptions { Timeout = window }).ConfigureAwait(false); }
                finally { try { host.HideListening(); } catch { } }
            }

            // Hold the mic shut until her clip ended; 30 s caps a lost finished callback.
            async Task WaitSpokenAsync()
            {
                if (host.WaitSpoken is { } wait)
                {
                    var spoken = wait(ct);
                    if (await Task.WhenAny(spoken, host.Delay(TimeSpan.FromSeconds(30), ct)).ConfigureAwait(false) != spoken
                        && !ct.IsCancellationRequested)
                        Log.Warning("SpokenMantra: her clip's finished signal never came; listening after the 30 s cap");
                }
                ct.ThrowIfCancellationRequested();
            }

            // A shared retry/timeout line (voiced if it ships audio), else plain text.
            void SayLine(MantraLine? line, string fallback)
            {
                if (line != null && !string.IsNullOrWhiteSpace(line.Text))
                    host.Say(line.Text, voice.ResolveAudio(line.Audio));
                else
                    host.Say(fallback, null);
            }
        }
    }
}
