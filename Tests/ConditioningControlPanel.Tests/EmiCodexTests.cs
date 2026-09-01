using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BOOK (Ask EMI, wave 2), on the half of it that can fail in silence.
///
/// <para>Almost everything the codex does is a string crossing a boundary: a moment id fired at a
/// forgiving bus, a target id arriving from a web page, a <c>TutorialType</c> NAME arriving from
/// the same page, a loc key looked up through a manager that answers a miss with the key itself.
/// None of those throw when they are wrong. They just quietly do nothing, forever, which is
/// exactly what a play-test cannot tell from a feature nobody happened to click.</para>
///
/// <para><b>What is deliberately not touched here.</b> <see cref="EmiState"/> reads and writes the
/// real user's <c>emi-desk.json</c>, so nothing below goes near the bookmark, the open counter or
/// <see cref="EmiCodex.MaybeOffer"/> - the same rule <c>EmiRingCatalogueTests</c> keeps. Every
/// bridge case asserted here is one that returns before it can reach the state singleton.</para>
/// </summary>
public class EmiCodexTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string AppDir() => Path.Combine(RepoRoot(), "ConditioningControlPanel");

    private static Dictionary<string, string> English()
    {
        var path = Path.Combine(SourceRoots.LanguagesDirectory, "en.json");
        Assert.True(File.Exists(path), path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) d[p.Name] = p.Value.GetString() ?? string.Empty;
        return d;
    }

    // =====================================================================================
    //  the ring row
    // =====================================================================================

    /// <summary>
    /// SIXTH, and this is the whole reason the assertion is worth a test. Catalogue order IS the
    /// ring before any usage exists, so the first six entries belong to a brand new user. The book
    /// shipped seventh because taking a ring slot is an owner call, not a build decision - and on
    /// 2026-08-30 the owner made it: the manual belongs in a new user's ring, videos steps down.
    /// </summary>
    [Fact]
    public void The_book_sits_sixth_in_the_catalogue()
    {
        Assert.Equal(5, EmiTargets.OrderOf(EmiCodex.TargetId));
        Assert.Equal(EmiCodex.TargetId, EmiTargets.All[5].Id);
    }

    /// <summary>
    /// The book is the manual. Gating it would lock a first-run user out of the explanation of the
    /// thing they cannot use yet, which is the one door where a padlock is actively harmful.
    /// </summary>
    [Fact]
    public void The_book_is_always_available_and_never_locked()
    {
        var t = EmiTargets.Find(EmiCodex.TargetId);
        Assert.NotNull(t);
        Assert.True(t!.Available, "the book must never be hidden from anybody");
        Assert.False(t.Locked, "the book must never be behind a tier gate");
        Assert.Null(t.Gate);
    }

    [Fact]
    public void The_book_declares_no_art_and_the_conventional_label_key()
    {
        var t = EmiTargets.Find(EmiCodex.TargetId)!;
        Assert.Equal("emi_desk_target_" + EmiCodex.TargetId, t.LabelKey);
        // No PNG ships in wave 2 - a ThumbPath here would resolve to nothing and paint the hue
        // tile anyway, but only after a logged miss on every ring composition.
        Assert.Null(t.ThumbPath);
    }

    /// <summary>The label key is a RING key, and the ring's own suite demands every one of them in
    /// all nine language files. English-only means one English value in nine files, not one file.</summary>
    [Fact]
    public void The_label_is_in_all_nine_language_files_and_fits_a_card()
    {
        var dir = SourceRoots.LanguagesDirectory;
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(9, files.Length);
        foreach (var f in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            var ok = doc.RootElement.TryGetProperty("emi_desk_target_codex", out var v);
            Assert.True(ok, Path.GetFileName(f) + " is missing emi_desk_target_codex");
            var s = v.GetString() ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(s), Path.GetFileName(f));
            Assert.True(s.Length <= 14, $"{Path.GetFileName(f)}: \"{s}\" is {s.Length} chars");
        }
    }

    // =====================================================================================
    //  the strings the window asks for
    // =====================================================================================

    /// <summary>
    /// Every key the C# lane looks up, checked against the shipped English file. A missing key does
    /// not throw and does not blank the UI - <c>LocalizationManager</c> answers with the key itself
    /// - so the failure mode is a window with "emi_codex_close" written on a button.
    /// </summary>
    [Theory]
    [InlineData("emi_codex_title")]
    [InlineData("emi_codex_plain_heading")]
    [InlineData("emi_codex_plain_note")]
    [InlineData("emi_codex_why_runtime")]
    [InlineData("emi_codex_why_bundle")]
    [InlineData("emi_codex_why_navigation")]
    [InlineData("emi_codex_why_process")]
    [InlineData("emi_codex_empty")]
    [InlineData("emi_codex_volume")]
    [InlineData("emi_codex_limit_label")]
    [InlineData("emi_codex_open_site")]
    [InlineData("emi_codex_close")]
    public void Every_codex_string_exists_in_english(string key)
    {
        var en = English();
        Assert.True(en.ContainsKey(key), "missing loc key " + key);
        Assert.False(string.IsNullOrWhiteSpace(en[key]), key);
    }

    /// <summary>
    /// The two house rules the language files have been bitten by before: a literal line break
    /// inside a value parses in Newtonsoft and nowhere else, and dashes are plain hyphens.
    /// </summary>
    [Fact]
    public void The_codex_strings_keep_the_house_rules()
    {
        foreach (var kv in English().Where(k => k.Key.StartsWith("emi_codex_", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("\n", kv.Value);
            Assert.DoesNotContain("\r", kv.Value);
            Assert.DoesNotContain("—", kv.Value);   // em dash
            Assert.DoesNotContain("–", kv.Value);   // en dash
        }
    }

    // =====================================================================================
    //  "walk me through it"
    // =====================================================================================

    [Theory]
    [InlineData("ShortWalk", ConditioningControlPanel.Services.TutorialType.ShortWalk)]
    [InlineData("shortwalk", ConditioningControlPanel.Services.TutorialType.ShortWalk)]
    [InlineData("  Settings  ", ConditioningControlPanel.Services.TutorialType.Settings)]
    [InlineData("UpgradeTour", ConditioningControlPanel.Services.TutorialType.UpgradeTour)]
    public void A_real_tutorial_name_parses(string name, ConditioningControlPanel.Services.TutorialType expected)
    {
        Assert.True(EmiCodex.TryParseTour(name, out var t));
        Assert.Equal(expected, t);
    }

    /// <summary>
    /// THE ORDINAL TRAP. <c>Enum.TryParse</c> accepts any number as a valid enum value, defined or
    /// not, so a page (or a chapter file merged from a later wave) that sent "12" would start
    /// whichever tour happened to sit at that ordinal in this build - and the whole reason the
    /// contract says NAME and never ordinal is that the enum has values inserted into its middle.
    /// </summary>
    [Theory]
    [InlineData("12")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,2")]
    public void A_numeric_tour_is_refused(string name)
    {
        Assert.False(EmiCodex.TryParseTour(name, out _));
    }

    /// <summary>
    /// The contract's own chapter-schema example writes <c>"tour": "SettingsTour"</c>, which is not
    /// a <c>TutorialType</c> - the value is <c>Settings</c>. A chapter authored from that example
    /// silently gets no walk button, so the refusal is pinned here where it is legible.
    /// </summary>
    [Theory]
    [InlineData("SettingsTour")]
    [InlineData("ShortWalkTour")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_unknown_tour_name_is_refused(string? name)
    {
        Assert.False(EmiCodex.TryParseTour(name, out _));
    }

    // =====================================================================================
    //  the bridge
    // =====================================================================================

    /// <summary>
    /// Builds an envelope and dispatches it the way <c>OnPageMessage</c> does: the message name is
    /// read back OUT of the object, never passed alongside it. Passing it alongside is what let the
    /// `tour`/`type` collision live - a payload field named `type` overwrote the envelope key, and
    /// the harness handed HandleMessage the right name anyway from its own argument.
    /// </summary>
    private static bool Handle(string type, params (string Key, string? Value)[] fields)
    {
        var o = new JObject { ["type"] = type };
        foreach (var (k, v) in fields) o[k] = v;
        return EmiCodex.HandleMessage((string?)o["type"], o);
    }

    [Fact]
    public void An_unknown_message_does_nothing()
    {
        Assert.False(Handle("codex:launch-missiles"));
        Assert.False(Handle("ready"));           // the host's own handshake is not one of the five
        Assert.False(Handle(""));
    }

    [Fact]
    public void Ready_is_accepted_and_costs_nothing()
    {
        Assert.True(Handle(EmiCodex.MsgReady));
    }

    /// <summary>
    /// A page asking for a door nobody has heard of does NOTHING - not an exception, not a guess at
    /// the nearest id. Returns before the catalogue's Open is reached, so no door is touched.
    /// </summary>
    [Theory]
    [InlineData("not-a-door")]
    [InlineData("Codex")]        // ids are ordinal; the catalogue's is lowercase
    [InlineData("")]
    [InlineData(null)]
    public void Take_me_there_refuses_an_unknown_target(string? id)
    {
        Assert.False(EmiCodex.TakeMeThere(id));
        Assert.False(Handle(EmiCodex.MsgTarget, ("id", id)));
    }

    /// <summary>
    /// TIER AWARENESS, on the only half of it a test can reach without a running app: every id the
    /// page could legitimately send resolves to a real catalogue row, and every one of those rows
    /// answers <c>Locked</c> without throwing. The refusal itself lives in <c>EmiTargets.Pick</c>,
    /// which is where the tier gate and <c>lockedCardTapped</c> already are.
    /// </summary>
    [Fact]
    public void Every_catalogue_row_can_answer_the_lock_question()
    {
        foreach (var t in EmiTargets.All)
        {
            var ex = Record.Exception(() => { _ = t.Locked; _ = t.Available; });
            Assert.True(ex == null, $"\"{t.Id}\" threw out of its probes: {ex}");
        }
    }

    [Theory]
    [InlineData("SettingsTour")]
    [InlineData("99")]
    [InlineData(null)]
    public void Walk_me_through_it_refuses_an_unparseable_tour(string? name)
    {
        Assert.False(EmiCodex.WalkMeThrough(name));
        Assert.False(Handle(EmiCodex.MsgTour, ("tour", name)));
    }

    /// <summary>
    /// The tour name travels as <c>tour</c>. <c>type</c> names the MESSAGE and nothing else, so an
    /// envelope carrying the tour under <c>type</c> - the shape the first contract asked for - hands
    /// the parser nothing at all rather than quietly starting the wrong walk.
    /// </summary>
    [Fact]
    public void The_tour_name_is_read_from_tour_not_from_the_envelope()
    {
        var good = new JObject { ["type"] = EmiCodex.MsgTour, ["tour"] = "ShortWalk" };
        Assert.Equal("ShortWalk", EmiCodex.TourNameOf(good));
        Assert.True(EmiCodex.TryParseTour(EmiCodex.TourNameOf(good), out var parsed));
        Assert.Equal(ConditioningControlPanel.Services.TutorialType.ShortWalk, parsed);

        var collided = new JObject { ["type"] = "ShortWalk" };   // the old, wrong shape
        Assert.Null(EmiCodex.TourNameOf(collided));
    }

    /// <summary>
    /// The last link: <see cref="EmiCodex.HandleMessage"/> really does go through
    /// <c>TourNameOf</c>. The test above pins the extractor, but the extractor being right does not
    /// prove the DISPATCHER calls it - the old line read <c>payload["type"]</c> inline, and would
    /// still pass every assertion above.
    ///
    /// <para>The assertion is on the LOG, and it has to be. Headless, <c>WalkMeThrough</c> returns
    /// false whichever way it fails - there is no MainWindow to start a tour on - so a return value
    /// cannot tell "the name parsed and there was nowhere to send it" from "the handler read the
    /// wrong key". The two branches log differently, and that difference is the only observable
    /// that separates them without a running app.</para>
    /// </summary>
    [Fact]
    public void A_real_tour_member_survives_the_whole_envelope()
    {
        // Exactly what the shipped renderer posts: flat fields beside the mirrored payload, with
        // the tour member under its own `tour` alias.
        var envelope = new JObject
        {
            ["type"] = EmiCodex.MsgTour,
            ["tour"] = "ShortWalk",
            ["payload"] = new JObject { ["type"] = "ShortWalk" },
        };

        var events = CaptureLogs(() => EmiCodex.HandleMessage((string?)envelope["type"], envelope));

        Assert.DoesNotContain(events, e => e.MessageTemplate.Text.Contains("unknown tour"));
        var landed = Assert.Single(
            events.Where(e => e.MessageTemplate.Text.Contains("no main window")));

        // The enum, not the string: proof the name came out of the envelope AND through the parser.
        Assert.Equal(TutorialType.ShortWalk.ToString(),
            landed.Properties["Type"].ToString().Trim('"'));
    }

    /// <summary>
    /// Serilog's global logger, swapped for a capturing sink and put back under a lock.
    ///
    /// <para><b>The sink is filtered, and that is not tidiness.</b> <c>Log.Logger</c> is
    /// process-global and xUnit runs test CLASSES in parallel, so for the length of the swap this
    /// sink receives EVERYTHING the suite logs. The phrase the caller matches on is not unique -
    /// <c>EmiOffers</c> and <c>EmiTargets</c> log "no main window" too - so an unfiltered sink
    /// turns an <c>Assert.Single</c> into a test that passes alone and fails in company, which is
    /// worse than no test at all.</para>
    ///
    /// <para>The discriminator is the <c>Tag</c> property: everything in <c>EmiCodex</c> logs
    /// through the <c>[{Tag}]</c> template, while the neighbours hard-code <c>[EmiDesk]</c> into
    /// their text and carry no property at all.</para>
    /// </summary>
    private static readonly object LogSwap = new();

    private const string CodexLogTag = "EmiCodex";

    private static List<Serilog.Events.LogEvent> CaptureLogs(Action body)
    {
        var sink = new ListSink();
        lock (LogSwap)
        {
            var previous = Serilog.Log.Logger;
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            try { body(); }
            finally { Serilog.Log.Logger = previous; }
        }

        List<Serilog.Events.LogEvent> all;
        lock (sink.Events) all = sink.Events.ToList();

        var mine = all
            .Where(e => e.Properties.TryGetValue("Tag", out var t) &&
                        t.ToString().Trim('"') == CodexLogTag)
            .ToList();

        // If the tag ever stops being how this service marks its lines, say so here rather than
        // letting an empty list quietly satisfy a DoesNotContain.
        Assert.True(mine.Count > 0,
            "no EmiCodex events were captured - has the [{Tag}] template changed? saw: " +
            string.Join(" | ", all.Select(e => e.MessageTemplate.Text)));
        return mine;
    }

    private sealed class ListSink : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = new();
        public void Emit(Serilog.Events.LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
    }

    // =====================================================================================
    //  the boot script
    // =====================================================================================

    [Fact]
    public void The_boot_script_declares_the_page_global()
    {
        var s = EmiCodex.BuildBootScript("first-light", false);
        Assert.StartsWith("window.CCP_CODEX = ", s);
        Assert.EndsWith(";", s);

        var json = JObject.Parse(s.Substring("window.CCP_CODEX = ".Length).TrimEnd(';'));
        Assert.Equal("first-light", (string?)json["bookmark"]);
        Assert.Equal(EmiCodex.ManualUrl, (string?)json["manualUrl"]);
        Assert.False((bool)json["reducedMotion"]!);
    }

    [Fact]
    public void The_boot_script_carries_reduced_motion_and_a_null_bookmark()
    {
        var json = JObject.Parse(EmiCodex.BuildBootScript(null, true)
            .Substring("window.CCP_CODEX = ".Length).TrimEnd(';'));
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, json["bookmark"]!.Type);
        Assert.True((bool)json["reducedMotion"]!);
    }

    /// <summary>
    /// A chapter id is a merged writer's file name. Concatenating one into a script is how a stray
    /// quote turns the page's first statement into a syntax error and the whole book into a blank.
    /// </summary>
    [Fact]
    public void A_hostile_bookmark_cannot_break_the_script()
    {
        var s = EmiCodex.BuildBootScript("a\"; alert(1); //", false);
        var json = JObject.Parse(s.Substring("window.CCP_CODEX = ".Length).TrimEnd(';'));
        Assert.Equal("a\"; alert(1); //", (string?)json["bookmark"]);
    }

    // =====================================================================================
    //  the fail-soft reader
    // =====================================================================================

    /// <summary>
    /// The chapter reader answers a missing folder with an empty list, never an exception. This is
    /// the state the app is in until the renderer lane's bundle lands, and it is also the state a
    /// user is in when a content file did not install.
    /// </summary>
    [Fact]
    public void Reading_chapters_never_throws_and_comes_back_ordered()
    {
        IReadOnlyList<CodexChapter> chapters = null!;
        var ex = Record.Exception(() => chapters = EmiCodex.ReadChapters());
        Assert.Null(ex);
        Assert.NotNull(chapters);

        for (int i = 1; i < chapters.Count; i++)
        {
            var a = chapters[i - 1];
            var b = chapters[i];
            Assert.True(a.Volume < b.Volume || (a.Volume == b.Volume && a.Order <= b.Order),
                $"\"{a.Id}\" and \"{b.Id}\" are out of reading order");
        }
    }

    /// <summary>A chapter with no title at all still puts SOMETHING on a list row: an untitled row
    /// is invisible, and an invisible row is a chapter nobody can reach.</summary>
    [Theory]
    [InlineData(null, "the-panic-key", "the panic key")]
    [InlineData("   ", "the-panic-key", "the panic key")]
    [InlineData("the panic key", "the-panic-key", "the panic key")]
    public void A_chapter_row_is_never_blank(string? title, string id, string expected)
    {
        Assert.Equal(expected, new CodexChapter { Title = title, Id = id }.DisplayTitle);
    }

    [Fact]
    public void A_chapter_with_nothing_in_it_still_names_itself()
    {
        Assert.False(string.IsNullOrWhiteSpace(new CodexChapter().DisplayTitle));
    }

    /// <summary>The probes are filesystem reads that run before any window is built, on machines
    /// where the folder may simply not be there. They answer, they never throw.</summary>
    [Fact]
    public void The_bundle_probes_never_throw()
    {
        Assert.Null(Record.Exception(() =>
        {
            _ = EmiCodex.BundlePresent;
            _ = EmiCodex.HasContent;
            _ = EmiCodex.BundleRoot;
            _ = EmiCodex.ChaptersDir;
        }));
    }

    // =====================================================================================
    //  the offer verb
    // =====================================================================================

    /// <summary>
    /// There is exactly one book verb. Feasibility fails SILENTLY at draw time (LINES-SCHEMA 4), so
    /// a typo in a lines file must read as an offer that is never shown - never as a chip that is
    /// shown and then does nothing.
    /// </summary>
    [Theory]
    [InlineData("book:")]
    [InlineData("book:shut")]
    [InlineData("book:open:1")]
    public void An_unknown_book_verb_is_never_offered(string effect)
    {
        Assert.False(EmiOffers.EffectFeasible(effect));
    }

    [Fact]
    public void The_open_verb_is_the_one_the_contract_names()
    {
        Assert.Equal("book:open", EmiCodex.OpenEffect);
        // Probing it is a filesystem read plus two statics; it must answer rather than throw,
        // whether or not this machine has a bundle on disk.
        Assert.Null(Record.Exception(() => EmiOffers.EffectFeasible(EmiCodex.OpenEffect)));
    }

    /// <summary>Running an effect never throws into the caller, whatever the verb says. The widget
    /// is the way back out of most of what she fires, so it may not be taken down by one of them.</summary>
    [Theory]
    [InlineData("book:shut")]
    [InlineData("book:")]
    public void An_unknown_book_verb_runs_to_nothing(string effect)
    {
        Assert.Null(Record.Exception(() => EmiOffers.Run(effect, fromAsk: true)));
    }

    // =====================================================================================
    //  her offer, on the content side
    // =====================================================================================

    /// <summary>The whole lines file, cloned off its document so the caller need not keep one
    /// alive. Cheap enough at this size, and it keeps every reader below to one line.</summary>
    private static JsonElement LinesFile()
    {
        var path = Path.Combine(AppDir(), "Resources", "emi", "desk-lines.json");
        Assert.True(File.Exists(path), path);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static JsonElement Moments() => LinesFile().GetProperty("moments");

    [Fact]
    public void The_book_offer_moment_is_declared()
    {
        Assert.Equal("bookOffer", EmiCodex.OfferMoment);
        Assert.True(Moments().TryGetProperty("bookOffer", out _),
            "bookOffer is not in desk-lines.json, so EmiCodex.MaybeOffer fires into the void");
    }

    /// <summary>
    /// It must actually carry an offer: the two-chip ask IS the entry point, and <c>askOdds</c> 0
    /// would leave a plain line about a book with no way to open it.
    /// </summary>
    [Fact]
    public void The_book_offer_carries_an_ask()
    {
        Assert.True(Moments().GetProperty("bookOffer").GetProperty("askOdds").GetDouble() > 0);
    }

    /// <summary>
    /// NOT <c>scripted</c>, deliberately, and <c>EmiKnockLinesFileTests.OnlyTheKnockIsScripted</c>
    /// enforces it from the other side. The bypass skips the 10 minute gap and the third-summon
    /// floor, and it exists for a beat the APP staged in answer to a chip the user just clicked.
    /// The book is not that: she volunteers it, unprompted, which is precisely what those two gates
    /// are for. Widening the bypass to a fourth moment is an owner call made by editing that
    /// allow-list, not a side effect of adding a moment.
    /// </summary>
    [Fact]
    public void The_book_offer_does_not_skip_the_cadence()
    {
        var m = Moments().GetProperty("bookOffer");
        Assert.False(m.TryGetProperty("scripted", out var s) && s.ValueKind == JsonValueKind.True);
    }

    /// <summary>Once in a lifetime, on the content side, independent of anything in code. She
    /// offers the book once; after that it is on her ring like everything else.</summary>
    [Fact]
    public void The_book_offer_is_limited_to_once_ever()
    {
        var limit = Moments().GetProperty("bookOffer").GetProperty("limit");
        Assert.Equal("ever", limit.GetProperty("per").GetString());
        Assert.Equal(1, limit.GetProperty("max").GetInt32());
    }

    /// <summary>
    /// A line whose spice is above its moment's ceiling is UNREACHABLE, not merely rare - it sits
    /// in the file looking written and can never be drawn. The ceiling itself is the writing lane's
    /// call (it is a tone dial, not a law); what is pinned here is that the two agree, in both
    /// directions, so raising the ceiling or writing a hotter line can never silently orphan one.
    /// </summary>
    [Fact]
    public void Every_book_offer_line_is_reachable_at_the_ceiling()
    {
        var ceiling = Moments().GetProperty("bookOffer").GetProperty("spiceCeiling").GetInt32();
        var root = LinesFile();

        var orphans = new List<string>();
        if (root.TryGetProperty("pools", out var pools) &&
            pools.TryGetProperty("bookOffer", out var pool))
        {
            foreach (var line in pool.EnumerateArray())
                if (Spice(line) > ceiling)
                    orphans.Add(line.GetProperty("id").GetString() ?? "?");
        }
        foreach (var ask in Asks().Where(a =>
                     a.TryGetProperty("moment", out var m) && m.GetString() == "bookOffer"))
        {
            if (Spice(ask) > ceiling) orphans.Add(ask.GetProperty("id").GetString() ?? "?");
        }

        Assert.True(orphans.Count == 0,
            "these can never be drawn at bookOffer's ceiling of " + ceiling + ": " +
            string.Join(", ", orphans));
    }

    /// <summary>
    /// THE TWO-CHIP LAW (wave 1), on this wave's asks. <c>PickAsk</c> drops any ask whose chip
    /// count is not exactly two, silently - so an ask with three chips is not a richer offer, it is
    /// an offer that never appears. Index 0 is yes, and yes is what carries the verb.
    /// </summary>
    [Fact]
    public void Every_book_ask_is_two_chips_and_opens_the_book()
    {
        var asks = Asks().Where(a =>
            a.TryGetProperty("moment", out var m) && m.GetString() == "bookOffer").ToList();

        Assert.NotEmpty(asks);
        foreach (var a in asks)
        {
            var id = a.GetProperty("id").GetString();
            Assert.Equal(2, a.GetProperty("chips").GetArrayLength());
            Assert.Equal(EmiCodex.OpenEffect, a.GetProperty("effect").GetString());
        }
    }

    private static int Spice(JsonElement e) =>
        e.TryGetProperty("spice", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;

    private static IEnumerable<JsonElement> Asks()
    {
        var root = LinesFile();
        return root.TryGetProperty("asks", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray()
            : Enumerable.Empty<JsonElement>();
    }

    /// <summary>Declared, not deferred. A deferred id that something already fires is caught by
    /// <c>EmiMomentIdWiringTests</c>; this states the intent from the other side.</summary>
    [Fact]
    public void The_book_offer_is_not_deferred()
    {
        var path = Path.Combine(AppDir(), "Resources", "emi", "desk-lines.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("deferred", out var d) || d.ValueKind != JsonValueKind.Array) return;
        foreach (var item in d.EnumerateArray())
            Assert.NotEqual("bookOffer", item.ValueKind == JsonValueKind.String ? item.GetString() : null);
    }

    // =====================================================================================
    //  source tripwires
    // =====================================================================================

    /// <summary>
    /// HER OFFER HAS A CALLER. <c>MaybeOffer</c> shipped with none, which is the quietest way for
    /// a feature to not exist: twelve written lines, three asks, a moment declared, a verb wired,
    /// and nothing on any code path that reaches them. Nothing throws and no test fails.
    /// </summary>
    [Fact]
    public void The_offer_of_the_book_is_actually_fired_from_somewhere()
    {
        var narrator = SourceRoots.ReadProductFile("Services", "EmiDesk", "EmiTourNarrator.cs");
        Assert.Contains("EmiCodex.MaybeOfferSoon(", narrator);
    }

    /// <summary>
    /// <c>bookOffer</c> MUST STAY A CEREMONY (priority >= 3), and this is not a tone preference.
    ///
    /// <para><c>EmiLineEngine.DrawCore</c> takes a moment's limit at step 4 and applies the
    /// 45-second global floor at step 6. A fire the floor swallows has therefore already spent the
    /// budget - and this moment's budget is <c>ever/1</c>, one offer for the life of an account.
    /// Priority 3 sets <c>ceremony</c>, which skips both the floor and the odds roll, so the offer
    /// cannot be eaten by whatever happened to speak in the previous three quarters of a minute.
    /// Drop this below 3 and the feature still builds, still tests green, and silently stops
    /// happening for anyone who was spoken to recently.</para>
    /// </summary>
    [Fact]
    public void The_book_offer_outranks_the_global_floor()
    {
        var priority = Moments().GetProperty("bookOffer").GetProperty("priority").GetInt32();
        Assert.True(priority >= 3,
            "bookOffer is priority " + priority + ". Below 3 the engine stops treating it as a " +
            "ceremony, the global floor can swallow the fire, and the ever/1 limit is spent anyway.");
    }

    /// <summary>
    /// The offer rides BEHIND the tour ending rather than replacing it. A suppress-and-replace
    /// would have to prove the offer landed before dropping the line it stood in for, and
    /// <c>EmiDeskService.Fire</c> returns void, so it cannot: the failure mode is a tour that ends
    /// in silence, once, for good.
    /// </summary>
    [Fact]
    public void The_tour_ending_still_speaks_for_itself()
    {
        var narrator = SourceRoots.ReadProductFile("Services", "EmiDesk", "EmiTourNarrator.cs");
        Assert.Contains("desk.Fire(\"tourFinished\")", narrator);
        Assert.Contains("desk.Fire(\"tourSkipped\")", narrator);
    }

    /// <summary>
    /// THE LAYERED-WINDOW TRAP, pinned in the source. A WebView2 child HWND does not paint inside
    /// <c>AllowsTransparency=true</c>, so a layered codex window renders nothing at all and reads
    /// as a dead panel rather than as a bug. Both bodies say it out loud.
    /// </summary>
    [Fact]
    public void Neither_body_of_the_book_is_a_layered_window()
    {
        var xaml = SourceRoots.ReadProductFile("Windows", "EmiCodexWindow.xaml");
        Assert.Contains("AllowsTransparency=\"False\"", xaml);

        var host = SourceRoots.ReadProductFile("Chaos", "ChaosWebViewHost.cs");
        Assert.Contains("AllowsTransparency = false", host);
    }

    /// <summary>
    /// The bundle is mapped Deny, like every other local page host. The book fetches only its own
    /// files from its own origin; a wider mapping would buy nothing and hand the page CORS-clean
    /// reach into the whole of Resources/web.
    /// </summary>
    [Fact]
    public void The_bundle_is_mapped_deny()
    {
        var src = SourceRoots.ReadProductFile("Services", "EmiDesk", "EmiCodex.cs");
        Assert.Contains("CoreWebView2HostResourceAccessKind.Deny", src);
        Assert.DoesNotContain("CoreWebView2HostResourceAccessKind.Allow", src);
    }

    /// <summary>
    /// No second WebView2 host (WAVE2-CONTRACT recon 2). The plumbing is <c>ChaosWebViewHost</c>'s
    /// and this lane only configures it - a private WebView2 control here would be a second copy of
    /// the mappings, the settings hardening and the navigation lockdown to keep in step.
    /// </summary>
    [Fact]
    public void The_codex_does_not_build_its_own_browser()
    {
        var src = SourceRoots.ReadProductFile("Services", "EmiDesk", "EmiCodex.cs");
        Assert.Contains("new ChaosWebViewHost(", src);
        Assert.DoesNotContain("new WebView2", src);
        Assert.DoesNotContain("EnsureCoreWebView2Async", src);
    }

    /// <summary>
    /// PROBE BEFORE BUILDING. An install with no WebView2 runtime must reach the plain reader
    /// without constructing a control or creating a browser user-data folder - the pattern
    /// <c>Controls/SpiralEmbedView</c> established and the first rung of the fail-soft ladder.
    /// </summary>
    [Fact]
    public void The_runtime_is_probed_before_anything_is_built()
    {
        var src = SourceRoots.ReadProductFile("Services", "EmiDesk", "EmiCodex.cs");
        int probe = src.IndexOf("GetAvailableBrowserVersionString", StringComparison.Ordinal);
        int build = src.IndexOf("new ChaosWebViewHost(", StringComparison.Ordinal);
        Assert.True(probe > 0, "the WebView2 runtime is never probed");
        Assert.True(build > probe, "the host is built before the runtime is probed");
    }
}
