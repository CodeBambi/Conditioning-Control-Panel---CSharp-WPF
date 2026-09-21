using System;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// What the launcher plays when the pointer crosses a tile. Pure: the caller passes the clock and
/// the dice, so a sweep across the grid is a phrase somebody played rather than a row of pings.
///
/// The rules are Breakout's (see the audio notes in AGENTS.md), because they are what makes a
/// player-driven sound musical instead of noisy:
///
///  * ONE KEY. Every note is a rung of C pentatonic, rendered one rung per file by
///    <c>scripts/render-launcher-notes.mjs</c>. Pentatonic has no semitone clashes, so no order of
///    tiles can sound wrong.
///  * SHAPES, NOT STEPS. A phrase is built from short motifs (a run, a turn, an arc, a leap that
///    settles) in scale degrees, so the line turns and breathes instead of walking up a ladder.
///  * A PHRASE HAS A REGISTER. Each new phrase draws its own window of the ladder, so two sweeps
///    are never the same tune. Silence ends it.
///
/// The rung never depends on WHICH tile is under the pointer (the owner's call): the melody is the
/// movement, not the target.
///
/// <para>This is the "Pluck" proposal, picked by the owner on 2026-09-21 out of six that were
/// A/B tested in the launcher (music box, wind chime, flourish, duet, pluck, plain ladder): the
/// dry wood voice, a tight register and nothing but the line - no ornaments, no chords, no
/// partner under the note. The others and their voices came out with the switcher.</para>
/// </summary>
public sealed class LauncherMelody
{
    /// <summary>Rungs on the ladder, and the number of note files on disk.</summary>
    public const int Rungs = 18;

    /// <summary>The rung that is C5, the room's key: the cue the launcher shipped with.</summary>
    public const int RootRung = 5;

    /// <summary>Lowest and highest rung the pluck uses. Its ends of the ladder are not its own.</summary>
    public const int Low = 2, High = 15;

    /// <summary>How many rungs one phrase roams over. About an octave.</summary>
    public const int Span = 6;

    /// <summary>Silence this long ends the phrase; the next crossing opens a new one.</summary>
    public const int PhraseGapMs = 1800;

    /// <summary>The closest two crossings may sound. A pointer thrown across the grid is throttled.</summary>
    public const int MinGapMs = 95;

    private static readonly int[] Penta = { 0, 2, 4, 7, 9 };

    /// <summary>Motifs in scale degrees, relative to where the line already is.</summary>
    private static readonly int[][] Steps =
    {
        new[] { 1, 1, 1 },          // a run up
        new[] { -1, -1 },           // and back down
        new[] { 1, -1, 1, 2 },      // a turn
        new[] { 2, 1, -1, -2 },     // an arc over and back
        new[] { -1, 1, 1 },         // a dip and a lift
        new[] { 1, 2 },             // a pair of steps
    };

    private static readonly int[][] Leaps =
    {
        new[] { 3, -1, -1 },        // a leap that settles
        new[] { 4, -2, 1 },         // a wider one
        new[] { -3, 1, 2 },         // a drop that climbs out
        new[] { 2, 3, -2 },         // an open call
        new[] { 5, -3 },            // an octave and a fall
    };

    /// <summary>How often a phrase is a leaping one rather than a stepping one.</summary>
    private const double LeapingPhrase = 0.5;

    /// <summary>One note to sound: a rung, and how loud it is against the hover level.</summary>
    public readonly record struct Cue(int Rung, double Level);

    private readonly object _gate = new();
    private readonly Random _rng;

    private DateTime _last = DateTime.MinValue;
    private bool _inPhrase;
    private int _lo, _hi, _degree;
    private int[] _motif = Array.Empty<int>();
    private int[]? _lastMotif;
    private readonly int[] _recent = new int[4];
    private int _heard;
    private int _at;
    private bool _leaps;

    public LauncherMelody() : this(new Random()) { }

    /// <summary>Seeded for the tests: the same seed is the same performance.</summary>
    public LauncherMelody(Random rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));

    /// <summary>Semitones from C5 for a rung, the contract the rendered files are cut to.</summary>
    public static int Semitones(int rung)
    {
        var i = Math.Clamp(rung, 0, Rungs - 1);
        return 12 * (i / Penta.Length) + Penta[i % Penta.Length] - 12 * (RootRung / Penta.Length);
    }

    /// <summary>The note this crossing sounds. Safe from any thread.</summary>
    public Cue Next(DateTime nowUtc)
    {
        lock (_gate)
        {
            var gap = nowUtc - _last;
            if (!_inPhrase || gap.TotalMilliseconds >= PhraseGapMs || gap < TimeSpan.Zero)
                OpenPhrase();
            else
                Advance();

            _last = nowUtc;
            return new Cue(_degree, 0.84 + _rng.NextDouble() * 0.16);
        }
    }

    /// <summary>End the phrase by hand: the next crossing opens a new one whenever it comes.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _inPhrase = false;
            _last = DateTime.MinValue;
        }
    }

    /// <summary>A new phrase: its own register, its own taste for leaps.</summary>
    private void OpenPhrase()
    {
        _lo = Low + _rng.Next(High - Low + 2 - Span);
        _hi = _lo + Span - 1;
        _degree = _lo + _rng.Next(Math.Min(3, Span));
        _leaps = _rng.NextDouble() < LeapingPhrase;
        _motif = Array.Empty<int>();
        _lastMotif = null;
        _at = 0;
        _heard = 0;
        _inPhrase = true;
    }

    /// <summary>One move along the line.</summary>
    private void Advance()
    {
        var step = MotifStep();
        var landed = Fold(_degree + step);

        // Reflecting off the end of the window can land back where the line already is. A stalled
        // note reads as a stuck cue, not a melody, so turn one further into the window.
        if (landed == _degree)
            landed = Fold(landed + (step > 0 ? -1 : 1));

        _degree = landed;
        _recent[_heard % _recent.Length] = landed;
        _heard++;
    }

    /// <summary>The next step of the current motif, drawing a new motif when it runs out.</summary>
    private int MotifStep()
    {
        if (_at >= _motif.Length)
        {
            // A line that has been treading water needs somewhere to go, whatever the phrase's
            // character says: four notes inside three rungs is a wobble, not a tune.
            var stuck = Treading();
            var bag = stuck || (_leaps && _rng.NextDouble() < 0.5) ? Leaps : Steps;

            var shape = bag[_rng.Next(bag.Length)];
            if (ReferenceEquals(shape, _lastMotif)) shape = bag[_rng.Next(bag.Length)];   // not twice running
            _lastMotif = shape;
            _at = 0;

            // Half the time a motif is played the other way up, so the same eleven shapes never
            // announce themselves.
            if (_rng.NextDouble() < 0.5)
            {
                var flipped = new int[shape.Length];
                for (int i = 0; i < flipped.Length; i++) flipped[i] = -shape[i];
                shape = flipped;
            }

            _motif = shape;
        }

        return _motif[_at++];
    }

    /// <summary>True when the last few notes have all sat inside three rungs of each other.</summary>
    private bool Treading()
    {
        if (_heard < _recent.Length) return false;
        int lo = int.MaxValue, hi = int.MinValue;
        foreach (var r in _recent) { lo = Math.Min(lo, r); hi = Math.Max(hi, r); }
        return hi - lo < 3;
    }

    /// <summary>
    /// Keep a degree inside the phrase's window by turning it back, not by pinning it: a line that
    /// hits the ceiling should come back down, not repeat the top note until the pointer stops.
    /// </summary>
    private int Fold(int degree)
    {
        var lo = Math.Max(0, _lo);
        var hi = Math.Min(Rungs - 1, _hi);
        if (hi <= lo) return Math.Clamp(degree, 0, Rungs - 1);

        var span = hi - lo;
        var d = degree - lo;
        var period = span * 2;
        d = ((d % period) + period) % period;
        return lo + (d <= span ? d : period - d);
    }
}
