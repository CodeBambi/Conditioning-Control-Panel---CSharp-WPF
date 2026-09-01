using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// WAVE 3'S TWO PROMISES, pinned as source tripwires.
///
/// <para><b>One:</b> left click pats her, right click opens her cards. The owner's report was "emi
/// is not reacting to the pats (when u click it)" - wave 2 had put the pat on her head only and
/// left the other 70% of her silhouette opening the ring. Neither gesture can be exercised in a
/// headless test (a layered, click-through-free tool window, a global mouse hook and a sibling
/// window), so what is checked is that the routing is still written the way it was decided.</para>
///
/// <para><b>Two:</b> the settings picker and the ring share ONE pin store. The picker is a second
/// front end onto <c>EmiState.Pins</c>, and the failure it must never have is the obvious one: its
/// own list, drifting out of step with the fan, so a target checked in settings is not on her ring
/// and unpinning from the ring leaves the box ticked. Every write goes through
/// <see cref="ConditioningControlPanel.Services.EmiDesk.EmiSuggester"/>, which is where the max-six
/// rule and the ordering live.</para>
///
/// <para>Source text rather than behaviour for the pin half too, on the house rule the ring tests
/// already state: <c>EmiState</c> is a disk-backed singleton pointed at the real user's
/// <c>%LOCALAPPDATA%</c>, and a test that pinned something would edit the machine owner's ring.</para>
/// </summary>
public class EmiGestureAndPinWiringTests
{
    private static string Read(params string[] parts) => SourceRoots.ReadProductFile(parts);

    // ---------------------------------------------------------------- the gestures

    [Fact]
    public void The_left_click_pats_her_and_does_not_open_the_ring()
    {
        var body = Read("Windows", "EmiDesk", "EmiDeskWindow.xaml.cs");

        // The pat is what a completed left click ends in...
        Assert.Contains("PetFromClick();", body);

        // ...and the head-only gate that caused the report is gone for clicks.
        Assert.DoesNotContain("TryHeadPet", body);

        // The ring is no longer reachable from the left-button path: the ONLY caller of the
        // body-click seam is the shared gesture road, which the right click and the glyph use.
        Assert.Contains("private void ToggleRingFromGesture()", body);
        Assert.Equal(1, CountOf(body, "OnBodyClickedCore(ref handled)"));
    }

    [Fact]
    public void The_right_click_and_her_options_panel_both_open_the_ring()
    {
        var body = Read("Windows", "EmiDesk", "EmiDeskWindow.xaml.cs");

        Assert.Contains("BodyRoot.MouseRightButtonUp += OnBodyRightClick;", body);

        // The six-dot glyph became a gear (owner, 2026-08-30): it opens her options, and the ring
        // is the panel's first action rather than this chip's job.
        Assert.Contains("BtnGear.Click += OnGearClick;", body);
        Assert.DoesNotContain("BtnCards", body);

        // Both roads still end in the ONE road, so they cannot drift apart.
        Assert.Contains("ToggleRingFromGesture();", body);
        Assert.Contains("CardsRequested += (_, _) => ToggleRingFromGesture();", body);
    }

    [Fact]
    public void The_gear_exists_and_fades_with_the_x()
    {
        var xaml = Read("Windows", "EmiDesk", "EmiDeskWindow.xaml");
        Assert.Contains("x:Name=\"BtnGear\"", xaml);
        Assert.DoesNotContain("BtnCards", xaml);

        // Hover chrome: invisible at rest, faded in with the close button by FadeChrome.
        var body = Read("Windows", "EmiDesk", "EmiDeskWindow.xaml.cs");
        int fade = body.IndexOf("private void FadeChrome(", StringComparison.Ordinal);
        Assert.True(fade > 0, "FadeChrome is gone; the hover chrome has been rewritten");
        int end = body.IndexOf("private void OnBodyMouseDown(", fade, StringComparison.Ordinal);
        Assert.True(end > fade, "FadeChrome moved; this region check needs a new end marker");
        var region = body.Substring(fade, end - fade);
        Assert.Contains("BtnClose.BeginAnimation(OpacityProperty", region);
        Assert.Contains("BtnGear.BeginAnimation(OpacityProperty", region);

        // And the enlarged hit areas are only live WHILE lit: at rest her whole silhouette pats
        // her, corners included, which is the gesture wave 3 exists to protect.
        Assert.Contains("BtnClose.IsHitTestVisible = lit;", region);
        Assert.Contains("BtnGear.IsHitTestVisible = lit;", region);
    }

    [Fact]
    public void A_click_always_reacts_even_inside_the_pet_cooldown()
    {
        var body = Read("Windows", "EmiDesk", "EmiDeskWindow.xaml.cs");
        var react = Read("Windows", "EmiDesk", "EmiDeskWindow.React.cs");

        // The squash is played BEFORE anything decides what the click meant, so there is no
        // outcome - a cooldown, a chain in flight, a locked input - that swallows a click in
        // silence. That silence was the other half of "she is not reacting".
        int squash = body.IndexOf("PlayClickSquash();", StringComparison.Ordinal);
        int route = body.IndexOf("PetFromClick();", StringComparison.Ordinal);
        Assert.True(squash > 0 && route > squash,
            "the click squash must still run before the click is routed");

        // And inside the cooldown she flicks rather than going silent. Since ALIVE wave A the
        // flick is DRESSED by the poke ladder - the plain wink, the annoyed look, or the glare on
        // the third poke - so the click path asks PlayPokeFlick which one to wear. The guarantee
        // is unchanged and is now asserted at both ends: the route here, the default there.
        Assert.Contains("PetFlickChain", react);
        Assert.Contains("PlayPokeFlick(poke);", react);

        var alive = Read("Windows", "EmiDesk", "EmiDeskWindow.Alive.cs");
        int flick = alive.IndexOf("private void PlayPokeFlick(", StringComparison.Ordinal);
        Assert.True(flick > 0, "the poke ladder's flick chooser is gone; a cooled-down click is silent again");
        Assert.Contains("PlayChain(PetFlickChain);", alive.Substring(flick));
    }

    [Fact]
    public void No_user_facing_string_says_the_forbidden_word()
    {
        // EMI's absolute fence: "door" is an Arcademy story spoiler and VOICE.md hard-errors on it.
        // The hover glyph is her CARDS everywhere a user can read it.
        foreach (var lang in new[] { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json"));
            foreach (var line in json.Split('\n'))
            {
                if (!line.Contains("emi_desk", StringComparison.Ordinal)) continue;
                Assert.DoesNotContain("door", line, StringComparison.OrdinalIgnoreCase);
            }
        }

        // The ring's only VISIBLE affordance moved into her options panel when the dots became a
        // gear, so that is where the un-localised fallback wording lives now.
        var panel = Read("Windows", "EmiDesk", "EmiOptionsWindow.xaml");
        Assert.Contains("Content=\"Open her cards\"", panel);
        Assert.DoesNotContain("door", panel, StringComparison.OrdinalIgnoreCase);

        var xaml = Read("Windows", "EmiDesk", "EmiDeskWindow.xaml");
        int tip = xaml.IndexOf("ToolTip=\"Her options\"", StringComparison.Ordinal);
        Assert.True(tip > 0, "the gear lost its fallback tooltip");
    }

    // ---------------------------------------------------------------- one pin store

    [Fact]
    public void The_shared_picker_writes_through_the_suggester_and_keeps_no_list_of_its_own()
    {
        var sec = Read("Views", "Controls", "EmiRingPicker.xaml.cs");

        Assert.Contains("EmiSuggester.IsPinned(", sec);
        Assert.Contains("EmiSuggester.TogglePin(", sec);
        Assert.Contains("EmiSuggester.ClearPins()", sec);

        // A second store is the whole failure mode. The picker may READ the count, never write it.
        Assert.DoesNotContain("EmiState.Current.Pins.Add", sec);
        Assert.DoesNotContain("EmiState.Current.Pins.Remove", sec);
        Assert.DoesNotContain("EmiState.Current.Pins.Clear", sec);
        Assert.DoesNotContain("new List<string>", sec);
    }

    /// <summary>
    /// The ring is not a pin front end any more (owner, 2026-08-30: "the Pin button is not usable
    /// right now, I propose we remove it from there") - pinning moved into her options menu. What
    /// this now pins down is the shape of that removal: the card carries NO pin gesture and no pin
    /// badge, it still never writes the store itself, and a pinned card is still legible on the fan
    /// because the thicker solid frame stayed behind.
    /// </summary>
    [Fact]
    public void The_ring_shows_a_pin_but_no_longer_makes_one()
    {
        var ring = Read("Windows", "EmiDesk", "EmiRingWindow.xaml.cs");

        // The gesture and its glyph are gone.
        Assert.DoesNotContain("OnCardPinToggled", ring);
        Assert.DoesNotContain("card.MouseRightButtonUp", ring);
        Assert.DoesNotContain("PinGlyph", ring);
        Assert.DoesNotContain("\\U0001F4CC", ring);

        // The APPEARANCE stayed: a pin made in the menu still reads across the room.
        Assert.Contains("CardBorderPinned", ring);
        Assert.Contains("slot.Pinned ? CardBorderPinned : CardBorder", ring);

        // And the store is still nobody's business but the suggester's.
        Assert.DoesNotContain("EmiState.Current.Pins.Add", ring);
        Assert.DoesNotContain("EmiState.Current.Pins.Remove", ring);
    }

    /// <summary>
    /// The event and its bookkeeping outlived the gesture on purpose. <c>pinAdded</c> and the
    /// pin-nudge latch belong to "a pin was made", wherever it was made, so the options menu has a
    /// door-free way in that lands in exactly the same place the ring's own road did.
    /// </summary>
    [Fact]
    public void The_pin_bookkeeping_survived_the_gesture_moving_out()
    {
        var ring = Read("Windows", "EmiDesk", "EmiRingWindow.xaml.cs");
        var glue = Read("Windows", "EmiDesk", "EmiDeskWindow.Ring.cs");

        // Declared, subscribed, and reachable from outside the fan.
        Assert.Contains("PinToggled;", ring);
        Assert.Contains("_ring.PinToggled += OnRingPinToggled;", glue);
        Assert.Contains("public void NotePinMadeElsewhere(string targetId, bool pinned)", glue);

        // Both roads end in the one place, so they cannot drift apart.
        Assert.Contains("EmiState.NotePinMade();", glue);
        Assert.Contains("Fire(\"pinAdded\"", glue);
        Assert.Equal(1, CountOf(glue, "private static void NotePin("));
    }

    [Fact]
    public void The_picker_obeys_the_same_six_and_refreshes_a_live_fan()
    {
        var sec = Read("Views", "Controls", "EmiRingPicker.xaml.cs");

        // Six comes from the suggester, not from a number typed into the picker.
        Assert.Contains("EmiSuggester.MaxPins", sec);
        Assert.DoesNotContain(">= 6", sec);

        // The tile is put back to whatever the STORE ended up saying, so a refused seventh pin
        // cannot leave a ticked box over an unpinned target.
        Assert.Contains("bool nowPinned = EmiSuggester.TogglePin(id);", sec);
        Assert.Contains("tb.IsChecked = nowPinned;", sec);

        // A fan that is open under the pointer shows the change now, not on the next open.
        Assert.Contains("App.EmiDesk?.RefreshRing()", sec);
    }

    [Fact]
    public void The_picker_shows_locked_targets_and_hides_unavailable_ones()
    {
        var sec = Read("Views", "Controls", "EmiRingPicker.xaml.cs");

        // Same rule as the ring: unavailable is not part of this build or account (skip it),
        // locked exists and the tier gate says no (show it, disabled, with the reason).
        Assert.Contains("if (!t.Available) continue;", sec);
        Assert.Contains("IsEnabled = !locked", sec);
        Assert.Contains("emi_desk_ring_tile_locked", sec);

        // A disabled control eats its own tooltip unless told otherwise, and the reason IS the
        // point of drawing the tile at all.
        Assert.Contains("ToolTipService.SetShowOnDisabled(tile, true);", sec);
    }

    // ---------------------------------------------------------------- the nudges are wired

    [Fact]
    public void The_gesture_counters_are_booked_where_the_gestures_happen()
    {
        Assert.Contains("EmiState.NotePet()", Read("Windows", "EmiDesk", "EmiDeskWindow.React.cs"));

        var ring = Read("Windows", "EmiDesk", "EmiDeskWindow.Ring.cs");
        Assert.Contains("App.EmiDesk?.NoteRingOpened()", ring);
        Assert.Contains("EmiState.NotePinMade()", ring);
    }

    [Fact]
    public void The_nudge_fallback_lines_never_say_the_forbidden_word()
    {
        var svc = Read("Services", "EmiDesk", "EmiDeskService.cs");

        foreach (var name in new[] { "PetNudgeFallbackText", "RingNudgeFallbackText", "PinNudgeFallbackText" })
        {
            int i = svc.IndexOf("const string " + name, StringComparison.Ordinal);
            Assert.True(i > 0, name + " is gone; the nudges have no line when the pools are missing");

            int end = svc.IndexOf(';', i);
            var decl = svc.Substring(i, end - i);
            Assert.DoesNotContain("door", decl, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_settings_tab_and_her_options_panel_host_the_same_picker()
    {
        // ONE pin wall, two hosts. The gear panel needed the identical 25 tiles, and a second copy
        // of "build the tiles, respect the six, put the tile back to what the store said" is
        // exactly how two front ends onto one store drift apart.
        var sec = Read("Views", "Controls", "AppSettings", "EmiDeskSettingsSection.xaml");
        var panel = Read("Windows", "EmiDesk", "EmiOptionsWindow.xaml");

        Assert.Contains("EmiRingPicker", sec);
        Assert.Contains("EmiRingPicker", panel);

        // And the wall itself is gone from the settings section's own code.
        var secCs = Read("Views", "Controls", "AppSettings", "EmiDeskSettingsSection.xaml.cs");
        Assert.DoesNotContain("EmiSuggester.TogglePin(", secCs);
    }

    [Fact]
    public void Her_options_panel_follows_the_widget_window_recipe()
    {
        var xaml = Read("Windows", "EmiDesk", "EmiOptionsWindow.xaml");

        // She is a desktop ornament, not an application window: no focus theft, nothing in
        // Alt-Tab. Every one of these is load-bearing and all three of her windows carry them.
        Assert.Contains("WindowStyle=\"None\"", xaml);
        Assert.Contains("AllowsTransparency=\"True\"", xaml);
        Assert.Contains("ShowActivated=\"False\"", xaml);
        Assert.Contains("ShowInTaskbar=\"False\"", xaml);
        Assert.Contains("Topmost=\"True\"", xaml);

        var cs = Read("Windows", "EmiDesk", "EmiOptionsWindow.xaml.cs");
        Assert.Contains("WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE", cs);

        // Non-modal on purpose: anything modal on a summon path needs the _summonGen guard, and
        // the live-QA bug that rule came from stranded her on screen with IsOut false.
        Assert.DoesNotContain("ShowDialog()", cs);

        // Click-away closes it, and the hook NEVER swallows the click.
        Assert.Contains("GlobalMouseHook", cs);
        Assert.Contains("return false;", cs);

        // Physical pixels over this window's OWN dpi, both sides of the sum. Assuming 1.0 is the
        // coordinate trap that ate the gaze work.
        Assert.Contains("BodyScreenRect", cs);
        Assert.Contains("DipScale", cs);
        Assert.DoesNotContain("BodyScreenRect.Left;", cs);
    }

    private static int CountOf(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
