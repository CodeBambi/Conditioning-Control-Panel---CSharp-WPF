using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>Every default-GUID audio stream a Windows process opens shares ONE render session, so
/// "does this process hold an ACTIVE render session" is a PROCESS-wide question that any second
/// real device in this process answers for you.</b>
///
/// <para>Two facts in <see cref="AudioCapabilityTests"/> are NEGATIVE CONTROLS over exactly that
/// question — no active session before a device is opened, none after teardown — and they were
/// intermittently red on full runs whose diffs could not reach an audio endpoint (task-board, the
/// P1 row this collection closes). This is the mechanism, measured rather than supposed, and the
/// three measurements below are what decided the shape of the fix.</para>
///
/// <list type="number">
/// <item><b>The question is ALREADY per-process, and a second PROCESS cannot poison it.</b>
/// <see cref="WasapiRenderProbe"/> enumerates the endpoint's sessions and skips every one whose
/// <c>IAudioSessionControl2::GetProcessId</c> is not this pid (<c>WasapiRenderProbe.cs:159,169-172</c>).
/// Measured: a second <c>CcpClient.Tests.exe</c> held a real ACTIVE render session (state 1, peak
/// 0.405) for 90 s while this class ran 8 times in another process — 8/8 green. So the row's
/// cross-process hypothesis is refused by the source AND by the machine, and this collection
/// deliberately carries NO lease, unlike <see cref="RealDesktopCollection"/> whose claim really is
/// machine-wide.</item>
///
/// <item><b>It cannot be narrowed BELOW the process, so attribution is not available as a fix.</b>
/// Measured with a second, independent enumeration: two <c>WasapiAudioPresence</c> devices open at
/// once in this process appear as ONE session with ONE
/// <c>IAudioSessionControl2::GetSessionInstanceIdentifier</c>, and its state stays
/// <c>AudioSessionStateActive</c> until BOTH are disposed. There is no per-stream identity to ask
/// about; miniaudio opens with the default session GUID and Windows folds the streams together.</item>
///
/// <item><b>So the only fix is in-process EXCLUSION, and the census of who to exclude is small.</b>
/// A sampler thread polled this question for a whole run of the assembly MINUS
/// <see cref="AudioCapabilityTests"/>: across 3176 tests the process held an active render session
/// in exactly two windows, and both are bracketed by
/// <c>ScriptedSessionSurfaceTests.TheComposedParticipantOwnsOneScriptedRun_OverItsOwnEngineAndItsOwnDocuments</c>
/// and <c>ScriptedSessionSurfaceTests.ClosingTheAppMidSession_PERSISTS_TheUsersDialsAndNotTheSessions</c>
/// — its two facts that really start a scripted session, which really brings the endpoint up.
/// Running that class alone reproduces exactly those two windows and nothing else does.</item>
/// </list>
///
/// <para><b>The mechanism is co-location and nothing else.</b> xunit runs the classes of one
/// collection sequentially, which is what serialises them; <c>DisableParallelization</c> is carried
/// for consistency with <see cref="RealDesktopCollection"/> and <c>ProcessEnvCollection</c>
/// (<c>DataRootOverrideTests.cs:116-121</c>) as the same NON-RELIED-UPON hint — measured again here,
/// it does not serialise cross-collection traffic on this runner.</para>
///
/// <para><b>What it is not.</b> Not a retry, not a wait, not a skip, and no assertion was weakened:
/// the negative controls still assert the absence of an ACTIVE render session, which is the claim
/// worth making. Measured on the pair that collides — <see cref="AudioCapabilityTests"/> plus
/// <c>ScriptedSessionSurfaceTests</c>, run together: <b>9 of 20 runs red before this collection
/// existed</b> (always <c>AfterTeardown_TheOsNoLongerReportsAnActiveRenderSession</c>, always
/// <c>osActiveAfterDispose=True</c>, the board's own line), <b>0 of 20 after</b>.</para>
///
/// <para><b>The residue, stated rather than hidden.</b> Membership here is a CENSUS, not a
/// mechanical guard: nothing textual can see that starting a real session engine reaches a render
/// device three layers down, so a future class that opens one and does not join re-opens the hazard.
/// That is why both controls now fail with the mechanism and this collection's name in their
/// message instead of a bare boolean — the next such class names itself in the first failure.</para>
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class RealRenderDeviceCollection;
