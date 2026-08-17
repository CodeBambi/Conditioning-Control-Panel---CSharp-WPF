using System;
using System.IO;
using System.Windows.Controls;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "UI Art" panel of the mod creator: the chrome a mod can repaint that is NOT a feature
    /// icon, achievement badge or skill node — the nav rail doors, the Play wall heroes, the
    /// vault/dashboard tiles, and the session + intake cards.
    ///
    /// Nothing here needs new plumbing. Mod art is pure PATH SHADOWING: any file at
    /// <c>resources/&lt;path&gt;</c> inside a mod overrides the app's embedded
    /// <c>Resources/&lt;same path&gt;</c> the moment it resolves through
    /// <see cref="Services.ModResourceResolver"/>. The slots below are therefore just the
    /// existing generic <see cref="ModCreatorWindow.CreateImageSlot"/> mechanism pointed at
    /// paths that already shipped as art but had never been offered in the editor — export
    /// copies each filled slot verbatim to <c>resources/&lt;key&gt;</c>, and load reads the
    /// same path back.
    ///
    /// Slot keys are a compatibility surface: NEVER rename one. Several of these files feed
    /// more than one surface (see the per-slot notes) and the app deliberately keeps their
    /// historical names — <c>features/lab_*_hero.png</c> still say "lab" on doors that are no
    /// longer the Lab tab. Renaming a key silently unhooks every mod already shipping that file.
    ///
    /// Partial-class side file of <see cref="ModCreatorWindow"/> (see ModCreatorWindow.xaml.cs).
    /// </summary>
    public partial class ModCreatorWindow
    {
        // ─── Slot Definitions ────────────────────────────────────
        // Sizes in the display names are the EMBEDDED art's real pixel size, not a hard
        // requirement — the app scales everything Uniform/UniformToFill. Matching them is
        // simply how a mod's art lands in the same crop as the art it replaces.

        /// <summary>
        /// Nav rail doors. Square, transparent PNG; 64x64 is the shipped size (supply 128
        /// for crisp HiDPI). These are the only slots in this panel drawn at icon scale.
        /// </summary>
        private static readonly (string Key, string Name)[] NavDoorSlots =
        {
            ("nav/door_home.png", "Home door (64px sq)"),
            ("nav/door_play.png", "Play door (64px sq)"),
            ("nav/door_companion.png", "Companion door (64px sq)"),
            ("nav/door_library.png", "Library door (64px sq)"),
            ("nav/door_studio.png", "Studio door (64px sq)"),
            ("nav/door_you.png", "You door (64px sq)"),
            ("nav/door_webapp.png", "Web App door (64px sq)"),
            ("nav/door_settings.png", "Settings door (64px sq)"),
        };

        /// <summary>
        /// Play wall heroes and card plates. Wide 16:9 (1376x768) except goon_game.png, which
        /// ships square and is cropped to the card. Three of these are shared files — see the
        /// section description.
        /// </summary>
        private static readonly (string Key, string Name)[] PlayWallSlots =
        {
            ("features/dtrh.png", "Rabbit Hole hero (1376x768)"),
            ("features/loom.png", "Loom strip (1376x768)"),
            ("features/goon_game.png", "Goon Game hero (1024 sq)"),
            ("features/lab_gaze_hero.png", "Gaze plate (1376x768)"),
            ("features/lab_focusgaze_hero.png", "Focus Gaze plate (1376x768)"),
            ("features/lab_quiz_hero.png", "Intake plate (1376x768)"),
            ("features/lab_aimemory_hero.png", "AI Memory plate (1376x768)"),
        };

        /// <summary>
        /// Vault, dashboard tiles and the premium quick-launch rail chips. All wide 16:9
        /// (1376x768) apart from the two banner strips.
        /// </summary>
        private static readonly (string Key, string Name)[] VaultDashboardSlots =
        {
            ("features/vault.png", "Velvet Vault (1376x768)"),
            ("features/mysterybox.png", "Mystery Box (1376x768)"),
            ("features/justdrop.png", "JustDrop (1376x768)"),
            ("features/fyp.png", "FYP tile (1376x768)"),
            ("features/fyp_banner.png", "FYP banner (1376x459)"),
            ("features/awareness.png", "Awareness (1376x768)"),
            ("features/remote_control.png", "Remote Control (1376x768)"),
            ("features/blink_trainer.png", "Blink Trainer (1376x768)"),
            ("features/deeper.png", "Deeper (1376x768)"),
            ("lockdown_icon.png", "Lockdown strip (1527x343)"),
            ("exclusives/vault_backdrop.png", "Vault backdrop (wide 16:9)"),
        };

        /// <summary>
        /// Session-complete cards and the Intake weekly pass cards. Squares: the session cards
        /// ship at ~512, the pass cards at 1024.
        /// </summary>
        private static readonly (string Key, string Name)[] SessionIntakeSlots =
        {
            ("Cards/fireworks.png", "Fireworks card (512 sq)"),
            ("Cards/hearth.png", "Hearth card (512 sq)"),
            ("Cards/spotlight.png", "Spotlight card (512 sq)"),
            ("intake/pass_card.png", "Pass card, all niches (1024 sq)"),
            ("intake/pass_card_bambi.png", "Pass card: Bambi (1024 sq)"),
            ("intake/pass_card_sissy.png", "Pass card: Sissy (1024 sq)"),
            ("intake/pass_card_circe.png", "Pass card: Circe (1024 sq)"),
            ("intake/pass_card_drone.png", "Pass card: Drone (1024 sq)"),
        };

        // ─── UI Art Section ──────────────────────────────────────
        private void BuildUiArtSection()
        {
            var panel = CreateSectionPanel("uiart");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("UI Art"));
            stack.Children.Add(CreateSectionDescription("The rest of the app's artwork: the nav rail doors, the Play wall heroes, the vault and dashboard tiles, and the session and intake cards. Every slot is a straight file swap -- your image is packed as resources/<the filename under each slot> and the app loads it instead of its own copy. Leave a slot empty and the built-in art stays. The dimmed art behind each slot is what you are replacing.\n\nSlots that get cropped to fit a chip, card or banner show a \"Frame\" button once you have picked an image. It opens a mock of the real surface at its real shape: drag to move, wheel to zoom, double-click for the automatic crop. Frame it there instead of pre-cropping the PNG -- the crop is stored as numbers, so one file can be framed differently for each surface it paints and you keep your original."));

            // ── Nav doors ──
            stack.Children.Add(CreateSubHeader("Nav Doors"));
            stack.Children.Add(CreateSectionDescription("The seven room icons down the left rail. Square transparent PNGs -- 64x64 is the shipped size, 128x128 supplies a crisper HiDPI version. Anything opaque or non-square will letterbox inside the rail button."));
            stack.Children.Add(BuildUiArtSlotWrap(NavDoorSlots, 100, 100));

            // ── Play wall ──
            stack.Children.Add(CreateSubHeader("Play Wall"));
            stack.Children.Add(CreateSectionDescription("Hero and card art on the Play door. Wide 16:9 (1376x768) reads best; the cards crop to a short header plate, so use Frame to say which part of the image that plate shows. Three of these are SHARED files, one image feeding several differently-shaped frames: features/dtrh.png is also the dashboard's Rabbit Hole tile, features/lab_quiz_hero.png also paints the Graded Intake header and the quick-launch rail chip, and features/lab_aimemory_hero.png also paints the Companion door's permissions grid. Framing is per surface, so a shared file opens one preview for each and you frame them independently. The \"lab\" filenames are historical and are never renamed -- the path is the compatibility surface, not the room."));
            stack.Children.Add(BuildUiArtSlotWrap(PlayWallSlots, 150, 84));

            // ── Vault & dashboard ──
            stack.Children.Add(CreateSubHeader("Vault & Dashboard"));
            stack.Children.Add(CreateSectionDescription("Dashboard tiles, Velvet Vault faces and the premium quick-launch rail chips. Wide 16:9 (1376x768) unless the slot says otherwise. Most of these files feed both a dashboard tile and a rail chip, so one upload repaints both -- and a chip is barely wider than it is tall, so it takes a hard crop out of a 16:9 image. Frame it rather than fight it: each surface gets its own preview and its own crop from the same file."));
            stack.Children.Add(BuildUiArtSlotWrap(VaultDashboardSlots, 150, 84));

            // ── Session & intake ──
            stack.Children.Add(CreateSubHeader("Session & Intake Cards"));
            stack.Children.Add(CreateSectionDescription("The three session-complete celebration cards (one is picked at random when a session ends) and the Intake weekly pass cards. Squares. \"Pass card, all niches\" is the override that wins for every niche at once -- fill it and the four per-niche cards below are never reached; leave it empty to theme each niche separately."));
            stack.Children.Add(BuildUiArtSlotWrap(SessionIntakeSlots, 110, 110));
        }

        /// <summary>
        /// Wraps a slot group. Same body as <see cref="BuildImageSlotsSection"/>'s loop, but
        /// returns the panel so several groups can share one section, and passes a per-group
        /// size so the wide 16:9 plates are not squeezed into the square icon box.
        /// </summary>
        private WrapPanel BuildUiArtSlotWrap((string Key, string Name)[] slots, double width, double height)
        {
            var wrap = new WrapPanel();
            foreach (var (slotKey, name) in slots)
            {
                var displayName = App.Mods?.MakeModAware(name) ?? name;
                var slot = CreateImageSlot(slotKey, displayName, width, height);
                slot.ToolTip = $"Packed as resources/{slotKey}  (overrides Resources/{slotKey})";
                wrap.Children.Add(slot);
            }
            return wrap;
        }
    }

    /// <summary>
    /// The rules an image picked for a slot has to pass. Split out of
    /// <see cref="ModCreatorWindow.SetImageSlot"/> so the decisions are testable without a
    /// Dispatcher — the window only shows the message and obeys the verdict.
    ///
    /// Why an extension check at all: the slot key IS the filename inside the package, so a
    /// JPEG picked for <c>features/vault.png</c> ships as bytes that claim to be a PNG. WPF's
    /// decoder sniffs the header and copes, but everything downstream that trusts the
    /// extension (web catalogue thumbnails, the mobile client, image tooling) does not. The
    /// same trap eats an animated PNG dropped on <c>spiral.gif</c>.
    /// </summary>
    internal static class ModImageSlotRules
    {
        /// <summary>Above this, picking is allowed but the author is asked to confirm.</summary>
        internal const long WarnFileSizeBytes = 4L * 1024 * 1024;

        /// <summary>
        /// Null when the file's extension is acceptable for the slot; otherwise the reason to
        /// show the author. A slot key with no extension (the "preview" slot) accepts anything.
        /// .jpg and .jpeg are treated as one format.
        /// </summary>
        internal static string? DescribeExtensionMismatch(string slotKey, string filePath)
        {
            var wanted = NormalizeExtension(slotKey);
            if (wanted.Length == 0) return null;      // extensionless slot: no rule to break

            var got = NormalizeExtension(filePath);
            if (got.Length == 0)
                return $"\"{Path.GetFileName(filePath)}\" has no file extension. This slot needs a {wanted} file.";

            if (string.Equals(wanted, got, StringComparison.OrdinalIgnoreCase)) return null;

            return $"\"{Path.GetFileName(filePath)}\" is a {got} file, but this slot must be {wanted} -- " +
                   $"it is packed as resources/{slotKey} and replaces the app's file of that exact name.\n\n" +
                   $"Renaming does not convert the format. Re-save the image as {wanted} and pick it again.";
        }

        /// <summary>True when the file is big enough to be worth a confirmation.</summary>
        internal static bool IsOversized(long sizeBytes) => sizeBytes > WarnFileSizeBytes;

        /// <summary>"6.2 MB" -- one decimal, invariant, for the oversize prompt.</summary>
        internal static string FormatSize(long sizeBytes) =>
            (sizeBytes / 1024d / 1024d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";

        /// <summary>Lowercased extension with its dot, ".jpeg" folded to ".jpg", "" when there is none.</summary>
        private static string NormalizeExtension(string path)
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return string.Empty;
            ext = ext.ToLowerInvariant();
            return ext == ".jpeg" ? ".jpg" : ext;
        }
    }
}
