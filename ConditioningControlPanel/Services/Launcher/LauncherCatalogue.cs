using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// One tile on the launcher: a game the CC Labs client can start without going through the panel.
///
/// Same shape as <c>EmiTarget</c>: a bag of delegates, so the launcher never learns what a game IS,
/// only whether it exists in this build (<see cref="IsAvailable"/>), whether the tier gate would
/// refuse it (<see cref="IsLocked"/>), how to start it (<see cref="Launch"/>) and whether its window
/// is up right now (<see cref="IsActive"/>). Every host service already exposes exactly those four
/// facts, so a tile is four lambdas and nothing in the window changes when a game is added.
/// </summary>
/// <param name="Id">Stable id. It is the <c>--game &lt;id&gt;</c> argument and the shortcut
/// argument, so it must never be renamed once shipped.</param>
/// <param name="TitleKey">Loc key of the visible name.</param>
/// <param name="BlurbKey">Loc key of the one-line description under the name.</param>
/// <param name="ArtPath">Resource-relative art (<c>features/dtrh.png</c>), resolved through
/// <c>ModResourceResolver</c> so a .ccpmod can reskin it. Null paints the hue plate with a glyph.</param>
/// <param name="Glyph">One character drawn on the hue plate when there is no art, or over it.</param>
/// <param name="Hue">Tile colour behind the art and the tint of the tile's glow.</param>
/// <param name="IsAvailable">False hides the tile. Build flags and "not ready for release".</param>
/// <param name="IsLocked">True paints the padlock; the click still goes to <see cref="Launch"/>,
/// which owns the refusal toast.</param>
/// <param name="Launch">Starts the game. Idempotent on every host: a live window is re-focused.</param>
/// <param name="IsActive">True while the game's window exists. The launcher polls it to know
/// when to come back.</param>
/// <param name="IsRevealed">Null means the tile always shows its face. False draws the mystery
/// card in its place: no title, no art, a "?" and a Play button that goes to the Back Room, where
/// the reveal is bought. The entry stays Available so <c>--game</c> and a shortcut still reach
/// <see cref="Launch"/>, which owns its own refusal.</param>
public sealed record LauncherEntry(
    string Id,
    string TitleKey,
    string BlurbKey,
    string? ArtPath,
    string Glyph,
    Color Hue,
    Func<bool> IsAvailable,
    Func<bool> IsLocked,
    Action Launch,
    Func<bool> IsActive,
    Func<bool>? IsRevealed = null)
{
    public string Title
    {
        get { try { return Loc.Get(TitleKey); } catch { return Id; } }
    }

    public string Blurb
    {
        get { try { return Loc.Get(BlurbKey); } catch { return ""; } }
    }

    /// <summary><see cref="IsAvailable"/> wrapped: a probe that throws hides the tile.</summary>
    public bool Available
    {
        get
        {
            try { return IsAvailable(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] availability probe threw for {Id}", Id); return false; }
        }
    }

    /// <summary><see cref="IsLocked"/> wrapped: a probe that throws reads as locked, never as free.</summary>
    public bool Locked
    {
        get
        {
            try { return IsLocked(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] lock probe threw for {Id}", Id); return true; }
        }
    }

    /// <summary>True while nobody is signed in. Every game needs an account (Sep 18 2026 owner
    /// decision), so this is one fact for the whole catalogue, read through
    /// <see cref="LauncherCatalogue.NeedsAccount"/>.</summary>
    public bool NeedsAccount => LauncherCatalogue.NeedsAccount;

    /// <summary><see cref="IsActive"/> wrapped: a probe that throws reads as closed.</summary>
    public bool Active
    {
        get
        {
            try { return IsActive(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] active probe threw for {Id}", Id); return false; }
        }
    }

    /// <summary><see cref="IsRevealed"/> wrapped: null is revealed, a probe that throws is not.
    /// The mystery card is the safe face; the real tile would launch straight into a refusal.</summary>
    public bool Revealed
    {
        get
        {
            if (IsRevealed == null) return true;
            try { return IsRevealed(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] reveal probe threw for {Id}", Id); return false; }
        }
    }
}

/// <summary>
/// The games the launcher can start. Pure: <see cref="Games"/> only captures lambdas, it never runs
/// them, so tests can walk the list without opening a window or evaluating a tier gate.
///
/// What is NOT here, on purpose: Remote, Companion and sessions. Those are panel features
/// (Sep 18 2026 owner decision: the split is games vs CCP, not 2D vs 3D). The Graded Intake is
/// the one panel tab with a tile, because it is the best first thing a new account can do; its
/// Launch opens the panel on the tab instead of a window. Breakout is a station inside the Back
/// Room with no deep link yet, so it rides the Back Room tile until the room grows a
/// <c>?station=</c> parameter.
/// </summary>
public static class LauncherCatalogue
{
    /// <summary>The id the panel itself answers to in <c>--game</c> and in shortcuts.</summary>
    public const string PanelId = "panel";

    /// <summary>
    /// Piece by Piece is hidden on the Play page for 6.9.5 ("not ready for release"). The launcher
    /// follows the same switch so the two surfaces never disagree about what exists.
    /// </summary>
    public static readonly bool PieceByPieceAvailable = false;

    /// <summary>
    /// Whether an account is signed in. Every game needs one; the panel does not. Settable so a
    /// test can walk the refusal without an App.
    /// </summary>
    public static Func<bool> SignedIn { get; set; } = () => App.IsLoggedIn;

    /// <summary>True when the games must refuse and ask for a sign-in. A probe that throws reads
    /// as signed out, never as signed in.</summary>
    public static bool NeedsAccount
    {
        get
        {
            try { return !SignedIn(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] sign-in probe threw"); return true; }
        }
    }

    private static readonly Lazy<IReadOnlyList<LauncherEntry>> _games = new(Build);

    public static IReadOnlyList<LauncherEntry> Games => _games.Value;

    public static LauncherEntry? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var key = id.Trim();
        return Games.FirstOrDefault(g => string.Equals(g.Id, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when any catalogued game currently has a window up.</summary>
    public static bool AnyActive => Games.Any(g => g.Active);

    /// <summary>
    /// Starts a game by id. False when the id is unknown, the tile is unavailable or nobody is
    /// signed in; a locked tile still routes to <c>Launch</c> because the host owns the refusal
    /// toast and the "See tiers" action.
    /// </summary>
    public static bool TryLaunch(string? id)
    {
        var entry = Find(id);
        if (entry == null) { Log.Warning("[Launcher] unknown game id {Id}", id); return false; }
        return TryLaunch(entry);
    }

    internal static bool TryLaunch(LauncherEntry entry)
    {
        if (!entry.Available) { Log.Information("[Launcher] {Id} is not available in this build", entry.Id); return false; }
        if (NeedsAccount) { Log.Information("[Launcher] {Id} refused: nobody is signed in", entry.Id); return false; }
        try
        {
            entry.Launch();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Launcher] launch of {Id} threw", entry.Id);
            return false;
        }
    }

    private static Color Tile(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static bool Always() => true;
    private static bool Never() => false;

    private static bool LabOk(string titleKey, string? dailyKey)
    {
        try
        {
            var name = Loc.Get(titleKey);
            return dailyKey == null
                ? TierGate.RequiresLab(name).Allowed
                : TierGate.RequiresLab(name, dailyKey).Allowed;
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] lab probe failed"); return false; }
    }

    private static List<LauncherEntry> Build()
    {
        var list = new List<LauncherEntry>();

        void G(string id, string? art, string glyph, Color hue, Func<bool> available, Func<bool> locked,
               Action launch, Func<bool> active, Func<bool>? revealed = null)
        {
            list.Add(new LauncherEntry(id, "launcher_game_" + id + "_title", "launcher_game_" + id + "_blurb",
                art, glyph, hue, available, locked, launch, active, revealed));
        }

        // Order is the order on the launcher: the newest, loudest room first, the quiet ones last.

        // The Back Room: free, no gate, no account needed (the SP relay just goes quiet signed out).
        G("backroom", "features/backroom.png", "♦", Tile(0xB9, 0x5C, 0xD8), Always, Never,
            () => BackRoom.BackRoomHostService.Launch(),
            () => BackRoom.BackRoomHostService.IsActive);

        // Racing Thoughts: a Back Room unlock since 2026-09-18. Without a track the tile is the
        // mystery card pointing at the counter; the entry stays Available so a shortcut still
        // reaches Launch, where CaucusHostService refuses on the same door (RacingAccess).
        G("race", "features/race.png", "☕", Tile(0xFF, 0xB3, 0x6B), Always, Never,
            () => Chaos.CaucusHostService.Launch(),
            () => Chaos.CaucusHostService.IsActive,
            revealed: () => Race.RacingAccess.CanLaunch);

        // Down the Rabbit Hole: Lab tier. The Play handler gates it; Launch does not, so the tile
        // asks the gate itself and lets DemandLab paint the refusal.
        G("dtrh", "features/dtrh.png", "▼", Tile(0x8C, 0xF5, 0xC8), Always,
            () => !LabOk("launcher_game_dtrh_title", "dtrh"),
            () =>
            {
                var gate = TierGate.RequiresLab(Loc.Get("launcher_game_dtrh_title"), "dtrh");
                if (gate.Allowed) Chaos.DtrhHostService.Launch();
                else TierGate.DemandLab(Loc.Get("launcher_game_dtrh_title"), "dtrh");
            },
            () => Chaos.DtrhHostService.IsActive);

        // The Arcademy: Launch owns the build flag, the Lab gate and the audio-only refusal.
        // The art is the Arcademy's own entrance-gates plate, copied into Resources/features/
        // because the web tree is Content (copied to disk) and only a WPF Resource resolves
        // through a pack:// uri - pointed at the web path the tile drew the glyph plate instead.
        G("arcademy", "features/arcademy.png", "★", Tile(0xFF, 0x69, 0xB4),
            () => Arcademy.ArcademyHostService.DoorAvailable,
            () => !LabOk("launcher_game_arcademy_title", null),
            () => Arcademy.ArcademyHostService.Launch(),
            () => Arcademy.ArcademyHostService.IsActive);

        // Goon Game: free to join. The launcher has already tucked the panel away, so the game
        // must not duck it a second time.
        G("goon", "features/goon_game_tile.png", "●", Tile(0x76, 0xC8, 0x93), Always, Never,
            () => GoonGame.GoonHostService.Launch(duckMainWindow: false),
            () => GoonGame.GoonHostService.IsActive);

        // Piece by Piece: Launch owns the Lab gate. Hidden while the Play card is hidden.
        G("piecebypiece", null, "♟", Tile(0x7B, 0x5C, 0xFF),
            () => PieceByPieceAvailable,
            () => !LabOk("launcher_game_piecebypiece_title", null),
            () => PieceByPiece.PieceByPieceHostService.Launch(),
            () => PieceByPiece.PieceByPieceHostService.IsActive);

        // Graded Intake: a panel tab, not a window, so Launch opens the panel on it and IsActive
        // never reports a window (the launcher does not wait for the panel). Locked when the
        // weekly free pass is spent and the account is below tier 2; the click still opens the
        // tab, whose gate explains the pass, so the tile is never a dead end.
        G("intake", "features/lab_quiz_hero.png", "❓", Tile(0x8E, 0x7C, 0xF2), Always,
            () => !(App.IntakePass?.CanStartIntake ?? false),
            () => LauncherHost.OpenPanelTab("gradedintake"),
            Never);

        return list;
    }
}
