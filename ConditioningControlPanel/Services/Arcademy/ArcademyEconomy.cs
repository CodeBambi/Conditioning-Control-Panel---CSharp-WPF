using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// THE ARCADEMY'S TILL. Two currencies, all of the arithmetic, and the prize catalog — kept in one
/// file with NO WPF and NO <see cref="App"/> references so the money can be exercised standalone.
/// Every number a player earns or spends is decided here and nowhere else; the page reports what
/// happened and the host decides what it was worth, exactly as the XP table already works.
///
/// <para>TICKETS are common and minted on every graded finish. TOKENS are rare: the first S-rank of
/// the LOCAL day, hard-capped at one, never from a zen pass. The two are independent — a token day
/// still pays its tickets.</para>
///
/// <para>NO LIVE RANDOMNESS ANYWHERE ON THIS PAGE (House Book law). The nightly payday is a pure
/// function of the UTC date seed and the enrolled roster, so every machine on the same day agrees
/// about which room is paying and the page only ever displays what <c>init</c> already told it.</para>
///
/// <para>The wallet is a plain <see cref="JObject"/> riding the meta blob under one host-owned key,
/// for the same reason the streak does: a stale or edited page must never be able to mint currency.
/// Shape (all fields ensured by <see cref="EnsureShape"/>):</para>
/// <code>
/// { "t":0, "k":0, "earnedT":0, "earnedK":0,
///   "tokenDays":["yyyy-MM-dd"],
///   "payDays":{ "yyyy-MM-dd": { "&lt;gameKey&gt;": 2 } },
///   "inv":{ "late_slip": { "n":2, "at":"yyyy-MM-dd" } },
///   "unlocks":{ "extra":false, "honors":false },
///   "log":[ { "sku":"late_slip", "cur":"t", "cost":250, "at":"yyyy-MM-dd" } ] }
/// </code>
/// </summary>
internal static class ArcademyEconomy
{
    // ============================ the money math ============================

    /// <summary>Base tickets by grade. Zen reports <c>pass</c> and pays the bottom row — a quiet
    /// night is still a night at school, it just is not a graded one.</summary>
    public static readonly IReadOnlyDictionary<string, int> TicketBase =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["S+"] = 160, ["S"] = 130, ["A"] = 100, ["B"] = 80, ["C"] = 60, ["pass"] = 40,
        };

    /// <summary>Replay decay by how many graded finishes this room has already paid for TODAY.
    /// A retake is a supported, deliberately free thing to do (the day's seed makes it the same
    /// script), so the second run pays a share and the third onward pays a token amount rather
    /// than nothing — grinding is bounded, not punished.</summary>
    private static readonly decimal[] DecayLadder = { 1.00m, 0.40m, 0.15m };

    /// <summary>Attendance bonus per streak day, and its ceiling.</summary>
    private const decimal StreakStep = 0.02m;
    private const decimal StreakCap = 0.30m;

    /// <summary>Nothing a graded finish pays ever drops under this.</summary>
    public const int TicketFloor = 10;

    /// <summary>The ordinary payday, and the rare one. <see cref="JackpotOdds"/> is a denominator:
    /// one night in thirty the lucky room pays five times instead of two.</summary>
    private const int PaydayMult = 2;
    private const int JackpotMult = 5;
    private const int JackpotOdds = 30;

    /// <summary>The Extra Credit lever's three notches. Unknown text is standard; the CALLER is
    /// what enforces the unlock rungs (see <see cref="ClampLever"/>).</summary>
    public static double LeverMult(string? lever) => (double)LeverMultDec(lever);

    private static decimal LeverMultDec(string? lever) => lever switch
    {
        "extra" => 1.5m,
        "honors" => 2.0m,
        _ => 1.0m,
    };

    /// <summary>The lever the player may actually pull, given what they have unlocked. Extra Credit
    /// opens on their first A-or-better; Honors is bought at the counter. Anything unrecognised, or
    /// a notch they have not earned, quietly becomes standard — the page proposes, the host
    /// disposes, and a forged frame can only ever cost the player money, never make it.</summary>
    public static string ClampLever(string? lever, bool extraUnlocked, bool honorsUnlocked) => lever switch
    {
        "extra" when extraUnlocked => "extra",
        "honors" when honorsUnlocked => "honors",
        _ => "standard",
    };

    /// <summary>What one graded finish is worth, broken out so the debrief can show its working.</summary>
    /// <param name="Tickets">The minted amount, post-rounding and post-floor.</param>
    /// <param name="Base">The grade's base row, before any multiplier.</param>
    /// <param name="Mult">Every multiplier combined, rounded to 2dp for display.</param>
    public readonly record struct TicketPayout(
        int Tickets, int Base, double Mult,
        double Decay, double Streak, int Payday, double Lever);

    /// <summary>
    /// THE ONE TICKET SUM. Base by grade, then the replay ladder, then the attendance bonus, then
    /// tonight's payday, then the lever last of all, then round half-up and floor.
    ///
    /// <para>Pure: every input is passed in, nothing is read off a clock, and the same arguments
    /// always produce the same number. That is what makes it testable without a WebView.</para>
    /// </summary>
    /// <param name="grade">S+/S/A/B/C/pass. Anything else pays the C row.</param>
    /// <param name="priorFinishesToday">Graded finishes this room has ALREADY paid for today (0 for
    /// the first of the day).</param>
    /// <param name="streak">The attendance streak AFTER today's credit.</param>
    /// <param name="paydayMult">1 when this room is not tonight's lucky one, otherwise 2 or 5.</param>
    /// <param name="lever">The clamped lever notch.</param>
    public static TicketPayout ComputeTickets(string? grade, int priorFinishesToday, int streak,
        int paydayMult, string? lever)
    {
        var baseAmount = TicketBase.TryGetValue(grade ?? "", out var b) ? b : TicketBase["C"];

        var idx = Math.Clamp(priorFinishesToday, 0, DecayLadder.Length - 1);
        var decay = DecayLadder[idx];

        var streakBonus = 1.0m + Math.Min(Math.Max(streak, 0) * StreakStep, StreakCap);

        var payday = paydayMult is PaydayMult or JackpotMult ? paydayMult : 1;
        var leverMult = LeverMultDec(lever);

        // DECIMAL, not double, all the way to the rounding. Every rate in the table is a base-10
        // fraction that binary floating point cannot hold exactly, and the error lands precisely
        // where it hurts: 0.15 x 1.5 x 100 comes out as 22.499999999999996, so a half-up rounding
        // that should have paid 23 quietly pays 22. Money rounds in decimal.
        var mult = decay * streakBonus * payday * leverMult;
        // Round half-up at the very end (Math.Round is banker's rounding, which would shave a
        // ticket off every .5 in the table).
        var tickets = (int)Math.Floor(baseAmount * mult + 0.5m);
        if (tickets < TicketFloor) tickets = TicketFloor;

        return new TicketPayout(tickets, baseAmount, (double)Math.Round(mult, 2),
            (double)decay, (double)Math.Round(streakBonus, 2), payday, (double)leverMult);
    }

    /// <summary>Is this a grade that mints a token? S and S+ only, and never a zen pass.</summary>
    public static bool IsTokenGrade(string? grade, bool zen) =>
        !zen && (string.Equals(grade, "S", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(grade, "S+", StringComparison.OrdinalIgnoreCase));

    /// <summary>Is this a grade that opens the Extra Credit notch? A or better.</summary>
    public static bool IsExtraCreditGrade(string? grade) =>
        string.Equals(grade, "A", StringComparison.OrdinalIgnoreCase) || IsTokenGrade(grade, false);

    // ============================ tonight's payday ============================

    /// <summary>Which room is paying extra tonight, and by how much.</summary>
    public readonly record struct Payday(string? GameKey, int Mult);

    /// <summary>
    /// THE NIGHTLY DRAW, school-wide and seeded. Every enrolled room is scored by hashing the UTC
    /// date seed with its own key and the highest score wins, which makes the answer independent of
    /// the ORDER the roster arrives in — a caller that sorts and a caller that does not agree, and
    /// there is no live roll anywhere to drift between machines.
    ///
    /// <para>An empty roster has no payday at all (mult 1), which is the honest answer on a save
    /// where nobody has enrolled in anything yet.</para>
    /// </summary>
    public static Payday PickPayday(string? utcDateSeed, IEnumerable<string>? enrolledGameKeys)
    {
        if (string.IsNullOrWhiteSpace(utcDateSeed)) return new Payday(null, 1);
        var keys = (enrolledGameKeys ?? Enumerable.Empty<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (keys.Count == 0) return new Payday(null, 1);

        string? best = null;
        uint bestScore = 0;
        foreach (var k in keys)
        {
            var score = Hash("payday|" + utcDateSeed + "|" + k);
            // Ordinal tie-break so a hash collision cannot make the answer depend on list order.
            if (best == null || score > bestScore
                || (score == bestScore && string.CompareOrdinal(k, best) < 0))
            {
                best = k;
                bestScore = score;
            }
        }

        var jackpot = Hash("jackpot|" + utcDateSeed) % JackpotOdds == 0;
        return new Payday(best, jackpot ? JackpotMult : PaydayMult);
    }

    /// <summary>Tonight's multiplier for ONE room: 1 unless it is the room the draw picked.</summary>
    public static int PaydayMultFor(Payday payday, string? gameKey) =>
        payday.GameKey != null && string.Equals(payday.GameKey, gameKey, StringComparison.Ordinal)
            ? payday.Mult : 1;

    /// <summary>FNV-1a over UTF-16 code units. Small, stable, and identical on every machine — the
    /// point is reproducibility, not cryptography, and <see cref="string.GetHashCode()"/> is
    /// explicitly randomised per process, which would break exactly the property we need.</summary>
    private static uint Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var ch in s)
            {
                h ^= ch;
                h *= 16777619;
            }
            return h;
        }
    }

    // ============================ the prize catalog ============================

    /// <summary>One row on the shelf. <c>Cur</c> is "t" (tickets) or "k" (tokens); <c>Kind</c> is
    /// cosmetic / consumable / unlock / display.</summary>
    /// <param name="StackMax">Consumables only: how many the player may hold at once.</param>
    /// <param name="Locked">A case slot that is dressed but not for sale yet.</param>
    /// <param name="Wave">Which RESTOCK this row rides in on. See <see cref="CurrentWave"/>.</param>
    public sealed record CatalogItem(
        string Sku, string Cur, int Cost, string Kind,
        string NameKey, string NameEn, string BlurbKey, string BlurbEn,
        int StackMax = 0, bool Locked = false, int Wave = 1);

    /// <summary>
    /// THE TRUCK HAS BEEN THIS FAR. Rows above this number are not on the wire at all — not
    /// "locked", not greyed out, simply absent, because a restock should APPEAR one night rather
    /// than sit on the shelf for weeks with a padlock on it spoiling its own arrival. Bumping this
    /// by one is the whole of shipping the next wave.
    ///
    /// <para>The host refuses an above-wave sku at <see cref="Buy"/> too, with reason
    /// <c>unknown</c>: a page that never saw the row cannot name it, so the only caller who could
    /// is a hand-edited one, and "the counter does not know that one" is the honest answer.</para>
    /// </summary>
    public const int CurrentWave = 2;

    /// <summary>Is this row on the shelf tonight? The one place the wave gate is spelled out, so
    /// the projection and the buy path can never drift apart.</summary>
    public static bool InStock(CatalogItem c) => c.Wave <= CurrentWave;

    public const string CurTickets = "t";
    public const string CurTokens = "k";

    public const string SkuLateSlip = "late_slip";
    public const string SkuHonorsLever = "honors_lever";
    public const string SkuFreeSwimKey = "free_swim_key";
    public const string SkuDeepEndWideBoard = "de_5x5";

    /// <summary>The one restock row C# itself has to know by name: the tube's darker glass is the
    /// only prize that lights up OUTSIDE the Arcademy (AvatarTubeWindow.SetTubeStyle).</summary>
    public const string SkuTubeMidnight = "tube_midnight";

    /// <summary>
    /// THE SHELF. C# is the single source: <c>init</c> projects this and the page only renders it,
    /// so nothing about a price or an effect can be argued from the page side.
    ///
    /// <para>NOTHING ON SAFETY IS EVER FOR SALE (three-tier law) — no intensity ceiling, no consent
    /// rung, no mute, no panic key. What is here is frames, garnish, a streak insurance slip, and
    /// three doors that only ever ADD an option.</para>
    /// </summary>
    public static readonly IReadOnlyList<CatalogItem> Catalog = new List<CatalogItem>
    {
        new("id_frame_gold", CurTickets, 300, "cosmetic",
            "prize_id_frame_gold", "Gold Pinstripe Frame",
            "prize_id_frame_gold_blurb",
            "A thin gold pinstripe around your ID photo, for the student who likes being seen."),
        new("id_frame_navy", CurTickets, 300, "cosmetic",
            "prize_id_frame_navy", "Navy Varsity Frame",
            "prize_id_frame_navy_blurb",
            "Deep navy with a varsity edge, the kind the old team photos in the hall wear."),
        new("confetti_stamp", CurTickets, 150, "cosmetic",
            "prize_confetti_stamp", "Confetti Stamp",
            "prize_confetti_stamp_blurb",
            "Your stamp lands in a little burst of paper now, every single time it lands."),
        new(SkuLateSlip, CurTickets, 250, "consumable",
            "prize_late_slip", "Late Slip",
            "prize_late_slip_blurb",
            "Slide one across the desk and a single missed day never touches your streak.",
            StackMax: 3),
        new(SkuHonorsLever, CurTokens, 1, "unlock",
            "prize_honors_lever", "Honors Lever",
            "prize_honors_lever_blurb",
            "Unbolts the third notch on the lever, which is where the S+ nights live."),
        new(SkuFreeSwimKey, CurTokens, 1, "unlock",
            "prize_free_swim_key", "Free Swim Key",
            "prize_free_swim_key_blurb",
            "Opens Free Swim on every room you are enrolled in, punched card or not."),
        new(SkuDeepEndWideBoard, CurTokens, 2, "unlock",
            "prize_de_5x5", "The Wide Board",
            "prize_de_5x5_blurb",
            "Adds the roomy 5x5 board to The Deep End, for a longer and gentler soak."),
        new("jukebox", CurTokens, 3, "display",
            "prize_jukebox", "Jukebox",
            "prize_jukebox_blurb",
            "The slot is dressed and the case is empty. The desk says it is on the truck.",
            Locked: true),

        // ============================ THE RESTOCK (2026-08-26) ============================
        // Fourteen rows over three waves - eleven the night the truck came, plus the three
        // outfits THE LOCKER hung up on 2026-08-28. Everything below is dressing: a walker's
        // kit, a bell, a poster tube, a voice on the PA, two campus looks, EMI's desk, her
        // jacket, three more looks for her wardrobe, and one darker pane of glass for the tube
        // back home. Nothing here touches safety, nothing charges to play (the pitched retake
        // ticket is CUT FOREVER - replays stay free), and every one of them is witnessed by
        // `wallet.inv` alone: no new unlock flags.
        //
        // WAVE 1 - ships with the restock.
        new("away_colors", CurTickets, 100, "cosmetic",
            "prize_away_colors", "AWAY COLORS",
            "prize_away_colors_blurb",
            "Alternate kit for your little walker. Same you, sharper stripes.",
            Wave: 1),
        new("sparkler_steps", CurTickets, 250, "cosmetic",
            "prize_sparkler_steps", "SPARKLER STEPS",
            "prize_sparkler_steps_blurb",
            "A trail of little sparks wherever you walk. The janitor has given up complaining.",
            Wave: 1),
        new("brass_bell", CurTickets, 80, "cosmetic",
            "prize_brass_bell", "THE BRASS BELL",
            "prize_brass_bell_blurb",
            "The old bell from the storage room takes over. Rings a little warmer than the new one.",
            Wave: 1),
        new("emi_desk_toy", CurTokens, 2, "unlock",
            "prize_emi_desk_toy", "EMI'S DESK TOY",
            "prize_emi_desk_toy_blurb",
            "A little something for her desk. She'll fidget with it and pretend she doesn't love it.",
            Wave: 1),

        // WAVE 2 - ships with the restock as well (CurrentWave == 2).
        new("poster_drop_1", CurTickets, 60, "cosmetic",
            "prize_poster_drop_1", "POSTER DROP NO 1",
            "prize_poster_drop_1_blurb",
            "Fresh prints for the corkboard, motivational in a way we can't quite explain.",
            Wave: 2),
        new("pa_pack", CurTickets, 300, "cosmetic",
            "prize_pa_pack", "PA ANNOUNCER",
            "prize_pa_pack_blurb",
            "The morning announcements get a voice. She mostly reads the schedule, mostly.",
            Wave: 2),
        new("theme_drone", CurTokens, 2, "unlock",
            "prize_theme_drone", "DRONE PROTOCOL",
            "prize_theme_drone_blurb",
            "Somebody left a strange cartridge in the AV room and now the campus runs green. We like it.",
            Wave: 2),
        // THE LOCKER (2026-08-28). Three outfits, sold one at a time rather than as a bundle so
        // a player can buy the one they actually want. Plain cosmetics on tickets: no unlock
        // flag, no StackMax, so a second press is answered `owned` like every other one-off.
        // The jacket stays a wave-3 token row - these three are the ticket road to the wardrobe.
        new("emi_labcoat", CurTickets, 180, "cosmetic",
            "prize_emi_labcoat", "LAB COAT",
            "prize_emi_labcoat_blurb",
            "White coat, pocket protector, the clipboard she never writes on. She looks like she is about to grade you.",
            Wave: 2),
        new("emi_cheer", CurTickets, 260, "cosmetic",
            "prize_emi_cheer", "CHEER UNIFORM",
            "prize_emi_cheer_blurb",
            "Navy and pink, pleats and all. The pom-poms are not optional and neither is the chant.",
            Wave: 2),
        new("emi_swim", CurTickets, 340, "cosmetic",
            "prize_emi_swim", "SWIM TEAM",
            "prize_emi_swim_blurb",
            "Lane four, goggles up. Free Swim was always going to end up here.",
            Wave: 2),

        // WAVE 3 - built, tested, and NOT on the wire yet. One bump of CurrentWave ships it.
        new("ghost_walk", CurTickets, 220, "cosmetic",
            "prize_ghost_walk", "GHOST WALK",
            "prize_ghost_walk_blurb",
            "Your walker goes see-through with a soft afterimage. Spooky in a fun way, we checked.",
            Wave: 3),
        new("theme_snowday", CurTokens, 2, "unlock",
            "prize_theme_snowday", "SNOW DAY",
            "prize_theme_snowday_blurb",
            "Frost on the windows, snow in the courtyard, everything soft and blue. Classes run anyway.",
            Wave: 3),
        new("emi_varsity", CurTokens, 2, "unlock",
            "prize_emi_varsity", "EMI: VARSITY JACKET",
            "prize_emi_varsity_blurb",
            "She found it in lost and found and it fits perfectly. Every one of her poses, re-dressed.",
            Wave: 3),
        new(SkuTubeMidnight, CurTickets, 160, "cosmetic",
            "prize_tube_midnight", "TUBE GLASS: MIDNIGHT",
            "prize_tube_midnight_blurb",
            "A darker glass for the tube back home. It ships to the whole app, not just the school.",
            Wave: 3),
    };

    public static CatalogItem? Find(string? sku) =>
        sku == null ? null : Catalog.FirstOrDefault(c => string.Equals(c.Sku, sku, StringComparison.Ordinal));

    /// <summary>Which unlock flag a token door flips, if any. Kept beside the catalog so a new
    /// unlock is one row plus one line here rather than a hunt through the host.</summary>
    private static string? UnlockFlagFor(string sku) => sku switch
    {
        SkuHonorsLever => "honors",
        _ => null,
    };

    // ============================ wallet ============================

    private const int TokenDayHistory = 14;
    private const int PayDayHistory = 14;
    private const int LogHistory = 40;

    /// <summary>Fill in every field the rest of this file assumes, in place. Safe to call on an
    /// empty object, on a half-written one, and on a wallet whose fields were left the wrong type
    /// by a hand-edited save — a wrong type is REPLACED rather than trusted, because a wallet that
    /// throws mid-payout would cost the player the class credit riding the same frame.</summary>
    public static JObject EnsureShape(JObject? w)
    {
        var wallet = w ?? new JObject();
        EnsureInt(wallet, "t");
        EnsureInt(wallet, "k");
        EnsureInt(wallet, "earnedT");
        EnsureInt(wallet, "earnedK");
        if (wallet["tokenDays"] is not JArray) wallet["tokenDays"] = new JArray();
        if (wallet["payDays"] is not JObject) wallet["payDays"] = new JObject();
        if (wallet["inv"] is not JObject) wallet["inv"] = new JObject();
        if (wallet["unlocks"] is not JObject) wallet["unlocks"] = new JObject();
        if (wallet["log"] is not JArray) wallet["log"] = new JArray();
        var unlocks = (JObject)wallet["unlocks"]!;
        if (unlocks["extra"] is not JValue { Type: JTokenType.Boolean }) unlocks["extra"] = false;
        if (unlocks["honors"] is not JValue { Type: JTokenType.Boolean }) unlocks["honors"] = false;
        return wallet;
    }

    private static void EnsureInt(JObject w, string key)
    {
        var v = w[key] as JValue;
        if (v is { Type: JTokenType.Integer }) return;
        int parsed = 0;
        if (v is { Type: JTokenType.Float }) { try { parsed = Convert.ToInt32((double)v); } catch { parsed = 0; } }
        w[key] = Math.Max(parsed, 0);
    }

    public static int Tickets(JObject w) => (int?)EnsureShape(w)["t"] ?? 0;
    public static int Tokens(JObject w) => (int?)EnsureShape(w)["k"] ?? 0;

    public static bool ExtraUnlocked(JObject w) => (bool?)EnsureShape(w)["unlocks"]?["extra"] ?? false;
    public static bool HonorsUnlocked(JObject w) => (bool?)EnsureShape(w)["unlocks"]?["honors"] ?? false;

    /// <summary>Does the player hold this sku at all? Cosmetics and unlocks are one-and-done;
    /// a consumable answers true while any remain.</summary>
    public static bool Owns(JObject w, string sku) => Held(w, sku) > 0;

    /// <summary>How many of a sku are on hand (1 for an owned one-off, 0 for anything unowned).</summary>
    public static int Held(JObject w, string sku)
    {
        var inv = EnsureShape(w)["inv"] as JObject;
        if (inv?[sku] is not JObject row) return 0;
        return Math.Max((int?)row["n"] ?? 0, 0);
    }

    /// <summary>Credit tickets. Both the balance and the lifetime total move together — the second
    /// is what the records room reads, and it must never go down when the first is spent.</summary>
    public static void EarnTickets(JObject w, int amount)
    {
        if (amount <= 0) return;
        var wallet = EnsureShape(w);
        wallet["t"] = ((int?)wallet["t"] ?? 0) + amount;
        wallet["earnedT"] = ((int?)wallet["earnedT"] ?? 0) + amount;
    }

    /// <summary>
    /// THE ONE TOKEN A DAY. Claimed against the LOCAL date, cloned from the XP ledger's shape: true
    /// the first time a date is seen and false forever after, so a second S before midnight mints
    /// nothing no matter how the frame is dressed.
    /// </summary>
    /// <returns>True when a token was actually minted.</returns>
    public static bool TryMintToken(JObject w, string? localDate)
    {
        if (string.IsNullOrWhiteSpace(localDate)) return false;
        var wallet = EnsureShape(w);
        var days = (JArray)wallet["tokenDays"]!;
        if (days.Any(d => string.Equals((string?)d, localDate, StringComparison.Ordinal))) return false;

        days.Add(localDate);
        // Ordinal order IS chronological for yyyy-MM-dd, so the ledger's SkipLast shape works here.
        var kept = days.Select(d => (string?)d).Where(d => d != null)
            .OrderBy(d => d, StringComparer.Ordinal)
            .TakeLast(TokenDayHistory).ToList();
        if (kept.Count < days.Count)
        {
            days.RemoveAll();
            foreach (var d in kept) days.Add(d);
        }

        wallet["k"] = ((int?)wallet["k"] ?? 0) + 1;
        wallet["earnedK"] = ((int?)wallet["earnedK"] ?? 0) + 1;
        return true;
    }

    /// <summary>
    /// Count one paid finish for (day, room) and answer how many came BEFORE it — which is exactly
    /// the index the decay ladder wants. Called once per graded finish, before the sum.
    /// </summary>
    public static int NotePlay(JObject w, string? localDate, string? gameKey)
    {
        if (string.IsNullOrWhiteSpace(localDate) || string.IsNullOrWhiteSpace(gameKey)) return 0;
        var wallet = EnsureShape(w);
        var days = (JObject)wallet["payDays"]!;
        if (days[localDate] is not JObject day)
        {
            day = new JObject();
            days[localDate] = day;
        }
        var prior = Math.Max((int?)day[gameKey] ?? 0, 0);
        day[gameKey] = prior + 1;

        foreach (var stale in days.Properties().Select(p => p.Name)
                     .OrderBy(n => n, StringComparer.Ordinal)
                     .SkipLast(PayDayHistory).ToList())
        {
            days.Remove(stale);
        }
        return prior;
    }

    /// <summary>Open the Extra Credit notch. Idempotent, and only ever opens.</summary>
    /// <returns>True the once, when it actually flipped.</returns>
    public static bool TryUnlockExtraCredit(JObject w, string? grade)
    {
        if (!IsExtraCreditGrade(grade)) return false;
        var wallet = EnsureShape(w);
        var unlocks = (JObject)wallet["unlocks"]!;
        if ((bool?)unlocks["extra"] == true) return false;
        unlocks["extra"] = true;
        return true;
    }

    /// <summary>
    /// SPEND A LATE SLIP. Called from the attendance path when the gap is exactly one missed day:
    /// one slip comes off the stack and the streak carries on as though the player had been there.
    /// </summary>
    /// <returns>True when a slip was actually spent.</returns>
    public static bool ConsumeLateSlip(JObject w)
    {
        var wallet = EnsureShape(w);
        var inv = (JObject)wallet["inv"]!;
        if (inv[SkuLateSlip] is not JObject row) return false;
        var n = Math.Max((int?)row["n"] ?? 0, 0);
        if (n <= 0) return false;
        if (n == 1) inv.Remove(SkuLateSlip);
        else row["n"] = n - 1;
        return true;
    }

    /// <summary>The outcome of a <c>prize-buy</c>. <c>Reason</c> is one of unknown / poor / owned /
    /// full / locked, and it is null when the trade went through.</summary>
    public readonly record struct BuyResult(bool Ok, string? Reason, CatalogItem? Item);

    /// <summary>
    /// THE COUNTER. Validation is host-side only, in this order: is it a thing, is it for sale, do
    /// they already have it, is their pocket full, can they afford it. Nothing is deducted until
    /// every one of those has passed, so a refusal can never leave the wallet half-spent.
    /// </summary>
    public static BuyResult Buy(JObject w, string? sku, string? localDate)
    {
        var item = Find(sku);
        if (item == null) return new BuyResult(false, "unknown", null);
        // A row the truck has not brought yet is not on the wire, so no honest page can ask for
        // it. Answered "unknown" rather than "locked" on purpose: "locked" is the dressed empty
        // case the player can SEE, and naming a wave-3 prize in a refusal would spoil it.
        if (!InStock(item)) return new BuyResult(false, "unknown", null);
        if (item.Locked) return new BuyResult(false, "locked", item);

        var wallet = EnsureShape(w);
        var held = Held(wallet, item.Sku);
        if (item.Kind == "consumable")
        {
            if (held >= Math.Max(item.StackMax, 1)) return new BuyResult(false, "full", item);
        }
        else if (held > 0) return new BuyResult(false, "owned", item);

        var purse = item.Cur == CurTokens ? "k" : "t";
        var have = (int?)wallet[purse] ?? 0;
        if (have < item.Cost) return new BuyResult(false, "poor", item);

        wallet[purse] = have - item.Cost;

        var inv = (JObject)wallet["inv"]!;
        if (inv[item.Sku] is JObject row) row["n"] = held + 1;
        else inv[item.Sku] = new JObject { ["n"] = 1 };
        ((JObject)inv[item.Sku]!)["at"] = localDate ?? "";

        var flag = UnlockFlagFor(item.Sku);
        if (flag != null) ((JObject)wallet["unlocks"]!)[flag] = true;

        var log = (JArray)wallet["log"]!;
        log.Add(new JObject
        {
            ["sku"] = item.Sku,
            ["cur"] = item.Cur,
            ["cost"] = item.Cost,
            ["at"] = localDate ?? "",
        });
        while (log.Count > LogHistory) log.RemoveAt(0);

        return new BuyResult(true, null, item);
    }

    // ============================ projections ============================

    /// <summary>The catalog as <c>init.economy.catalog</c> — every row, priced, with both the
    /// lexicon key and the neutral English so a page with a partial table still reads.</summary>
    public static JArray CatalogJson()
    {
        var arr = new JArray();
        foreach (var c in Catalog)
        {
            // Above the current wave = ABSENT, not locked (see CurrentWave). The page renders
            // what it is handed, so a row that is not here is a row that does not exist tonight.
            if (!InStock(c)) continue;
            var row = new JObject
            {
                ["sku"] = c.Sku,
                ["cur"] = c.Cur,
                ["cost"] = c.Cost,
                ["kind"] = c.Kind,
                ["nameKey"] = c.NameKey,
                ["nameEn"] = c.NameEn,
                ["blurbKey"] = c.BlurbKey,
                ["blurbEn"] = c.BlurbEn,
            };
            // `max` on the wire, not `stackMax`: the counter reads `row.max` for its "2 of 3"
            // badge, and the shorter name is the one the page already had. Display only either
            // way - the host is what refuses the third late slip, with reason "full".
            if (c.StackMax > 0) row["max"] = c.StackMax;
            if (c.Locked) row["locked"] = true;
            arr.Add(row);
        }
        return arr;
    }

    /// <summary>A clone of what the player holds, for the counter's owned badges.</summary>
    public static JObject InvJson(JObject w) => (JObject)EnsureShape(w)["inv"]!.DeepClone();

    /// <summary>A clone of the two lever rungs.</summary>
    public static JObject UnlocksJson(JObject w) => (JObject)EnsureShape(w)["unlocks"]!.DeepClone();

    /// <summary>Just the balances — what every reply carries so the page never has to add up.</summary>
    public static JObject BalanceJson(JObject w)
    {
        var wallet = EnsureShape(w);
        return new JObject { ["t"] = (int?)wallet["t"] ?? 0, ["k"] = (int?)wallet["k"] ?? 0 };
    }

    // ============================ dates ============================

    /// <summary>Whole days from <paramref name="from"/> to <paramref name="to"/>, or -1 when either
    /// date is missing or unreadable. Same yyyy-MM-dd invariant shape the rest of the school uses.
    /// A gap of 1 is an unbroken streak; a gap of 2 is the one a late slip can cover.</summary>
    public static int DayGap(string? from, string? to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return -1;
        if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var a)) return -1;
        if (!DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var b)) return -1;
        var days = (b.Date - a.Date).TotalDays;
        return days is >= 0 and < int.MaxValue ? (int)days : -1;
    }
}
