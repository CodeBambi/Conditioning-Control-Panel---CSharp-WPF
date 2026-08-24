using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CcpClient.Desktop.Ai;

namespace CcpClient.Desktop.Features.Companion;

/// <summary>
/// D11 (audit row D11, ADOPT): a read-only viewer over the PERSISTED conversation — "Everything
/// you two have said" (Localization/Languages/en.json:4477). Upstream's counterpart is
/// Views/Controls/Companion/Runtime/CompanionTranscriptWindow.cs:11-23.
///
/// <para><b>What it exposes, and why that broadens nothing.</b> Only what is already retained: the
/// pairs in <see cref="IAiMemoryStore"/>, which by construction are user/assistant CHAT turns and
/// nothing else — awareness and ambient turns are never persisted (admission §5 rule 5), and a
/// turn moderation rolled back never reached the store (c4). So this window cannot show browsing
/// commentary and cannot show a refused reply, for the same structural reason upstream's cannot
/// (`:15-19`). The audit's boundary line for this row is "none — exposes only what is already
/// retained".</para>
///
/// <para><b>The UNGATED read, deliberately.</b> It reads <see cref="IAiMemoryStore.ReadRecent"/>,
/// not <see cref="IAiMemoryStore.ReadPromptContext"/>. The consent gate on the second one is about
/// what may be fed to a model (contract §5 rule 2); this is the user inspecting their own stored
/// document, and gating it would hide from the user exactly the bytes the gate exists to protect
/// them about.</para>
///
/// <para><b>Built in code, on purpose</b> — upstream's own recorded reason (`:21-23`): a dialog
/// with one list in it does not need an AXAML file, a design-time instance or a theme entry, and
/// each of those is a place for a duplicate resource key to land. Read-only means read-only: every
/// element here is a TextBlock, there is no editor and no command.</para>
/// </summary>
public sealed class CompanionTranscriptWindow : Window
{
    /// <summary>The window title and heading (en.json:4477).</summary>
    public const string Heading = "Everything you two have said";

    /// <summary>Shown when nothing has been persisted yet (en.json:4475).</summary>
    public const string EmptyCopy = "nothing yet. the first thing you say is the first thing she keeps.";

    /// <summary>The footer note (en.json:4590).</summary>
    public const string StorageNote = "her memory lives on this machine only";

    /// <summary>Row label for a user turn (en.json:4478).</summary>
    public const string YouLabel = "you";

    /// <summary>Row label for an assistant turn (en.json:4476).</summary>
    public const string HerLabel = "her";

    // Sized so that, centred on the companion window, it covers that window's settings column —
    // which is what makes the headed `companion-transcript` pair a pair. capture.ps1 does not
    // trust these numbers: it derives its sample band from the companion window's own UIA rects
    // and then asserts the band is inside the REAL transcript rect before reading a pixel.
    private const double WindowWidth = 440;
    private const double WindowHeight = 700;

    public CompanionTranscriptWindow(IReadOnlyList<AiMemoryTurn> turns)
    {
        ArgumentNullException.ThrowIfNull(turns);

        Title = Heading;
        Width = WindowWidth;
        Height = WindowHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;
        // The companion surface's own panel colour (CompanionWindow.axaml chat-bubble ground),
        // which is what the headed pair reads against the window's #FF141018 ground beneath it.
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x18, 0x22));
        AutomationProperties.SetAutomationId(this, "TranscriptWindow");

        var body = new StackPanel { Spacing = 9 };
        if (turns.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = EmptyCopy,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x8F, 0xA3)),
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0),
                [AutomationProperties.AutomationIdProperty] = "TranscriptEmpty",
            });
        }
        else
        {
            foreach (var turn in turns)
            {
                body.Children.Add(BuildRow(turn));
            }
        }

        var root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };

        var heading = new TextBlock
        {
            Text = Heading,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x66, 0xFF)),
            [AutomationProperties.AutomationIdProperty] = "TranscriptHeading",
        };
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
            Margin = new Thickness(0, 8, 0, 8),
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var note = new TextBlock
        {
            Text = StorageNote,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x5B, 0x73)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            [AutomationProperties.AutomationIdProperty] = "TranscriptNote",
        };
        Grid.SetRow(note, 2);
        root.Children.Add(note);

        Content = root;
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse("Escape"),
            Command = new CloseCommand(Close),
        });
    }

    private static Control BuildRow(AiMemoryTurn turn)
    {
        var mine = turn.Role is AiMemoryRole.User;
        return new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = mine ? YouLabel : HerLabel,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(mine
                        ? Color.FromRgb(0x8F, 0xB4, 0xD9)
                        : Color.FromRgb(0xE0, 0x66, 0xFF)),
                },
                new TextBlock
                {
                    Text = turn.Text,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD2, 0xEA)),
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
    }

    // Escape closes, the W-04 window shape the companion window already uses. A one-line
    // ICommand rather than a KeyDown handler so the gesture and the action stay in one place.
    private sealed class CloseCommand(Action close) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => close();
    }
}
