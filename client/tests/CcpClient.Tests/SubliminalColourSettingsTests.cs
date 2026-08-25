using System.Text.Json;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The subliminal card's three COLOUR settings, end to end: persisted document → the module's card
/// → the pixels the rasteriser actually writes. And the fourth setting beside them, which this build
/// refuses by name rather than obeying.
///
/// <para><b>What was wrong before.</b> The card's background, text and outline were compile-time
/// constants. Each cited upstream's DEFAULT correctly, so the shipped card looked right — and every
/// one of them is a USER SETTING upstream (<c>SubBackgroundColor</c>, <c>SubTextColor</c>,
/// <c>SubBorderColor</c>, edited in <c>Dialogs/ColorEditorDialog.xaml.cs</c> and re-read on every
/// show at <c>Services/Subliminal/SubliminalService.cs:622-624</c>). The port reproduced upstream's
/// defaults faithfully and its configurability not at all.</para>
///
/// <para><b>Nothing here opens a window, waits, or touches a screen.</b> The rasteriser is driven
/// directly and the module runs on a clock the test fires by hand.</para>
/// </summary>
public class SubliminalColourSettingsTests
{
    /// <summary>Three colours no default is, no two of which share a channel value, so a swapped
    /// pair is as visible as a dropped one.</summary>
    private const string UserBackground = "#102030";

    private const string UserText = "#40C060";

    private const string UserOutline = "#F0A000";

    // ---------------------------------------------------------------------------------
    //  the defaults are still upstream's
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Somebody who never opened the colour editor must see exactly the card they saw before: the
    /// three shipped values, compared against upstream's own field initializers rather than against
    /// numbers retyped here.
    /// </summary>
    [Fact]
    public void AUserWhoNeverSetAColour_StillGetsUpstreamsOwnThree()
    {
        var document = new SubliminalPresetDocument();

        Assert.Equal(UpstreamAppSettings.String("_subBackgroundColor"), document.BackgroundColour);
        Assert.Equal(UpstreamAppSettings.String("_subTextColor"), document.TextColour);
        Assert.Equal(UpstreamAppSettings.String("_subBorderColor"), document.OutlineColour);
        Assert.Equal(UpstreamAppSettings.Bool("_subBackgroundTransparent"), document.BackgroundTransparent);
    }

    /// <summary>The ARGB constants the rasteriser falls back to are the same three colours the
    /// document persists as hex — one representation cannot drift from the other.</summary>
    [Fact]
    public void ThePalettesDefaults_AreTheDocumentsDefaults_InTheOtherRepresentation()
    {
        var document = new SubliminalPresetDocument();
        var fromDocument = SubliminalPalette.From(
            document.BackgroundColour, document.TextColour, document.OutlineColour);

        Assert.Equal(SubliminalPalette.Default, fromDocument);
    }

    // ---------------------------------------------------------------------------------
    //  a user's colours reach the pixels
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// <b>The whole point.</b> Three colours are written into the module's persisted document, the
    /// module is armed and its schedule fired, and the card it hands the surface carries those
    /// colours — none of the shipped three survives anywhere in it.
    /// </summary>
    [Fact]
    public void ColoursSetOnTheDocument_ReachTheCardTheModuleShows()
    {
        using var lab = new Lab();
        lab.Preset.Mutate(p =>
        {
            p.Enabled = true;
            p.BackgroundColour = UserBackground;
            p.TextColour = UserText;
            p.OutlineColour = UserOutline;
        });

        lab.Effect.Arm();
        lab.Clock.Fire();

        var card = Assert.Single(lab.Surface.Cards);
        Assert.Equal(0xFF102030, card.Palette.BackgroundArgb);
        Assert.Equal(0xFF40C060, card.Palette.TextArgb);
        Assert.Equal(0xFFF0A000, card.Palette.OutlineArgb);
        Assert.NotEqual(SubliminalPalette.Default, card.Palette);
    }

    /// <summary>
    /// A colour changed BETWEEN two cards lands on the next one, because the palette is resolved per
    /// show — upstream parses all three inside <c>ShowSubliminalVisuals</c>
    /// (<c>SubliminalService.cs:622-624</c>), so a user who edits the colour mid-session sees it on
    /// the next subliminal rather than at the next start.
    /// </summary>
    [Fact]
    public void AColourChangedMidSession_LandsOnTheVeryNextCard()
    {
        using var lab = new Lab();
        lab.Preset.Mutate(p => p.Enabled = true);

        lab.Effect.Arm();
        lab.Clock.Fire();
        lab.Preset.Mutate(p => p.TextColour = UserText);
        lab.Clock.Fire();

        Assert.Equal(2, lab.Surface.Cards.Count);
        Assert.Equal(SubliminalPalette.DefaultTextArgb, lab.Surface.Cards[0].Palette.TextArgb);
        Assert.Equal(0xFF40C060, lab.Surface.Cards[1].Palette.TextArgb);
    }

    /// <summary>
    /// And the rasteriser really paints what the palette says, on real pixels: the card's corners
    /// are the user's background, both of the user's other two colours are present, and none of
    /// upstream's shipped three appears anywhere on the card.
    /// </summary>
    [Fact]
    public void TheRasteriserPaintsThePaletteItIsGiven_AndNoneOfTheShippedColoursSurvive()
    {
        var measured = ColouredCard.Run;

        Assert.Equal(GdiPlusRuntime.Available, measured.Rendered);
        Assert.Equal(GdiPlusRuntime.Available, measured.CornersAreTheUsersBackground);
        Assert.Equal(GdiPlusRuntime.Available, measured.CarriesTheUsersText);
        Assert.Equal(GdiPlusRuntime.Available, measured.CarriesTheUsersOutline);

        // Not one pixel of black, magenta or white: the constants are gone, not merely overridden
        // in one of the three places.
        Assert.Equal(0, measured.ShippedBackgroundPixels);
        Assert.Equal(0, measured.ShippedTextPixels);
        Assert.Equal(0, measured.ShippedOutlinePixels);
    }

    // ---------------------------------------------------------------------------------
    //  what an unusable value does
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// A hand-edited value this parser cannot read falls back to that setting's OWN default, which
    /// is the shape of upstream's three calls — each passes its own fallback colour as
    /// <c>ParseColor</c>'s second argument (<c>SubliminalService.cs:622-624</c>). The failure is
    /// per-setting: a broken background must not drag the text colour down with it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a colour")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void AnUnreadableBackground_FallsBackAlone(string? broken)
    {
        var palette = SubliminalPalette.From(broken, UserText, UserOutline);

        Assert.Equal(SubliminalPalette.DefaultBackgroundArgb, palette.BackgroundArgb);
        Assert.Equal(0xFF40C060, palette.TextArgb);
        Assert.Equal(0xFFF0A000, palette.OutlineArgb);
    }

    /// <summary>The document itself never holds null — WPF's own <c>value ?? "#000000"</c>
    /// (<c>CCP.Core/Models/AppSettings.cs:1356</c>) and the two beside it.</summary>
    [Fact]
    public void ANullColourWrittenToTheDocument_BecomesItsDefaultRatherThanNull()
    {
        var document = new SubliminalPresetDocument
        {
            BackgroundColour = null!,
            TextColour = null!,
            OutlineColour = null!,
        };

        Assert.Equal(SubliminalPresetDocument.DefaultBackgroundColour, document.BackgroundColour);
        Assert.Equal(SubliminalPresetDocument.DefaultTextColour, document.TextColour);
        Assert.Equal(SubliminalPresetDocument.DefaultOutlineColour, document.OutlineColour);
    }

    /// <summary>
    /// All four settings are really PERSISTED — they survive the document's own serialization round
    /// trip. Without this, a member that was never written to the file would look identical to every
    /// fact above and the user's choice would last exactly one launch.
    /// </summary>
    [Fact]
    public void AllFourSettings_SurviveTheDocumentsSerializationRoundTrip()
    {
        var written = new SubliminalPresetDocument
        {
            BackgroundColour = UserBackground,
            TextColour = UserText,
            OutlineColour = UserOutline,
            BackgroundTransparent = true,
        };

        var json = JsonSerializer.Serialize(written);
        var read = JsonSerializer.Deserialize<SubliminalPresetDocument>(json);

        Assert.NotNull(read);
        Assert.Equal(UserBackground, read.BackgroundColour);
        Assert.Equal(UserText, read.TextColour);
        Assert.Equal(UserOutline, read.OutlineColour);
        Assert.True(read.BackgroundTransparent);
    }

    // ---------------------------------------------------------------------------------
    //  the fourth setting: refused, not obeyed and not ignored
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// <b>The transparency refusal.</b> This surface composites one layered window at a single
    /// uniform alpha over a frame with no per-pixel alpha, so "no background" is not expressible.
    /// The module says so in type when the user has turned the dial on, and stays
    /// <c>Available</c> when they have not — a silent no-op on a setting somebody toggled is the
    /// dead dial the port refuses everywhere else.
    /// </summary>
    [Fact]
    public void TransparentBackgroundIsRefusedByName_AndOnlyWhenTheUserAskedForIt()
    {
        using var opaque = new Lab();
        opaque.Preset.Mutate(p => p.Enabled = true);
        Assert.IsType<CapabilityState.Available>(opaque.Effect.Arm());

        using var transparent = new Lab();
        transparent.Preset.Mutate(p =>
        {
            p.Enabled = true;
            p.BackgroundTransparent = true;
        });

        var degraded = Assert.IsType<CapabilityState.Degraded>(transparent.Effect.Arm());
        Assert.Equal(EffectReasonCodes.SubliminalOpaqueBackgroundOnly, degraded.Reason.Code);
    }

    /// <summary>
    /// The refusal never displaces the stronger one. A module with no active phrase shows nothing at
    /// all, which makes the background colour moot; that answer wins, and the transparency code is
    /// only reachable on a module that will really put a card on screen.
    /// </summary>
    [Fact]
    public void AnEmptyPoolStillWins_BecauseNothingAppearsAtAll()
    {
        using var lab = new Lab();
        lab.Preset.Mutate(p =>
        {
            p.Enabled = true;
            p.BackgroundTransparent = true;
            p.Phrases = new Dictionary<string, bool>(StringComparer.Ordinal) { ["EVERY PHRASE UNCHECKED"] = false };
        });

        var degraded = Assert.IsType<CapabilityState.Degraded>(lab.Effect.Arm());
        Assert.Equal(EffectReasonCodes.SubliminalNoActivePhrase, degraded.Reason.Code);
    }

    /// <summary>
    /// And the refusal is honest about which half holds: the card is still shown, still in the
    /// user's chosen background colour, and still opaque. Degraded means "this dial did not
    /// happen", never "the module did not run".
    /// </summary>
    [Fact]
    public void ARefusedTransparencyStillShowsTheCard_InTheChosenBackgroundColour()
    {
        using var lab = new Lab();
        lab.Preset.Mutate(p =>
        {
            p.Enabled = true;
            p.BackgroundTransparent = true;
            p.BackgroundColour = UserBackground;
        });

        lab.Effect.Arm();
        lab.Clock.Fire();

        var card = Assert.Single(lab.Surface.Cards);
        Assert.Equal(0xFF102030, card.Palette.BackgroundArgb);
        Assert.Equal(1, lab.Effect.SubliminalCount);
    }

    // ---------------------------------------------------------------------------------
    //  the rig
    // ---------------------------------------------------------------------------------

    /// <summary>The Subliminals module on a clock the test fires by hand, with a surface that
    /// records the cards instead of drawing them. No window, no wall clock.</summary>
    private sealed class Lab : IDisposable
    {
        private readonly string _directory;
        private readonly OperationRegistry _registry = new();

        public Lab()
        {
            _directory = Path.Combine(Path.GetTempPath(), "ccp-sub-colour-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Preset = OpenStore();

            // A BOUND boundary that runs the projection inline: unbound is skip-until-bound
            // (async-lifecycle-fault-contract §5.3), and the card would never reach the surface at
            // all — which is the state that would make every colour fact below pass vacuously.
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());

            Effect = new SubliminalsEffect(
                _registry.OwnerFor("ColourSubliminals"),
                new EffectSignal(boundary, static () => true),
                Clock,
                new SubliminalPhrasePool(Preset, new Random(11)),
                Preset,
                new Random(11),
                Surface);
        }

        public HandClock Clock { get; } = new();

        public PersistenceStore<SubliminalPresetDocument> Preset { get; }

        public RecordingSurface Surface { get; } = new();

        public SubliminalsEffect Effect { get; }

        public void Dispose()
        {
            Effect.Disarm();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private PersistenceStore<SubliminalPresetDocument> OpenStore() =>
            new(_registry.OwnerFor("ColourSubliminalPreset-" + Guid.NewGuid().ToString("N")),
                new SilentSink(),
                Path.Combine(_directory, SubliminalPresetDocument.FileName),
                SubliminalPresetDocument.CurrentSchemaVersion);
    }

    /// <summary>Holds the one pending callback and fires it on demand. Zero wall-clock.</summary>
    private sealed class HandClock : ISessionClock
    {
        private Action? _pending;

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            _pending = fire;
            return new Handle(this, fire);
        }

        /// <summary>Run whatever is scheduled, as the real timer would when it came due.</summary>
        public void Fire()
        {
            var due = _pending;
            _pending = null;
            UtcNow = UtcNow.AddSeconds(12);
            due?.Invoke();
        }

        private sealed class Handle(HandClock clock, Action fire) : IDisposable
        {
            public void Dispose()
            {
                if (ReferenceEquals(clock._pending, fire))
                {
                    clock._pending = null;
                }
            }
        }
    }

    /// <summary>Keeps every card it is handed, in order, and draws nothing.</summary>
    private sealed class RecordingSurface : ISubliminalSurface
    {
        public List<SubliminalCard> Cards { get; } = [];

        public CapabilityState? LastPlacement => null;

        public void Show(SubliminalCard card) => Cards.Add(card);

        public void HideAll()
        {
        }
    }

    /// <summary>Runs a posted projection on the calling thread. No Avalonia runtime anywhere.</summary>
    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }

    private sealed class SilentSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }
}

/// <summary>
/// One card rasterised in a palette that shares no colour with the shipped one, measured once.
/// Hoisted out of the facts for <see cref="SubliminalCardObservations"/>'s reason: the rasteriser is
/// Windows-only, so every fact compares a MACHINE property against a PRODUCT property and no fact
/// carries a platform branch.
/// </summary>
public sealed record ColouredCard(
    bool Rendered,
    bool CornersAreTheUsersBackground,
    bool CarriesTheUsersText,
    bool CarriesTheUsersOutline,
    int ShippedBackgroundPixels,
    int ShippedTextPixels,
    int ShippedOutlinePixels)
{
    private static readonly Lazy<ColouredCard> Lazy = new(Measure, LazyThreadSafetyMode.ExecutionAndPublication);

    public static ColouredCard Run => Lazy.Value;

    private static ColouredCard Measure()
    {
        var palette = SubliminalPalette.From("#102030", "#40C060", "#F0A000");
        var frame = new GdiPlusSubliminalFrameSource().Render("GOOD GIRL", 640, 360, palette);
        if (frame is null)
        {
            return new ColouredCard(false, false, false, false, 0, 0, 0);
        }

        return new ColouredCard(
            Rendered: true,
            CornersAreTheUsersBackground: Corners(frame, 0x00302010),
            CarriesTheUsersText: Count(frame, 0x0060C040) > 0,
            CarriesTheUsersOutline: Count(frame, 0x0000A0F0) > 0,
            ShippedBackgroundPixels: Count(frame, SubliminalPalette.DefaultBackgroundArgb & 0x00FFFFFF),
            ShippedTextPixels: Count(frame, SubliminalPalette.DefaultTextArgb & 0x00FFFFFF),
            ShippedOutlinePixels: Count(frame, SubliminalPalette.DefaultOutlineArgb & 0x00FFFFFF));
    }

    private static bool Corners(OverlayFrame frame, uint colourRef) =>
        frame.ColourAt(0, 0) == colourRef
        && frame.ColourAt(frame.Width - 1, 0) == colourRef
        && frame.ColourAt(0, frame.Height - 1) == colourRef
        && frame.ColourAt(frame.Width - 1, frame.Height - 1) == colourRef;

    private static int Count(OverlayFrame frame, uint colourRef)
    {
        var count = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (frame.ColourAt(x, y) == colourRef)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
