using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The pre-descent save picker (SP-024 slice b2). Ports the WPF interaction OUTCOMES
/// (ChaosSlotPickerWindow.xaml.cs, File.cs:line cites per block): three fixed-order
/// cards; empty = "New Journey"; populated = rank / descents / ✦ sparks / 🪙 gold /
/// best / last played; slots 2/3 stitched shut until any save owns the doll craft;
/// per-card delete with confirm + re-lock warning; cancel backs out (no launch);
/// DESCEND commits. The commit is read from <see cref="ChosenSlot"/> after close.
/// </summary>
public partial class DtrhSlotPickerWindow : Window
{
    private readonly DtrhSaveSlots _slots;
    private readonly Action<string>? _log;
    private readonly Dictionary<int, Border> _cards = new();
    private int _selected;
    private int _pendingDelete;

    /// <summary>The slot the player committed to; null when cancelled (WPF: Pick returns null).</summary>
    public int? ChosenSlot { get; private set; }

    public DtrhSlotPickerWindow(DtrhSaveSlots slots, Action<string>? log = null)
    {
        _slots = slots;
        _log = log;
        InitializeComponent();
        _selected = slots.ActiveSlot;
        PathText.Text = slots.SaveFolder;
        DescendButton.Click += (_, _) =>
        {
            ChosenSlot = _selected;
            Close();
        };
        CancelButton.Click += (_, _) => Close();
        ConfirmEraseButton.Click += (_, _) => ConfirmDelete();
        ConfirmCancelButton.Click += (_, _) => ConfirmOverlay.IsVisible = false;
        OpenFolderButton.Click += (_, _) => OpenSaveFolder();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (ConfirmOverlay.IsVisible)
                {
                    ConfirmOverlay.IsVisible = false;
                }
                else
                {
                    Close(); // cancel backs out (WPF: BtnClose_Click)
                }

                e.Handled = true;
            }
            else if (e.Key == Key.Enter && !ConfirmOverlay.IsVisible)
            {
                ChosenSlot = _selected;
                Close();
                e.Handled = true;
            }
        };
        RebuildCards();
    }

    /// <summary>Programmatic commit of the current selection (same outcome as DESCEND):
    /// the no-input demo harness's timed drive and tests use this; it is not a simulated click.</summary>
    public void CommitCurrentSelection()
    {
        ChosenSlot = _selected;
        Close();
    }

    /// <summary>The currently-highlighted slot (tests + the timed-drive harness).</summary>
    public int SelectedSlot => _selected;

    /// <summary>Card count for draw-level tests.</summary>
    public int CardCount => SlotsPanel.Children.Count;

    private void RebuildCards()
    {
        SlotsPanel.Children.Clear();
        _cards.Clear();

        var summaries = _slots.Summaries();
        foreach (var s in summaries)
        {
            var locked = _slots.IsSlotLocked(s.Slot, summaries);
            var card = locked ? BuildLockedCard(s) : BuildCard(s);
            _cards[s.Slot] = card;
            SlotsPanel.Children.Add(card);
        }

        // Locked selected slot falls back to 1 (ChaosSlotPickerWindow.xaml.cs:81).
        if (_slots.IsSlotLocked(_selected, summaries))
        {
            _selected = 1;
        }

        UpdateSelectionVisuals();
    }

    private Border BuildLockedCard(DtrhSlotSummary s)
    {
        var body = new StackPanel { Margin = new Avalonia.Thickness(16, 14, 16, 16) };
        body.Children.Add(Label($"SAVE {s.Slot}"));
        body.Children.Add(new TextBlock
        {
            Text = "🔒",
            FontSize = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 34, 0, 8),
        });
        body.Children.Add(Centered("Stitched Shut", 18, FontWeight.SemiBold, Brushes.White));
        // ChaosSlotPickerWindow.xaml.cs:100-104.
        body.Children.Add(Centered(
            s.Slot == 2
                ? "craft the Ragdoll in THE BOUDOIR to open a second life"
                : "craft the Porcelain doll in THE BOUDOIR to open a third life",
            11.5, FontWeight.Normal, Brush("#88A0A0C0"), wrap: true));

        return new Border { Classes = { "slot-card", "locked" }, Tag = s.Slot, Child = body };
    }

    private Border BuildCard(DtrhSlotSummary s)
    {
        var body = new StackPanel { Margin = new Avalonia.Thickness(16, 14, 16, 16) };
        body.Children.Add(Label($"SAVE {s.Slot}"));

        if (!s.Exists)
        {
            // Empty slot (ChaosSlotPickerWindow.xaml.cs:131-152).
            body.Children.Add(new TextBlock
            {
                Text = "✨",
                FontSize = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Avalonia.Thickness(0, 34, 0, 8),
            });
            body.Children.Add(Centered("New Journey", 18, FontWeight.SemiBold, Brushes.White));
            body.Children.Add(Centered(
                s.Degraded
                    ? "previous save was unreadable — preserved aside, starting fresh"
                    : "Empty slot — start fresh",
                11.5, FontWeight.Normal, Brush("#88A0A0C0"), wrap: true));
        }
        else
        {
            // Populated card (ChaosSlotPickerWindow.xaml.cs:154-181).
            body.Children.Add(new TextBlock
            {
                Text = RankName(s.RunsCompleted),
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("#E84393"),
                Margin = new Avalonia.Thickness(0, 10, 0, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            body.Children.Add(StatRow($"{s.RunsCompleted}", s.RunsCompleted == 1 ? "descent" : "descents"));
            body.Children.Add(StatRow($"✦ {s.Sparks:N0}", "drops"));
            body.Children.Add(StatRow($"🪙 {s.Gold:N0}", "gold"));
            if (s.BestScore > 0)
            {
                body.Children.Add(StatRow($"{s.BestScore:N0}", "best score"));
            }

            if (s.LastPlayedUtc.HasValue)
            {
                body.Children.Add(new TextBlock
                {
                    Text = "Last played " + s.LastPlayedUtc.Value.ToLocalTime().ToString("MMM d, h:mm tt"),
                    FontSize = 10.5,
                    Foreground = Brush("#88A0A0C0"),
                    Margin = new Avalonia.Thickness(0, 12, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            if (s.Degraded)
            {
                body.Children.Add(Centered(
                    "save was unreadable — original preserved aside",
                    10.5, FontWeight.Normal, Brush("#E84393"), wrap: true));
            }
        }

        var content = new Panel { Children = { body } };

        // Delete only when there's something to erase (ChaosSlotPickerWindow.xaml.cs:190-208).
        if (s.Exists)
        {
            var del = new Button
            {
                Content = "🗑",
                Width = 28,
                Height = 28,
                Padding = new Avalonia.Thickness(0),
                FontSize = 12,
                Background = Brush("#22FFFFFF"),
                BorderThickness = new Avalonia.Thickness(0),
                Foreground = Brush("#AAB8B8D0"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Avalonia.Thickness(0, 10, 10, 0),
                Tag = s.Slot,
            };
            del.Click += (_, _) => AskDelete((int)del.Tag!);
            content.Children.Add(del);
        }

        var card = new Border { Classes = { "slot-card" }, Tag = s.Slot, Child = content };
        card.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                _selected = (int)card.Tag!;
                UpdateSelectionVisuals();
            }
        };
        return card;
    }

    /// <summary>The delete confirm, incl. the stitch re-lock warning
    /// (ChaosSlotPickerWindow.xaml.cs:246-273).</summary>
    private void AskDelete(int slot)
    {
        _pendingDelete = slot;
        var msg = $"Erase Save {slot}?\n\nThis permanently deletes this local save. It can't be undone.";
        if (slot is 2 or 3)
        {
            var dollElsewhere = false;
            foreach (var s in _slots.Summaries())
            {
                if (s.Slot != slot)
                {
                    dollElsewhere |= slot == 2 ? s.HasRagdoll : s.HasPorcelain;
                }
            }

            if (!dollElsewhere)
            {
                msg += slot == 2
                    ? "\n\nWithout this save, Save 2 is Stitched Shut again until the Ragdoll is crafted in THE BOUDOIR."
                    : "\n\nWithout this save, Save 3 is Stitched Shut again until the Porcelain doll is crafted in THE BOUDOIR.";
            }
        }

        ConfirmText.Text = msg;
        ConfirmOverlay.IsVisible = true;
    }

    private void ConfirmDelete()
    {
        ConfirmOverlay.IsVisible = false;
        _slots.DeleteSlot(_pendingDelete);
        _log?.Invoke($"dtrh-picker: deleted save slot {_pendingDelete}");
        RebuildCards();
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var (slot, card) in _cards)
        {
            var selected = slot == _selected && !card.Classes.Contains("locked");
            if (selected)
            {
                card.Classes.Add("sel");
            }
            else
            {
                card.Classes.Remove("sel");
            }
        }
    }

    private void OpenSaveFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_slots.SaveFolder);
            // Windows: shell open; Linux: xdg-open (best-effort — a convenience, never a gate).
            var fileName = OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open";
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = fileName, Arguments = $"\"{_slots.SaveFolder}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log?.Invoke($"dtrh-picker: open folder failed: {ex.Message}");
        }
    }

    /// <summary>The rank spine (ChaosRanks.cs:11-55): thresholds {0,3,10,25,50,100}.</summary>
    public static string RankName(int runsCompleted)
    {
        string[] names = ["Curious", "Tempted", "Slipping", "Entranced", "Devoted", "Claimed"];
        int[] thresholds = [0, 3, 10, 25, 50, 100];
        var rank = 0;
        for (var i = thresholds.Length - 1; i >= 0; i--)
        {
            if (runsCompleted >= thresholds[i])
            {
                rank = i;
                break;
            }
        }

        return names[rank];
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.Bold,
        Foreground = Brush("#88A0A0C0"),
    };

    private static TextBlock Centered(string text, double size, FontWeight weight, IBrush foreground, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = foreground,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    private static StackPanel StatRow(string value, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Avalonia.Thickness(0, 4, 0, 0) };
        row.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White });
        row.Children.Add(new TextBlock { Text = "  " + label, FontSize = 12, Foreground = Brush("#AAB8B8D0"), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
