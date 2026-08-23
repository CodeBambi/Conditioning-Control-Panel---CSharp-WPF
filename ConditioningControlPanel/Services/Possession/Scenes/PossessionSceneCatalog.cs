using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Services.Possession.Scenes;

/// <summary>Every scene the director can run, one instance each (like the effect catalog).</summary>
public static class PossessionSceneCatalog
{
    public static List<IPossessionScene> CreateAll() => new()
    {
        new RailSweepScene(),
        new WhereYouAreScene(),
        new TheCountScene(),
    };
}

/// <summary>
/// A scene dressed as an <see cref="IPossessionEffect"/>.
///
/// <para>The director already owns everything a running scene needs: a cancellation token per haunt,
/// a live-ghost ledger, per-target cooldowns, the reassembly exit and a crash-safe synchronous UndoAll.
/// Teaching it a second lifecycle for scenes would have meant duplicating all of that (and forgetting
/// one of them). So a scene is handed to the same machinery through this adapter: Apply runs the
/// choreography, Undo unwinds it, and UndoAll cancels it mid-sentence exactly as it cancels an effect.</para>
///
/// <para>It declares no Roles - the scene picks its own victims through the director's booking
/// callback, because a scene needs several controls of several roles and the deck's one-effect-one-
/// victim pick cannot express that.</para>
/// </summary>
public sealed class PossessionSceneEffect : IPossessionEffect
{
    private readonly IPossessionScene _scene;
    private readonly Func<PossessionRole, PossessionTarget?> _pick;
    private bool _live;

    public PossessionSceneEffect(IPossessionScene scene, Func<PossessionRole, PossessionTarget?> pick)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _pick = pick ?? (_ => null);
    }

    public IPossessionScene Scene => _scene;

    public string Id => _scene.Id;
    public PossessionRung MinRung => _scene.MinRung;
    public PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    /// <summary>Always named: a scene is the loudest thing on the ladder short of a Doki dialog, and an
    /// unattributed choreography is exactly the "was that a bug?" the whole grammar exists to prevent.</summary>
    public bool IsBig => true;
    public bool UsesFlicker => false;
    public double Weight => 0;                 // never dealt from the deck; the director elects it
    /// <summary>The scene cleans up after itself; this is the short tail before the director takes the
    /// slot back, so the last beat is readable before the room reassembles.</summary>
    public TimeSpan HoldFor => TimeSpan.FromSeconds(1.5);
    public IReadOnlyList<PossessionRole> Roles => Array.Empty<PossessionRole>();
    public bool IsLive => _live;

    public bool CanApply(PossessionContext ctx, PossessionTarget? target)
        => !_live && ctx != null && ctx.Rung >= _scene.MinRung;

    public async Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (_live) return;
        _live = true;
        try { await _scene.RunAsync(ctx, ctx.Host, _pick, ct).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Logger?.Warning("Possession scene {Id} threw: {Error}", Id, ex.Message); }
    }

    public async Task UndoAsync(TimeSpan duration)
    {
        try { await _scene.UndoAsync(duration).ConfigureAwait(true); }
        catch (Exception ex) { App.Logger?.Warning("Possession scene {Id} undo failed: {Error}", Id, ex.Message); }
        finally { _live = false; }
    }
}
