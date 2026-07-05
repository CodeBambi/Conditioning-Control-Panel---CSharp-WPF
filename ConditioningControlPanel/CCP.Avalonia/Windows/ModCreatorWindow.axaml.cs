using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Avalonia.Services.Tutorial;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Help;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using Newtonsoft.Json;
using CoreApp = global::ConditioningControlPanel.CoreApp;
using IModService = ConditioningControlPanel.IModService;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the .ccpmod creator window.
///
/// All formerly WPF-only surfaces are now wired to cross-platform equivalents:
/// color picker (ColorPickerDialog), audio preview (IAudioPlayer), tutorial
/// overlay (TutorialOverlay), the modding help video (HelpContentService +
/// HelpVideoWindow) and hint-image resolution (AvaloniaModResourceResolver).
/// </summary>
public partial class ModCreatorWindow : Window
{
    private readonly ILogger<ModCreatorWindow> _logger;


    // ─── State ───────────────────────────────────────────────
    private readonly Dictionary<string, string?> _imageSlots = new();
    private readonly Dictionary<string, Image> _imageControls = new();
    private readonly Dictionary<string, string> _imageNames = new();
    private readonly Dictionary<string, TextBox> _imageNameBoxes = new();
    private readonly Dictionary<string, List<string>> _phraseData = new();
    private readonly Dictionary<string, StackPanel> _phrasePanels = new();
    private readonly Dictionary<string, Border> _sectionPanels = new();
    private readonly List<(TextBox From, TextBox To)> _textReplacements = new();

    private TextBox? _txtModName, _txtAuthor, _txtVersion, _txtDescription;
    private TextBox? _txtAccentHex, _txtLightHex, _txtDarkHex, _txtFilterHex;
    private TextBox? _txtBgHex, _txtPanelHex, _txtSurfaceHex;
    private Border? _swatchAccent, _swatchLight, _swatchDark, _swatchFilter;
    private Border? _swatchBg, _swatchPanel, _swatchSurface;
    private StackPanel? _previewStrip;
    private TextBox? _txtCompanionName, _txtUserTerm, _txtModeDisplayName, _txtTalkToLabel, _txtTakeoverLabel;
    private TextBox? _txtFreeze, _txtReset, _txtCumCollapse, _txtAutonomyOn;
    private TextBox? _txtAttentionFail, _txtAttentionMercy, _txtBubbleRetry;
    private StackPanel? _replacementsPanel;

    private readonly Dictionary<int, CheckBox> _avatarSetCheckboxes = new();
    private readonly Dictionary<int, StackPanel> _avatarSetContainers = new();
    private StackPanel? _avatarSetsParent;
    private readonly List<(int SetNum, TextBox LabelBox, TextBox LevelBox, StackPanel Container)> _customAvatarSets = new();
    private int _nextCustomSetNum = 8;

    private readonly Dictionary<string, string?> _audioSlots = new();
    private readonly Dictionary<string, TextBlock> _audioFileLabels = new();
    private readonly List<(string FilePath, StackPanel Row)> _voiceLines = new();
    private StackPanel? _voiceLinesPanel;
    private Button? _activePlayButton;

    private TextBox? _txtBrowserUrl, _txtBrowserSiteName;
    private CheckBox? _chkShowBambiCloud;
    private readonly List<(TextBox Name, TextBox Url)> _videoLinks = new();
    private StackPanel? _videoLinksPanel;

    private string _activeSectionKey = "";
    private readonly Dictionary<string, Button> _sidebarButtons = new();
    private string? _loadedTempDir;
    private readonly bool _startWithTutorial;
    private TutorialOverlay? _tutorialOverlay;

    private readonly IDialogService? _dialogService;
    private readonly IModService _mods;
    private readonly IAudioPlayer? _audioPlayer;
    private string? _previewingPath;

    private static readonly (string Key, string Name)[] AchievementSlots =
    {
        ("achievements/lv_10.png", "Plastic Initiation"),
        ("achievements/Dumb_Bimbo.png", "Dumb Bimbo"),
        ("achievements/lv_50.png", "Fully Synthetic"),
        ("achievements/docile_cow.png", "Docile Cow"),
        ("achievements/perfect_plastic_puppet.png", "Perfect Plastic Puppet"),
        ("achievements/BrainwashedSlavedoll.png", "Brainwashed Slavedoll"),
        ("achievements/PlatinumPuppet.png", "Platinum Puppet"),
        ("achievements/10_hours_pink.png", "Rose-Tinted Reality"),
        ("achievements/deep_sleep.png", "Deep Sleep Mode"),
        ("achievements/daily_maintenance.png", "Daily Maintenance"),
        ("achievements/retinal_burn.png", "Retinal Burn"),
        ("achievements/morning_glory.png", "Morning Glory"),
        ("achievements/player_2_disconnected.png", "Player 2 Disconnected"),
        ("achievements/sofa_decor.png", "Sofa Decor"),
        ("achievements/look_but_dont_touch.png", "Look But Don't Touch"),
        ("achievements/spiral_eyes.png", "Spiral Eyes"),
        ("achievements/Mathematician's_nightmare.png", "Mathematician's Nightmare"),
        ("achievements/pop_the_Thought.png", "Pop Goes The Thought"),
        ("achievements/typing_tutor.png", "Typing Tutor"),
        ("achievements/obedience_reflex.png", "Obedience Reflex"),
        ("achievements/mercy_beggar.png", "Mercy Beggar"),
        ("achievements/clean_slate.png", "Clean Slate"),
        ("achievements/corner_hit.png", "Corner Hit"),
        ("achievements/Neon_obsession.png", "Neon Obsession"),
        ("achievements/What_panic_button.png", "Panic Button?"),
        ("achievements/relapse.png", "Relapse"),
        ("achievements/total_lockdown.png", "Total Lockdown"),
        ("achievements/system_overload.png", "System Overload"),
        ("achievements/how_many.png", "How Many?"),
    };

    private static readonly (string Key, string Name)[] FeatureSlots =
    {
        ("features/flash.png", "Flash Images"),
        ("features/mandatory_videos.png", "Mandatory Videos"),
        ("features/subliminal.png", "Subliminal Text"),
        ("features/bouncing_text.png", "Bouncing Text"),
        ("features/Pink_filter.png", "Pink Filter"),
        ("features/spiral_overlay.png", "Spiral Overlay"),
        ("features/brain_drain.png", "Brain Drain"),
        ("features/Bubble_pop.png", "Bubbles"),
        ("features/Phrase_Lock.png", "Lock Cards"),
        ("features/Bubble_count.png", "Bubble Count"),
        ("features/corner_gif.png", "Corner GIF"),
        ("features/audio_whispers.png", "Audio Whispers"),
        ("features/Mind_Wipers.png", "Mind Wipe"),
        ("features/bambi takeover.png", "Takeover"),
        ("features/takeover.png", "Takeover Alt"),
        ("features/vibe.png", "Vibe"),
        ("features/4new.png", "New Features"),
    };

    private static readonly (string Key, string Name)[] SkillSlots =
    {
        ("skills/pink_hours.png", "Pink Hours"),
        ("skills/ditzy_data.png", "Ditzy Data"),
        ("skills/sparkle_boost_1.png", "Sparkle Boost"),
        ("skills/good_girl_streak.png", "Good Girl Streak"),
        ("skills/hive_mind.png", "Hive Mind"),
        ("skills/trophy_case.png", "Trophy Case"),
        ("skills/sparkle_boost_2.png", "Extra Sparkly"),
        ("skills/lucky_bimbo.png", "Lucky Bimbo"),
        ("skills/milestone_rewards.png", "Milestone Rewards"),
        ("skills/oopsie_insurance.png", "Oopsie Insurance"),
        ("skills/popular_girl.png", "Popular Girl"),
        ("skills/quest_refresh.png", "Quest Refresh"),
        ("skills/better_quests.png", "Better Quests"),
        ("skills/sparkle_boost_3.png", "Maximum Sparkle"),
        ("skills/lucky_bubbles.png", "Lucky Bubbles"),
        ("skills/pink_rush.png", "Pink Rush"),
        ("skills/streak_power.png", "Streak Power"),
        ("skills/reroll_addict.png", "Reroll Addict"),
        ("skills/perfect_bimbo_week.png", "Perfect Bimbo Week"),
        ("skills/night_shift.png", "Night Shift"),
        ("skills/early_bird_bimbo.png", "Early Bird Bimbo"),
        ("skills/eternal_doll.png", "Eternal Doll"),
    };

    private static readonly (string SetLabel, string Prefix, int SetNum)[] AvatarSets =
    {
        ("Default", "avatar", 1),
        ("Level 20", "avatar2", 2),
        ("Level 35", "avatar3", 3),
        ("Level 50", "avatar4", 4),
        ("Level 125", "avatar5", 5),
        ("Level 150", "avatar6", 6),
        ("Level 175", "avatar7", 7),
    };

    private static readonly (string Key, string Name)[] UiAssetSlots =
    {
        ("bubble.png", "Bubble"),
        ("tube.png", "Tube"),
        ("tube2.png", "Tube Alt"),
        ("spiral.gif", "Spiral GIF"),
        ("logo.png", "Logo"),
    };

    private static readonly string[] PhraseCategories =
    {
        "Greeting", "StartupGreeting", "Idle", "RandomFloating", "Generic",
        "Gaming", "Browsing", "Shopping", "Social", "Discord",
        "TrainingSite", "HypnoContent", "Working", "Media", "Learning",
        "WindowAwarenessIdle", "EngineStop", "FlashPre", "SubliminalAck",
        "RandomBubble", "BubbleCountMercy", "BubblePop", "GameFailed",
        "BubbleMissed", "FlashClicked", "LevelUp", "MindWipe", "BrainDrain"
    };

    private static readonly (string Key, string Label)[] SectionDefs =
    {
        ("info", "Info"),
        ("theme", "Theme"),
        ("identity", "Identity"),
        ("achievements", "Achievements"),
        ("features", "Features"),
        ("skills", "Skills"),
        ("avatars", "Avatars"),
        ("uiassets", "UI Assets"),
        ("audio", "Audio"),
        ("browser", "Browser"),
        ("triggers", "Triggers"),
        ("messages", "Messages"),
        ("phrases", "Phrases"),
        ("replacements", "Text Replacements"),
    };

    public ModCreatorWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ModCreatorWindow>>();
        _dialogService = App.Services?.GetService<IDialogService>();
        _mods = App.Services!.GetRequiredService<IModService>();
        _audioPlayer = App.Services?.GetService<IAudioPlayer>();
        BuildSidebar();
        BuildAllSections();
        PopulateDefaults();
        LoadActiveModAsPreset();
        NavigateToSection("info");
        UpdateStatusBar();
    }

    public ModCreatorWindow(bool startWithTutorial) : this()
    {
        _startWithTutorial = startWithTutorial;
        if (_startWithTutorial)
        {
            Loaded += (_, _) => LaunchTutorial();
        }
    }

    // ─── Title Bar ──────────────────────────────────────────
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void BtnMinimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close();

    private void BtnHelp_Click(object? sender, RoutedEventArgs e)
    {
        // Prefer the tutorial video when the topic ships a clip; otherwise fall
        // back to the existing coach-mark tutorial.
        // WPF ground truth: Windows/ModCreatorWindow.xaml.cs:229-240.
        var content = HelpContentService.GetContent("Modding");
        if (content.HasClip)
        {
            HelpVideoWindow.Show(content, this);
            return;
        }
        LaunchTutorial();
    }

    private void LaunchTutorial()
    {
        if (_tutorialOverlay != null) return;
        if (App.Tutorial == null) return;

        App.Tutorial.Start(TutorialType.Modding);

        // Wire OnActivate callbacks: steps with RequiresTab="mod:xxx" navigate to that section.
        foreach (var step in App.Tutorial.CurrentSteps)
        {
            if (step.RequiresTab != null && step.RequiresTab.StartsWith("mod:"))
            {
                var sectionKey = step.RequiresTab.Substring(4);
                step.OnActivate = () => NavigateToSection(sectionKey);
            }
        }

        App.Tutorial.CurrentStep?.OnActivate?.Invoke();

        _tutorialOverlay = new TutorialOverlay(this, App.Tutorial);
        _tutorialOverlay.Closed += (_, _) => _tutorialOverlay = null;
        _tutorialOverlay.Show();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        StopAudioPreview();
        CleanupTempDir();

        if (_tutorialOverlay != null)
        {
            App.Tutorial?.Skip();
            _tutorialOverlay.Close();
            _tutorialOverlay = null;
        }
    }

    // ─── Sidebar ────────────────────────────────────────────
    private void BuildSidebar()
    {
        foreach (var (key, label) in SectionDefs)
        {
            var btn = new Button
            {
                Content = label,
                Tag = key,
                HorizontalContentAlignment =HorizontalAlignment.Left,
                Padding = new Thickness(13, 9, 13, 9),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse("#C0C0C0")),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            var capturedKey = key;
            btn.Click += (_, _) => NavigateToSection(capturedKey);
            _sidebarButtons[key] = btn;
            SidebarPanel.Children.Add(btn);
        }
    }

    private void NavigateToSection(string key)
    {
        _activeSectionKey = key;

        foreach (var (k, btn) in _sidebarButtons)
        {
            if (k == key)
            {
                btn.Background = new SolidColorBrush(Color.Parse("#353560"));
                btn.Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!;
                btn.FontWeight = FontWeight.SemiBold;
                btn.BorderBrush = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["PinkColor"]!);
                btn.BorderThickness = new Thickness(3, 0, 0, 0);
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = new SolidColorBrush(Color.Parse("#C0C0C0"));
                btn.FontWeight = FontWeight.Normal;
                btn.BorderThickness = new Thickness(0);
            }
        }

        foreach (var (k, panel) in _sectionPanels)
            panel.IsVisible = k == key;

        ContentScroll.ScrollToHome();
    }

    // ─── Build All Sections ──────────────────────────────────
    private void BuildAllSections()
    {
        BuildInfoSection();
        BuildThemeSection();
        BuildIdentitySection();
        BuildImageSlotsSection("achievements", "Achievements", "Custom badge images for the built-in achievements shown in the Trophy Case. Use square PNGs (128x128 recommended). Achievement display names are changed via Text Replacements, not here.", AchievementSlots);
        BuildImageSlotsSection("features", "Features", "Icons for the feature tiles in the main control panel tabs (Flashes, Videos, Overlays, etc). Use square PNGs with transparent backgrounds. Feature display names are changed via Text Replacements.", FeatureSlots);
        BuildImageSlotsSection("skills", "Skills", "Icons for the nodes in the skill/enhancement tree. Each icon represents a specific unlockable skill. Use square PNGs. Skill display names are changed via Text Replacements.", SkillSlots);
        BuildAvatarsSection();
        BuildImageSlotsSection("uiassets", "UI Assets", "Miscellaneous UI images: Bubble is the floating orb in the pop minigame, Tube is the glass container around the avatar, Spiral GIF is the hypnotic overlay animation, and Logo replaces the app logo.", UiAssetSlots);
        BuildAudioSection();
        BuildBrowserSection();
        BuildTriggersSection();
        BuildMessagesSection();
        BuildPhrasesSection();
        BuildReplacementsSection();
    }

    private Border CreateSectionPanel(string key)
    {
        var panel = new Border
        {
            IsVisible = false,
            Padding = new Thickness(0, 5, 0, 20),
        };
        _sectionPanels[key] = panel;
        ContentPanel.Children.Add(panel);
        return panel;
    }

    private static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["PinkColor"]!),
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        };
    }

    private static TextBlock CreateSectionDescription(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#909090")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -6, 0, 14),
            MaxWidth = 600,
            HorizontalAlignment = HorizontalAlignment.Left,
            LineHeight = 18,
        };
    }

    private static TextBlock CreateSubHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#B0B0B0")),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 6),
        };
    }

    private static TextBlock CreateFieldLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#808080")),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 3),
        };
    }

    private TextBox CreateDarkTextBox(string placeholder = "", bool multiline = false, double height = 0)
    {
        var tb = new TextBox
        {
            Theme = this.FindResource("DarkTextBox") as ControlTheme,
            Margin = new Thickness(0, 0, 0, 4),
            MaxWidth = 500,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (multiline)
        {
            tb.AcceptsReturn = true;
            tb.TextWrapping = TextWrapping.Wrap;
            ScrollViewer.SetVerticalScrollBarVisibility(tb, ScrollBarVisibility.Auto);
            if (height > 0) tb.Height = height;
        }
        if (!string.IsNullOrEmpty(placeholder))
        {
            tb.Tag = placeholder;
            tb.Text = "";
            tb.Foreground = new SolidColorBrush(Color.Parse("#606080"));
            tb.Text = placeholder;
            tb.GotFocus += PlaceholderTextBox_GotFocus;
            tb.LostFocus += PlaceholderTextBox_LostFocus;
        }
        return tb;
    }

    private void PlaceholderTextBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is string ph && tb.Text == ph)
        {
            tb.Text = "";
            tb.Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!;
        }
    }

    private void PlaceholderTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is string ph && string.IsNullOrEmpty(tb.Text))
        {
            tb.Text = ph;
            tb.Foreground = new SolidColorBrush(Color.Parse("#606080"));
        }
    }

    private string GetTextBoxValue(TextBox? tb)
    {
        if (tb == null) return "";
        if (tb.Tag is string ph && tb.Text == ph) return "";
        return tb.Text ?? "";
    }

    private void SetTextBoxValue(TextBox? tb, string? value)
    {
        if (tb == null) return;
        if (string.IsNullOrEmpty(value))
        {
            if (tb.Tag is string ph)
            {
                tb.Text = ph;
                tb.Foreground = new SolidColorBrush(Color.Parse("#606080"));
            }
            else
            {
                tb.Text = "";
            }
        }
        else
        {
            tb.Text = value;
            tb.Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!;
        }
    }


    // ─── Info Section ────────────────────────────────────────
    private void BuildInfoSection()
    {
        var panel = CreateSectionPanel("info");
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader("Mod Info"));
        stack.Children.Add(CreateSectionDescription("Basic metadata for your mod package. Name and author are required. The preview image appears as your mod's thumbnail in the mod browser."));

        stack.Children.Add(CreateFieldLabel("Mod Name *"));
        _txtModName = CreateDarkTextBox();
        _txtModName.Width = 350;
        stack.Children.Add(_txtModName);

        stack.Children.Add(CreateFieldLabel("Author *"));
        _txtAuthor = CreateDarkTextBox();
        _txtAuthor.Width = 250;
        stack.Children.Add(_txtAuthor);

        stack.Children.Add(CreateFieldLabel("Version"));
        _txtVersion = CreateDarkTextBox();
        _txtVersion.Width = 120;
        _txtVersion.Text = "1.0.0";
        _txtVersion.Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!;
        stack.Children.Add(_txtVersion);

        stack.Children.Add(CreateFieldLabel("Description"));
        _txtDescription = CreateDarkTextBox(multiline: true, height: 80);
        _txtDescription.Width = 500;
        stack.Children.Add(_txtDescription);

        stack.Children.Add(CreateFieldLabel("Preview Image"));
        var previewSlot = CreateImageSlot("preview", "Preview Image", 160, 120);
        stack.Children.Add(previewSlot);
    }

    // ─── Theme Section ───────────────────────────────────────
    private void BuildThemeSection()
    {
        var panel = CreateSectionPanel("theme");
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader("Theme Colors"));
        stack.Children.Add(CreateSectionDescription("Colors applied across the entire app UI when your mod is active. Accent is the primary highlight (buttons, links, progress bars). Filter Color tints the screen overlay. Keep sufficient contrast between Background/Panel/Surface or text becomes unreadable."));

        (_swatchAccent, _txtAccentHex) = CreateColorRow(stack, "Accent Color", "#FF69B4");
        (_swatchLight, _txtLightHex) = CreateColorRow(stack, "Light Color", "#FFB6C1");
        (_swatchDark, _txtDarkHex) = CreateColorRow(stack, "Dark Color", "#FF1493");
        (_swatchFilter, _txtFilterHex) = CreateColorRow(stack, "Filter Color", "#FF69B4");

        stack.Children.Add(CreateSubHeader("Background Colors"));
        (_swatchBg, _txtBgHex) = CreateColorRow(stack, "Background Color", "#1A1A2E");
        (_swatchPanel, _txtPanelHex) = CreateColorRow(stack, "Panel Color", "#252542");
        (_swatchSurface, _txtSurfaceHex) = CreateColorRow(stack, "Surface Color", "#1E1E3A");

        stack.Children.Add(CreateSubHeader("Preview"));
        _previewStrip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        for (int i = 0; i < 6; i++)
        {
            _previewStrip.Children.Add(new Border
            {
                Width = 60,
                Height = 30,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 0),
            });
        }
        stack.Children.Add(_previewStrip);
        UpdateThemePreview();
    }

    private (Border swatch, TextBox hexBox) CreateColorRow(StackPanel parent, string label, string defaultHex)
    {
        parent.Children.Add(CreateFieldLabel(label));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        var swatch = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(4),
            Background = BrushFromHex(defaultHex),
            Margin = new Thickness(0, 0, 8, 0),
            BorderBrush = new SolidColorBrush(Color.Parse("#505070")),
            BorderThickness = new Thickness(1),
        };
        row.Children.Add(swatch);

        var hexBox = new TextBox
        {
            Theme = this.FindResource("DarkTextBox") as ControlTheme,
            Width = 100,
            Text = defaultHex,
            Margin = new Thickness(0, 0, 8, 0),
        };
        hexBox.TextChanged += (_, _) =>
        {
            var hex = hexBox.Text?.Trim() ?? "";
            if (TryParseHex(hex, out var color))
            {
                swatch.Background = new SolidColorBrush(color);
                UpdateThemePreview();
            }
        };
        row.Children.Add(hexBox);

        var pickBtn = new Button
        {
            Content = "Pick",
            Theme = this.FindResource("SecondaryButton") as ControlTheme,
            Padding = new Thickness(10, 4, 10, 4),
        };
        pickBtn.Click += async (_, _) =>
        {
            var result = await ShowColorPickerAsync(hexBox.Text?.Trim() ?? defaultHex).ConfigureAwait(true);
            if (result != null)
            {
                hexBox.Text = result;
            }
        };
        row.Children.Add(pickBtn);

        parent.Children.Add(row);
        return (swatch, hexBox);
    }

    private void UpdateThemePreview()
    {
        if (_previewStrip == null || _txtAccentHex == null || _txtLightHex == null || _txtDarkHex == null) return;

        var hexes = new[]
        {
            _txtAccentHex.Text?.Trim() ?? "#FF69B4",
            _txtLightHex.Text?.Trim() ?? "#FFB6C1",
            _txtDarkHex.Text?.Trim() ?? "#FF1493",
            _txtBgHex?.Text?.Trim() ?? "#1A1A2E",
            _txtPanelHex?.Text?.Trim() ?? "#252542",
            _txtSurfaceHex?.Text?.Trim() ?? "#1E1E3A"
        };
        for (int i = 0; i < hexes.Length && i < _previewStrip.Children.Count; i++)
        {
            if (_previewStrip.Children[i] is Border b)
                b.Background = BrushFromHex(hexes[i]);
        }
    }

    // ─── Identity Section ────────────────────────────────────
    private void BuildIdentitySection()
    {
        var panel = CreateSectionPanel("identity");
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader("Identity"));
        stack.Children.Add(CreateSectionDescription("Renames core concepts throughout the app. Companion Name replaces the avatar's name in speech bubbles. User Term is what the companion calls you. Mode Display Name appears in the title bar. Talk To / Takeover labels change the companion interaction buttons."));

        stack.Children.Add(CreateFieldLabel("Companion Name"));
        _txtCompanionName = CreateDarkTextBox("");
        _txtCompanionName.Width = 250;
        stack.Children.Add(_txtCompanionName);

        stack.Children.Add(CreateFieldLabel("User Term"));
        _txtUserTerm = CreateDarkTextBox("");
        _txtUserTerm.Width = 250;
        stack.Children.Add(_txtUserTerm);

        stack.Children.Add(CreateFieldLabel("Mode Display Name"));
        _txtModeDisplayName = CreateDarkTextBox("");
        _txtModeDisplayName.Width = 250;
        stack.Children.Add(_txtModeDisplayName);

        stack.Children.Add(CreateFieldLabel("Talk To Label"));
        _txtTalkToLabel = CreateDarkTextBox("");
        _txtTalkToLabel.Width = 250;
        stack.Children.Add(_txtTalkToLabel);

        stack.Children.Add(CreateFieldLabel("Takeover Label"));
        _txtTakeoverLabel = CreateDarkTextBox("");
        _txtTakeoverLabel.Width = 250;
        stack.Children.Add(_txtTakeoverLabel);
    }

    // ─── Image Slots Sections ────────────────────────────────
    private void BuildImageSlotsSection(string key, string header, string description, (string Key, string Name)[] slots)
    {
        var panel = CreateSectionPanel(key);
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader(header));
        stack.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = new SolidColorBrush(Color.Parse("#808080")),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        });

        var wrap = new WrapPanel();
        foreach (var (slotKey, name) in slots)
        {
            var displayName = _mods.MakeModAware(name);
            wrap.Children.Add(CreateImageSlot(slotKey, displayName));
        }
        stack.Children.Add(wrap);
    }

    // ─── Avatars Section ─────────────────────────────────────
    private void BuildAvatarsSection()
    {
        var panel = CreateSectionPanel("avatars");
        var stack = new StackPanel();
        panel.Child = stack;
        _avatarSetsParent = stack;

        stack.Children.Add(CreateSectionHeader("Avatars"));
        stack.Children.Add(new TextBlock
        {
            Text = "Toggle which avatar sets your mod supports. Uncheck sets you don't have images for.",
            Foreground = new SolidColorBrush(Color.Parse("#808080")),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        });

        var checkboxRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (setLabel, _, setNum) in AvatarSets)
        {
            var cb = new CheckBox
            {
                Content = $"Set {setNum}: {setLabel}",
                IsChecked = true,
                Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
                Margin = new Thickness(0, 0, 16, 4),
                FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var capturedSetNum = setNum;
            cb.IsCheckedChanged += (_, _) => ToggleAvatarSet(capturedSetNum, cb.IsChecked == true);
            _avatarSetCheckboxes[setNum] = cb;
            checkboxRow.Children.Add(cb);
        }
        stack.Children.Add(checkboxRow);

        foreach (var (setLabel, prefix, setNum) in AvatarSets)
        {
            var container = new StackPanel();
            container.Children.Add(CreateSubHeader($"Set {setNum}: {setLabel}"));

            var wrap = new WrapPanel();
            for (int pose = 1; pose <= 4; pose++)
            {
                var filename = setNum == 1
                    ? $"avatar_pose{pose}.png"
                    : $"{prefix}_pose{pose}.png";
                wrap.Children.Add(CreateImageSlot(filename, $"Pose {pose}"));
            }
            container.Children.Add(wrap);
            _avatarSetContainers[setNum] = container;
            stack.Children.Add(container);
        }

        var accentHex = _mods.GetAccentColorHex();
        var addBtn = new Button
        {
            Content = "+ Add Custom Avatar Set",
            Background = new SolidColorBrush(Color.Parse("#2A2A4A")),
            Foreground = new SolidColorBrush(Color.Parse(accentHex)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse(accentHex)),
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        addBtn.Click += (_, _) => AddCustomAvatarSet();
        stack.Children.Add(addBtn);
    }

    private void ToggleAvatarSet(int setNum, bool enabled)
    {
        if (_avatarSetContainers.TryGetValue(setNum, out var container))
            container.IsVisible = enabled;
    }

    private void AddCustomAvatarSet(int setNum = 0, string? label = null, int unlockLevel = 200)
    {
        if (setNum == 0) setNum = _nextCustomSetNum++;
        if (setNum >= _nextCustomSetNum) _nextCustomSetNum = setNum + 1;

        var container = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        headerRow.Children.Add(new TextBlock
        {
            Text = $"Custom Set {setNum}:  Label ",
            Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        });
        var lblBox = CreateDarkTextBox(label ?? $"Set {setNum}");
        lblBox.Width = 150;
        headerRow.Children.Add(lblBox);

        headerRow.Children.Add(new TextBlock
        {
            Text = "  Unlock Level ",
            Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        });
        var lvlBox = CreateDarkTextBox(unlockLevel.ToString());
        lvlBox.Width = 60;
        headerRow.Children.Add(lvlBox);

        var capturedSetNum = setNum;
        var capturedContainer = container;
        var removeBtn = new Button
        {
            Content = "✕ Remove", Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
            BorderThickness = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 11, Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        removeBtn.Click += (_, _) =>
        {
            _avatarSetsParent?.Children.Remove(capturedContainer);
            _customAvatarSets.RemoveAll(c => c.SetNum == capturedSetNum);
        };
        headerRow.Children.Add(removeBtn);
        container.Children.Add(headerRow);

        var wrap = new WrapPanel();
        for (int pose = 1; pose <= 4; pose++)
            wrap.Children.Add(CreateImageSlot($"avatar{setNum}_pose{pose}.png", $"Pose {pose}"));
        container.Children.Add(wrap);

        _customAvatarSets.Add((setNum, lblBox, lvlBox, container));

        if (_avatarSetsParent != null)
            _avatarSetsParent.Children.Insert(_avatarSetsParent.Children.Count - 1, container);
    }


    // ─── Audio Section ──────────────────────────────────────
    private void BuildAudioSection()
    {
        var panel = CreateSectionPanel("audio");
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader("Audio"));
        stack.Children.Add(new TextBlock
        {
            Text = "Replace companion sounds, bubble pops, and add custom voice lines. Voice line filenames become the spoken text.",
            Foreground = new SolidColorBrush(Color.Parse("#808080")),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        stack.Children.Add(CreateSubHeader("Companion Sounds"));
        for (int i = 1; i <= 8; i++)
        {
            var key = $"sounds/giggle{i}.wav";
            stack.Children.Add(CreateAudioSlot(key, $"Giggle {i}"));
        }

        stack.Children.Add(CreateSubHeader("Bubble Pop Sounds"));
        foreach (var name in new[] { "Pop", "Pop2", "Pop3" })
        {
            var key = $"sounds/bubbles/{name}.wav";
            stack.Children.Add(CreateAudioSlot(key, name));
        }

        stack.Children.Add(CreateSubHeader("Lucky Bubble Chimes"));
        for (int i = 1; i <= 3; i++)
        {
            var key = $"sounds/chime{i}.mp3";
            stack.Children.Add(CreateAudioSlot(key, $"Chime {i}"));
        }

        stack.Children.Add(CreateSubHeader("Voice Lines"));
        stack.Children.Add(new TextBlock
        {
            Text = "Each file's name becomes the text the companion speaks. E.g. \"COMPLY.mp3\" → companion says \"COMPLY\".",
            Foreground = new SolidColorBrush(Color.Parse("#606080")),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        _voiceLinesPanel = new StackPanel();
        var voiceScroll = new ScrollViewer
        {
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _voiceLinesPanel,
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(voiceScroll);

        var accentHex = _mods.GetAccentColorHex();
        var addVoiceBtn = new Button
        {
            Content = "+ Add Voice Lines",
            Background = new SolidColorBrush(Color.Parse("#2A2A4A")),
            Foreground = new SolidColorBrush(Color.Parse(accentHex)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse(accentHex)),
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        addVoiceBtn.Click += async (_, _) =>
        {
            var files = await (_dialogService?.ShowOpenFileDialogAsync(
                "Select voice line audio files",
                new[] { new FileFilter("Audio Files", new[] { ".mp3", ".wav", ".ogg", ".flac" }) },
                allowMultiple: true) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

            foreach (var file in files)
                AddVoiceLineRow(file);
            UpdateStatusBar();
        };
        stack.Children.Add(addVoiceBtn);
    }

    private Grid CreateAudioSlot(string resourceKey, string displayName)
    {
        _audioSlots[resourceKey] = null;

        var grid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 4),
            Height = 32
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = displayName,
            Foreground = new SolidColorBrush(Color.FromRgb(192, 192, 192)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var fileLabel = new TextBlock
        {
            Text = "No file",
            Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128)),
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _audioFileLabels[resourceKey] = fileLabel;
        Grid.SetColumn(fileLabel, 1);
        grid.Children.Add(fileLabel);

        var playBtn = new Button
        {
            Content = "▶",
            Width = 28, Height = 28,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 12,
            IsVisible = false,
        };
        ToolTip.SetTip(playBtn, "Play preview");
        var capturedKey = resourceKey;
        playBtn.Click += (_, _) => ToggleAudioPreview(capturedKey, playBtn);
        Grid.SetColumn(playBtn, 2);
        grid.Children.Add(playBtn);

        var browseBtn = new Button
        {
            Content = "Browse",
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 80)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 10,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0)
        };
        browseBtn.Click += async (_, _) =>
        {
            var files = await (_dialogService?.ShowOpenFileDialogAsync(
                $"Select audio for {displayName}",
                new[] { new FileFilter("Audio Files", new[] { ".mp3", ".wav", ".ogg", ".flac" }) }) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
            if (files.Count > 0)
                SetAudioSlot(capturedKey, files[0]);
        };
        Grid.SetColumn(browseBtn, 3);
        grid.Children.Add(browseBtn);

        var clearBtn = new Button
        {
            Content = "✕",
            Width = 24, Height = 24,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            IsVisible = false
        };
        clearBtn.Click += (_, _) => ClearAudioSlot(capturedKey);
        Grid.SetColumn(clearBtn, 4);
        grid.Children.Add(clearBtn);

        return grid;
    }

    private void SetAudioSlot(string key, string filePath)
    {
        _audioSlots[key] = filePath;
        if (_audioFileLabels.TryGetValue(key, out var label))
        {
            label.Text = Path.GetFileName(filePath);
            label.FontStyle = FontStyle.Normal;
            label.Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!;
        }

        var grid = label?.GetVisualParent() as Grid;
        if (grid != null)
        {
            foreach (var child in grid.Children)
            {
                if (child is Button btn)
                {
                    var content = btn.Content?.ToString();
                    if (content == "▶" || content == "⏹")
                        btn.IsVisible = true;
                    if (content == "✕")
                        btn.IsVisible = true;
                }
            }
        }
        UpdateStatusBar();
    }

    private void ClearAudioSlot(string key)
    {
        _audioSlots[key] = null;
        if (_audioFileLabels.TryGetValue(key, out var label))
        {
            label.Text = Loc.Get("label_no_file");
            label.FontStyle = FontStyle.Italic;
            label.Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128));
        }

        var grid = label?.GetVisualParent() as Grid;
        if (grid != null)
        {
            foreach (var child in grid.Children)
            {
                if (child is Button btn)
                {
                    var content = btn.Content?.ToString();
                    if (content == "▶" || content == "⏹" || content == "✕")
                        btn.IsVisible = false;
                }
            }
        }
        UpdateStatusBar();
    }

    private void AddVoiceLineRow(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (_voiceLines.Any(v => Path.GetFileName(v.FilePath).Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            return;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };

        var playBtn = new Button
        {
            Content = "▶",
            Width = 24, Height = 24,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        var capturedPath = filePath;
        playBtn.Click += (_, _) => ToggleAudioPreview(capturedPath, playBtn);
        row.Children.Add(playBtn);

        row.Children.Add(new TextBlock
        {
            Text = Path.GetFileNameWithoutExtension(filePath),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        });

        row.Children.Add(new TextBlock
        {
            Text = Path.GetExtension(filePath),
            Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128)),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        });

        var removeBtn = new Button
        {
            Content = "✕",
            Width = 20, Height = 20,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 9,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var capturedRow = row;
        removeBtn.Click += (_, _) =>
        {
            _voiceLinesPanel?.Children.Remove(capturedRow);
            _voiceLines.RemoveAll(v => v.Row == capturedRow);
            UpdateStatusBar();
        };
        row.Children.Add(removeBtn);

        _voiceLines.Add((filePath, row));
        _voiceLinesPanel?.Children.Add(row);
    }

    private void ToggleAudioPreview(string keyOrPath, Button playBtn)
    {
        // Resolve a resource-key slot to its actual file path; voice-line rows already pass a path.
        var resolvedPath = _audioSlots.TryGetValue(keyOrPath, out var slotPath) && !string.IsNullOrEmpty(slotPath)
            ? slotPath
            : keyOrPath;

        if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
        {
            _logger?.LogWarning("ModCreatorWindow: no audio file to preview for {KeyOrPath}", keyOrPath);
            return;
        }

        if (string.Equals(_previewingPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
        {
            StopAudioPreview();
            return;
        }

        StopAudioPreview();

        try
        {
            _audioPlayer?.PlayAsync(resolvedPath);
            _previewingPath = resolvedPath;
            _activePlayButton = playBtn;
            playBtn.Content = "■";
            _logger?.LogInformation("ModCreatorWindow: playing audio preview {Path}", resolvedPath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ModCreatorWindow: failed to play audio preview {Path}", resolvedPath);
            playBtn.Content = "▶";
        }
    }

    private void StopAudioPreview()
    {
        try
        {
            _audioPlayer?.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ModCreatorWindow: failed to stop audio preview");
        }

        if (_activePlayButton != null)
        {
            _activePlayButton.Content = "▶";
        }

        _previewingPath = null;
        _activePlayButton = null;
    }

    private void LoadAudioFromResources(string resourcesDir)
    {
        foreach (var key in _audioSlots.Keys.ToList())
        {
            var audioPath = Path.Combine(resourcesDir, key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(audioPath))
            {
                var altExt = Path.GetExtension(audioPath).ToLowerInvariant() == ".wav" ? ".mp3" : ".wav";
                audioPath = Path.ChangeExtension(audioPath, altExt);
            }
            if (File.Exists(audioPath))
                SetAudioSlot(key, audioPath);
        }

        var voiceDir = Path.Combine(resourcesDir, "sounds", "flashes_audio");
        if (Directory.Exists(voiceDir))
        {
            foreach (var ext in new[] { "*.mp3", "*.wav", "*.ogg", "*.flac" })
                foreach (var file in Directory.GetFiles(voiceDir, ext).OrderBy(f => f))
                    AddVoiceLineRow(file);
        }
    }

    // ─── Browser Section ─────────────────────────────────────
    private void BuildBrowserSection()
    {
        var panel = CreateSectionPanel("browser");
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader("Browser"));
        stack.Children.Add(new TextBlock
        {
            Text = "Configure the embedded browser defaults and video links the companion will suggest.",
            Foreground = new SolidColorBrush(Color.Parse("#808080")),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        stack.Children.Add(CreateFieldLabel("Default Browser URL"));
        _txtBrowserUrl = CreateDarkTextBox("");
        _txtBrowserUrl.Width = 400;
        stack.Children.Add(_txtBrowserUrl);

        stack.Children.Add(CreateFieldLabel("Site Name"));
        _txtBrowserSiteName = CreateDarkTextBox("");
        _txtBrowserSiteName.Width = 250;
        stack.Children.Add(_txtBrowserSiteName);

        _chkShowBambiCloud = new CheckBox
        {
            Content = "Show BambiCloud option in browser menu",
            IsChecked = true,
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            Margin = new Thickness(0, 8, 0, 12),
            FontSize = 12
        };
        stack.Children.Add(_chkShowBambiCloud);

        stack.Children.Add(CreateSubHeader("Video Links"));
        stack.Children.Add(new TextBlock
        {
            Text = "Video name → URL pairs. The companion will suggest these videos and make them clickable in speech bubbles.",
            Foreground = new SolidColorBrush(Color.Parse("#606080")),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        _videoLinksPanel = new StackPanel();
        var linksScroll = new ScrollViewer
        {
            MaxHeight = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _videoLinksPanel,
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(linksScroll);

        var accentHex = _mods.GetAccentColorHex();
        var addLinkBtn = new Button
        {
            Content = "+ Add Video Link",
            Background = new SolidColorBrush(Color.Parse("#2A2A4A")),
            Foreground = new SolidColorBrush(Color.Parse(accentHex)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse(accentHex)),
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        addLinkBtn.Click += (_, _) => AddVideoLinkRow("", "");
        stack.Children.Add(addLinkBtn);
    }

    private void AddVideoLinkRow(string name, string url)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBox = CreateDarkTextBox(name);
        nameBox.Tag = "VideoName";
        Grid.SetColumn(nameBox, 0);
        row.Children.Add(nameBox);

        var urlBox = CreateDarkTextBox(url);
        urlBox.Tag = "VideoUrl";
        urlBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 255));
        Grid.SetColumn(urlBox, 2);
        row.Children.Add(urlBox);

        var removeBtn = new Button
        {
            Content = "✕",
            Width = 24, Height = 24,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var capturedRow = row;
        removeBtn.Click += (_, _) =>
        {
            _videoLinksPanel?.Children.Remove(capturedRow);
            _videoLinks.RemoveAll(v => v.Name == nameBox && v.Url == urlBox);
        };
        Grid.SetColumn(removeBtn, 3);
        row.Children.Add(removeBtn);

        _videoLinks.Add((nameBox, urlBox));
        _videoLinksPanel?.Children.Add(row);
    }

    // ─── Triggers Section ────────────────────────────────────
    private void BuildTriggersSection()
    {
        var panel = CreateSectionPanel("triggers");
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader("Triggers"));
        stack.Children.Add(CreateSectionDescription("Text displayed as large fullscreen overlays during sessions. These appear before mandatory videos and during special events. Keep them short and punchy -- they're shown briefly in large centered text."));

        stack.Children.Add(CreateFieldLabel("Freeze Trigger"));
        _txtFreeze = CreateDarkTextBox("");
        _txtFreeze.Width = 350;
        stack.Children.Add(_txtFreeze);

        stack.Children.Add(CreateFieldLabel("Reset Trigger"));
        _txtReset = CreateDarkTextBox("");
        _txtReset.Width = 350;
        stack.Children.Add(_txtReset);

        stack.Children.Add(CreateFieldLabel("Cum & Collapse"));
        _txtCumCollapse = CreateDarkTextBox("");
        _txtCumCollapse.Width = 350;
        stack.Children.Add(_txtCumCollapse);

        stack.Children.Add(CreateFieldLabel("Autonomy On"));
        _txtAutonomyOn = CreateDarkTextBox("");
        _txtAutonomyOn.Width = 350;
        stack.Children.Add(_txtAutonomyOn);
    }

    // ─── Messages Section ────────────────────────────────────
    private void BuildMessagesSection()
    {
        var panel = CreateSectionPanel("messages");
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader("Messages"));
        stack.Children.Add(CreateSectionDescription("System messages shown during minigames and attention checks. These appear as overlays when the user fails a video check or miscounts bubbles. Use \\n for line breaks."));

        stack.Children.Add(CreateFieldLabel("Attention Check Fail"));
        _txtAttentionFail = CreateDarkTextBox(multiline: true, height: 50);
        _txtAttentionFail.Width = 400;
        stack.Children.Add(_txtAttentionFail);

        stack.Children.Add(CreateFieldLabel("Attention Check Mercy"));
        _txtAttentionMercy = CreateDarkTextBox();
        _txtAttentionMercy.Width = 400;
        stack.Children.Add(_txtAttentionMercy);

        stack.Children.Add(CreateFieldLabel("Bubble Count Retry"));
        _txtBubbleRetry = CreateDarkTextBox(multiline: true, height: 50);
        _txtBubbleRetry.Width = 400;
        stack.Children.Add(_txtBubbleRetry);
    }


    // ─── Phrases Section ────────────────────────────────────
    private void BuildPhrasesSection()
    {
        var panel = CreateSectionPanel("phrases");
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader("Phrases"));
        stack.Children.Add(CreateSectionDescription("What the companion says in speech bubbles during different situations. Each category triggers contextually -- Gaming/Browsing/Social fire based on the active window, FlashPre before showing images, LevelUp on rank-up, etc. Use {0} as a placeholder for the detected app/site name in activity categories. Empty categories fall back to defaults."));

        foreach (var cat in PhraseCategories)
        {
            var phraseList = new List<string>();
            _phraseData[cat] = phraseList;

            var phrasePanel = new StackPanel();
            _phrasePanels[cat] = phrasePanel;

            var expander = new Expander
            {
                Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 4),
                IsExpanded = false,
            };

            var headerText = new TextBlock
            {
                Text = $"{FormatCategoryName(cat)} (0 phrases)",
                Foreground = new SolidColorBrush(Color.Parse("#C0C0C0")),
                FontSize = 13,
            };
            expander.Header = headerText;

            var body = new StackPanel { Margin = new Thickness(16, 4, 0, 4) };
            body.Children.Add(phrasePanel);

            var addBtn = new Button
            {
                Content = "+ Add phrase",
                Theme = this.FindResource("SecondaryButton") as ControlTheme,
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0),
                Tag = cat,
            };
            addBtn.Click += (_, _) =>
            {
                AddPhraseRow(cat, "");
                UpdatePhraseHeader(cat, expander);
            };
            body.Children.Add(addBtn);

            expander.Content = body;
            expander.Tag = cat;

            stack.Children.Add(expander);
        }
    }

    private void AddPhraseRow(string category, string text)
    {
        if (!_phraseData.ContainsKey(category)) return;
        _phraseData[category].Add(text);

        var panel = _phrasePanels[category];
        var idx = _phraseData[category].Count - 1;

        var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tb = new TextBox
        {
            Theme = this.FindResource("DarkTextBox") as ControlTheme,
            Text = text,
            FontSize = 12,
            Padding = new Thickness(6, 4, 6, 4),
        };
        var capturedIdx = idx;
        tb.TextChanged += (_, _) =>
        {
            if (capturedIdx < _phraseData[category].Count)
                _phraseData[category][capturedIdx] = tb.Text ?? "";
        };
        Grid.SetColumn(tb, 0);
        row.Children.Add(tb);

        var delBtn = new Button
        {
            Content = "\u00D7",
            Foreground = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["Danger"]!),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 16,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        delBtn.Click += (_, _) =>
        {
            var rowIndex = panel.Children.IndexOf(row);
            if (rowIndex >= 0 && rowIndex < _phraseData[category].Count)
            {
                _phraseData[category].RemoveAt(rowIndex);
                panel.Children.Remove(row);
                UpdatePhraseHeaderByCategory(category);
            }
        };
        Grid.SetColumn(delBtn, 1);
        row.Children.Add(delBtn);

        panel.Children.Add(row);
    }

    private void UpdatePhraseHeader(string category, Expander expander)
    {
        if (expander.Header is TextBlock tb)
            tb.Text = Loc.GetF("mod_phrase_header", FormatCategoryName(category), _phraseData[category].Count);
    }

    private void UpdatePhraseHeaderByCategory(string category)
    {
        if (!_sectionPanels.TryGetValue("phrases", out var sectionBorder)) return;
        if (sectionBorder.Child is not StackPanel sectionStack) return;

        foreach (var child in sectionStack.Children)
        {
            if (child is Expander exp && exp.Tag is string cat && cat == category)
            {
                UpdatePhraseHeader(category, exp);
                break;
            }
        }
    }

    private static string FormatCategoryName(string cat)
    {
        return Regex.Replace(cat, @"(?<=[a-z])([A-Z])", " $1");
    }

    // ─── Text Replacements Section ───────────────────────────
    private void BuildReplacementsSection()
    {
        var panel = CreateSectionPanel("replacements");
        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(CreateSectionHeader("Text Replacements"));
        stack.Children.Add(CreateSectionDescription("Find-and-replace pairs applied globally across the entire UI -- button labels, achievement names, quest descriptions, companion speech, tab headers, and more. Longer match strings are applied first to prevent partial replacements. This is the most powerful theming tool for completely re-skinning the app's vocabulary."));

        _replacementsPanel = new StackPanel();
        stack.Children.Add(_replacementsPanel);

        var addBtn = new Button
        {
            Content = "+ Add replacement",
            Theme = this.FindResource("SecondaryButton") as ControlTheme,
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };
        addBtn.Click += (_, _) => AddReplacementRow("", "");
        stack.Children.Add(addBtn);
    }

    private void AddReplacementRow(string fromText, string toText)
    {
        if (_replacementsPanel == null) return;

        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fromBox = new TextBox
        {
            Theme = this.FindResource("DarkTextBox") as ControlTheme,
            Text = fromText,
            FontSize = 12,
            Padding = new Thickness(6, 4, 6, 4),
        };
        Grid.SetColumn(fromBox, 0);
        row.Children.Add(fromBox);

        var arrow = new TextBlock
        {
            Text = "\u2192",
            Foreground = new SolidColorBrush(Color.Parse("#808080")),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        Grid.SetColumn(arrow, 1);
        row.Children.Add(arrow);

        var toBox = new TextBox
        {
            Theme = this.FindResource("DarkTextBox") as ControlTheme,
            Text = toText,
            FontSize = 12,
            Padding = new Thickness(6, 4, 6, 4),
        };
        Grid.SetColumn(toBox, 2);
        row.Children.Add(toBox);

        var delBtn = new Button
        {
            Content = "\u00D7",
            Foreground = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["Danger"]!),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 16,
            Cursor = new Cursor(StandardCursorType.Hand),
            Padding = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        delBtn.Click += (_, _) =>
        {
            var idx = _replacementsPanel.Children.IndexOf(row);
            if (idx >= 0)
            {
                _replacementsPanel.Children.Remove(row);
                if (idx < _textReplacements.Count)
                    _textReplacements.RemoveAt(idx);
            }
        };
        Grid.SetColumn(delBtn, 3);
        row.Children.Add(delBtn);

        _replacementsPanel.Children.Add(row);
        _textReplacements.Add((fromBox, toBox));
    }

    // ─── Image Slot Helper ───────────────────────────────────
    private StackPanel CreateImageSlot(string resourceKey, string displayName, double width = 100, double height = 100)
    {
        _imageSlots[resourceKey] = null;
        _imageNames[resourceKey] = displayName;

        var container = new StackPanel
        {
            Width = width + 20,
            Margin = new Thickness(4),
        };

        var borderHolder = new Grid { Width = width, Height = height };

        var hintImage = new Image
        {
            Opacity = 0.2,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };
        try
        {
            var hint = ResolveHintImage(resourceKey);
            if (hint != null) hintImage.Source = hint;
        }
        catch { /* no hint available */ }
        borderHolder.Children.Add(hintImage);

        var mainImage = new Image
        {
            Stretch = Stretch.Uniform,
            IsVisible = false,
            IsHitTestVisible = false,
        };
        _imageControls[resourceKey] = mainImage;
        borderHolder.Children.Add(mainImage);

        var plusText = new TextBlock
        {
            Text = "+",
            Foreground = new SolidColorBrush(Color.Parse("#505070")),
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        borderHolder.Children.Add(plusText);

        var clearBtn = new Button
        {
            Content = "\u00D7",
            Foreground = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["Danger"]!),
            Background = new SolidColorBrush(Color.FromArgb(180, 30, 30, 50)),
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Width = 18,
            Height = 18,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            IsVisible = false,
            Padding = new Thickness(0),
        };
        clearBtn.Click += (_, _) => ClearImageSlot(resourceKey);
        borderHolder.Children.Add(clearBtn);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#252542")),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.Parse("#505070")),
            BorderThickness = new Thickness(1),
            Width = width,
            Height = height,
            ClipToBounds = true,
            Child = borderHolder,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        border.PointerPressed += async (_, e) =>
        {
            if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                await BrowseImageForSlotAsync(resourceKey);
            }
        };

        border.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        });
        border.AddHandler(DragDrop.DropEvent, (_, e) => HandleImageDrop(resourceKey, e));
        DragDrop.SetAllowDrop(border, true);

        border.Tag = (clearBtn, plusText, hintImage);

        container.Children.Add(border);

        var filename = Path.GetFileName(resourceKey);
        container.Children.Add(new TextBlock
        {
            Text = filename,
            Foreground = new SolidColorBrush(Color.Parse("#606080")),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = width + 10,
        });

        var nameBox = new TextBox
        {
            Theme = this.FindResource("DarkTextBox") as ControlTheme,
            Text = displayName,
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = width + 10,
            TextAlignment = TextAlignment.Center,
        };
        nameBox.TextChanged += (_, _) => _imageNames[resourceKey] = nameBox.Text ?? "";
        _imageNameBoxes[resourceKey] = nameBox;
        container.Children.Add(nameBox);

        return container;
    }

    private static IImage? ResolveHintImage(string resourceKey)
    {
        // WPF ground truth: Windows/ModCreatorWindow.xaml.cs:1682
        // (Services.ModResourceResolver.ResolveImage(resourceKey)) - prefers the
        // active mod's override, else the embedded asset. ResolveBitmap already
        // swallows failures and returns null, matching the WPF try/catch.
        var resolver = App.Services?.GetService<AvaloniaModResourceResolver>();
        return resolver?.ResolveBitmap(resourceKey);
    }

    private void SetImageSlot(string key, string filePath)
    {
        if (!_imageControls.ContainsKey(key)) return;

        try
        {
            var bitmap = new Bitmap(filePath);

            _imageControls[key].Source = bitmap;
            _imageControls[key].IsVisible = true;
            _imageSlots[key] = filePath;

            var parent = _imageControls[key].GetVisualParent();
            while (parent != null && parent is not Border)
                parent = parent.GetVisualParent();
            if (parent is Border border && border.Tag is ValueTuple<Button, TextBlock, Image> tuple)
            {
                var (clearBtn, plusText, hintImage) = tuple;
                clearBtn.IsVisible = true;
                plusText.IsVisible = false;
                hintImage.Opacity = 0;
            }

            UpdateStatusBar();
        }
        catch { /* invalid image file */ }
    }

    private void ClearImageSlot(string key)
    {
        if (!_imageControls.ContainsKey(key)) return;

        _imageControls[key].Source = null;
        _imageControls[key].IsVisible = false;
        _imageSlots[key] = null;

        var parent = _imageControls[key].GetVisualParent();
        while (parent != null && parent is not Border)
            parent = parent.GetVisualParent();
        if (parent is Border border && border.Tag is ValueTuple<Button, TextBlock, Image> tuple)
        {
            var (clearBtn, plusText, hintImage) = tuple;
            clearBtn.IsVisible = false;
            plusText.IsVisible = true;
            hintImage.Opacity = 0.2;
        }

        UpdateStatusBar();
    }

    private async Task BrowseImageForSlotAsync(string key)
    {
        var files = await (_dialogService?.ShowOpenFileDialogAsync(
            $"Select image for {Path.GetFileName(key)}",
            new[] { new FileFilter("Image Files", new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" }) }) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        if (files.Count > 0)
            SetImageSlot(key, files[0]);
    }

    private void HandleImageDrop(string key, DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Contains(DataFormat.File)) return;
        var files = e.DataTransfer.TryGetFiles()?.Select(f => f.Path.LocalPath).ToArray();
        if (files?.Length > 0)
            SetImageSlot(key, files[0]);
        e.Handled = true;
    }

    // ─── Color Picker ────────────────────────────────────────
    private async Task<string?> ShowColorPickerAsync(string currentHex)
    {
        if (!TryParseHex(currentHex, out var initialColor))
            initialColor = (Color)global::Avalonia.Application.Current!.Resources["PinkColor"]!;

        var dialog = new ColorPickerDialog(initialColor);
        var result = await dialog.ShowDialog<object?>(this);
        if (result is Color chosen)
        {
            var hex = $"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}";
            _logger?.LogInformation("ModCreatorWindow: color picked {Hex}", hex);
            return hex;
        }

        return null;
    }

    // ─── Populate Defaults ───────────────────────────────────
    private void PopulateDefaults()
    {
        // New mods start empty — no pre-filled content from the base mod.
    }

    private void LoadActiveModAsPreset()
    {
        try
        {
            dynamic? mods = CoreApp.Mods;
            if (mods == null) return;
            var activeMod = (ModPackage?)mods.ActiveMod;
            if (activeMod == null || activeMod.IsBuiltIn || string.IsNullOrEmpty(activeMod.InstalledPath))
                return;

            var manifestPath = Path.Combine(activeMod.InstalledPath, "mod.json");
            if (!File.Exists(manifestPath)) return;

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonConvert.DeserializeObject<ModManifest>(json);
            if (manifest == null) return;

            PopulateFromManifest(manifest);

            var resourcesDir = Path.Combine((string)activeMod.InstalledPath, "resources");
            if (Directory.Exists(resourcesDir))
            {
                foreach (var key in _imageSlots.Keys.ToList())
                {
                    var imgPath = Path.Combine(resourcesDir, key.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(imgPath))
                        SetImageSlot(key, imgPath);
                }
                LoadAudioFromResources(resourcesDir);
            }

            TxtStatus.Text = Loc.GetF("mod_loaded_active", manifest.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to auto-load active mod as preset");
        }
    }


    private void PopulateFromManifest(ModManifest manifest)
    {
        SetTextBoxValue(_txtModName, manifest.Name);
        SetTextBoxValue(_txtAuthor, manifest.Author);
        SetTextBoxValue(_txtVersion, manifest.Version);
        SetTextBoxValue(_txtDescription, manifest.Description);

        if (manifest.Theme != null)
        {
            if (_txtAccentHex != null) _txtAccentHex.Text = manifest.Theme.AccentColor ?? "#FF69B4";
            if (_txtLightHex != null) _txtLightHex.Text = manifest.Theme.AccentLightColor ?? "#FFB6C1";
            if (_txtDarkHex != null) _txtDarkHex.Text = manifest.Theme.AccentDarkColor ?? "#FF1493";
            if (_txtBgHex != null) _txtBgHex.Text = manifest.Theme.BackgroundColor ?? "#1A1A2E";
            if (_txtPanelHex != null) _txtPanelHex.Text = manifest.Theme.PanelColor ?? "#252542";
            if (_txtSurfaceHex != null) _txtSurfaceHex.Text = manifest.Theme.SurfaceColor ?? "#1E1E3A";
            if (_txtFilterHex != null) _txtFilterHex.Text = manifest.Theme.FilterColor ?? "#FF69B4";
        }

        if (manifest.Identity != null)
        {
            SetTextBoxValue(_txtCompanionName, manifest.Identity.CompanionName);
            SetTextBoxValue(_txtUserTerm, manifest.Identity.UserTerm);
            SetTextBoxValue(_txtModeDisplayName, manifest.Identity.ModeDisplayName);
            SetTextBoxValue(_txtTalkToLabel, manifest.Identity.TalkToLabel);
            SetTextBoxValue(_txtTakeoverLabel, manifest.Identity.TakeoverLabel);
        }

        if (manifest.Triggers != null)
        {
            SetTextBoxValue(_txtFreeze, manifest.Triggers.Freeze);
            SetTextBoxValue(_txtReset, manifest.Triggers.Reset);
            SetTextBoxValue(_txtCumCollapse, manifest.Triggers.CumAndCollapse);
            SetTextBoxValue(_txtAutonomyOn, manifest.Triggers.AutonomyOn);
        }

        if (manifest.Messages != null)
        {
            SetTextBoxValue(_txtAttentionFail, manifest.Messages.AttentionCheckFail);
            SetTextBoxValue(_txtAttentionMercy, manifest.Messages.AttentionCheckMercy);
            SetTextBoxValue(_txtBubbleRetry, manifest.Messages.BubbleCountRetry);
        }

        if (manifest.Phrases != null)
        {
            foreach (var (cat, phrases) in manifest.Phrases)
            {
                if (!_phraseData.ContainsKey(cat)) continue;
                _phraseData[cat].Clear();
                _phrasePanels[cat].Children.Clear();

                foreach (var phrase in phrases)
                    AddPhraseRow(cat, phrase);

                UpdatePhraseHeaderByCategory(cat);
            }
        }

        if (manifest.Browser != null)
        {
            SetTextBoxValue(_txtBrowserUrl, manifest.Browser.DefaultUrl);
            SetTextBoxValue(_txtBrowserSiteName, manifest.Browser.SiteName);
            if (_chkShowBambiCloud != null)
                _chkShowBambiCloud.IsChecked = manifest.Browser.ShowBambiCloudOption ?? true;
            if (manifest.Browser.DefaultVideoLinks != null)
            {
                _videoLinks.Clear();
                _videoLinksPanel?.Children.Clear();
                foreach (var (vName, vUrl) in manifest.Browser.DefaultVideoLinks)
                    AddVideoLinkRow(vName, vUrl);
            }
        }

        if (manifest.TextReplacements != null)
        {
            _textReplacements.Clear();
            _replacementsPanel?.Children.Clear();

            foreach (var (from, to) in manifest.TextReplacements)
                AddReplacementRow(from, to);
        }

        if (manifest.SupportedAvatarSets != null)
        {
            foreach (var (setNum, cb) in _avatarSetCheckboxes)
            {
                var supported = manifest.SupportedAvatarSets.Contains(setNum);
                cb.IsChecked = supported;
                ToggleAvatarSet(setNum, supported);
            }
        }

        if (manifest.CustomAvatarSets != null)
        {
            foreach (var cs in manifest.CustomAvatarSets)
                AddCustomAvatarSet(cs.SetNumber, cs.Label, cs.UnlockLevel);
        }

        UpdateStatusBar();
    }

    private ModManifest BuildManifestFromForm()
    {
        var name = GetTextBoxValue(_txtModName);
        var manifest = new ModManifest
        {
            Id = SanitizeModId(name),
            Name = name,
            Version = GetTextBoxValue(_txtVersion),
            Author = GetTextBoxValue(_txtAuthor),
            Description = string.IsNullOrWhiteSpace(GetTextBoxValue(_txtDescription)) ? null : GetTextBoxValue(_txtDescription),
        };

        if (_imageSlots.TryGetValue("preview", out var previewPath) && previewPath != null)
            manifest.PreviewImage = "resources/preview" + Path.GetExtension(previewPath);

        var accent = _txtAccentHex?.Text?.Trim() ?? "#FF69B4";
        var light = _txtLightHex?.Text?.Trim() ?? "#FFB6C1";
        var dark = _txtDarkHex?.Text?.Trim() ?? "#FF1493";
        var bg = _txtBgHex?.Text?.Trim() ?? "#1A1A2E";
        var panel = _txtPanelHex?.Text?.Trim() ?? "#252542";
        var surface = _txtSurfaceHex?.Text?.Trim() ?? "#1E1E3A";
        var filter = _txtFilterHex?.Text?.Trim() ?? "#FF69B4";
        if (accent != "#FF69B4" || light != "#FFB6C1" || dark != "#FF1493"
            || bg != "#1A1A2E" || panel != "#252542" || surface != "#1E1E3A"
            || filter != accent)
        {
            manifest.Theme = new ModTheme
            {
                AccentColor = accent,
                AccentLightColor = light,
                AccentDarkColor = dark,
                BackgroundColor = bg != "#1A1A2E" ? bg : null,
                PanelColor = panel != "#252542" ? panel : null,
                SurfaceColor = surface != "#1E1E3A" ? surface : null,
                FilterColor = filter != accent ? filter : null,
            };
        }

        var cn = GetTextBoxValue(_txtCompanionName);
        var ut = GetTextBoxValue(_txtUserTerm);
        var mdn = GetTextBoxValue(_txtModeDisplayName);
        var ttl = GetTextBoxValue(_txtTalkToLabel);
        var tol = GetTextBoxValue(_txtTakeoverLabel);
        if (!string.IsNullOrEmpty(cn) || !string.IsNullOrEmpty(ut) || !string.IsNullOrEmpty(mdn)
            || !string.IsNullOrEmpty(ttl) || !string.IsNullOrEmpty(tol))
        {
            manifest.Identity = new ModIdentity
            {
                CompanionName = string.IsNullOrEmpty(cn) ? null : cn,
                UserTerm = string.IsNullOrEmpty(ut) ? null : ut,
                ModeDisplayName = string.IsNullOrEmpty(mdn) ? null : mdn,
                TalkToLabel = string.IsNullOrEmpty(ttl) ? null : ttl,
                TakeoverLabel = string.IsNullOrEmpty(tol) ? null : tol,
            };
        }

        var freeze = GetTextBoxValue(_txtFreeze);
        var reset = GetTextBoxValue(_txtReset);
        var cum = GetTextBoxValue(_txtCumCollapse);
        var auto = GetTextBoxValue(_txtAutonomyOn);
        if (!string.IsNullOrEmpty(freeze) || !string.IsNullOrEmpty(reset)
            || !string.IsNullOrEmpty(cum) || !string.IsNullOrEmpty(auto))
        {
            manifest.Triggers = new ModTriggers
            {
                Freeze = string.IsNullOrEmpty(freeze) ? null : freeze,
                Reset = string.IsNullOrEmpty(reset) ? null : reset,
                CumAndCollapse = string.IsNullOrEmpty(cum) ? null : cum,
                AutonomyOn = string.IsNullOrEmpty(auto) ? null : auto,
            };
        }

        var af = GetTextBoxValue(_txtAttentionFail);
        var am = GetTextBoxValue(_txtAttentionMercy);
        var br = GetTextBoxValue(_txtBubbleRetry);
        if (!string.IsNullOrEmpty(af) || !string.IsNullOrEmpty(am) || !string.IsNullOrEmpty(br))
        {
            manifest.Messages = new ModMessages
            {
                AttentionCheckFail = string.IsNullOrEmpty(af) ? null : af,
                AttentionCheckMercy = string.IsNullOrEmpty(am) ? null : am,
                BubbleCountRetry = string.IsNullOrEmpty(br) ? null : br,
            };
        }

        var phrases = new Dictionary<string, string[]>();
        foreach (var (cat, list) in _phraseData)
        {
            var filtered = list.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (filtered.Length > 0)
                phrases[cat] = filtered;
        }
        if (phrases.Count > 0)
            manifest.Phrases = phrases;

        var replacements = new Dictionary<string, string>();
        foreach (var (fromBox, toBox) in _textReplacements)
        {
            var from = fromBox.Text?.Trim() ?? "";
            var to = toBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(from) && !replacements.ContainsKey(from))
                replacements[from] = to;
        }
        if (replacements.Count > 0)
            manifest.TextReplacements = replacements;

        var browserUrl = GetTextBoxValue(_txtBrowserUrl);
        var siteName = GetTextBoxValue(_txtBrowserSiteName);
        var showBambi = _chkShowBambiCloud?.IsChecked;
        var vidLinks = new Dictionary<string, string>();
        foreach (var (nameBox, urlBox) in _videoLinks)
        {
            var vName = nameBox.Text?.Trim() ?? "";
            var vUrl = urlBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(vName) && !string.IsNullOrEmpty(vUrl) && !vidLinks.ContainsKey(vName))
                vidLinks[vName] = vUrl;
        }
        if (!string.IsNullOrEmpty(browserUrl) || !string.IsNullOrEmpty(siteName)
            || showBambi == false || vidLinks.Count > 0)
        {
            manifest.Browser = new ModBrowser
            {
                DefaultUrl = string.IsNullOrEmpty(browserUrl) ? null : browserUrl,
                SiteName = string.IsNullOrEmpty(siteName) ? null : siteName,
                ShowBambiCloudOption = showBambi == false ? false : null,
                DefaultVideoLinks = vidLinks.Count > 0 ? vidLinks : null,
            };
        }

        var enabledSets = _avatarSetCheckboxes
            .Where(kv => kv.Value.IsChecked == true)
            .Select(kv => kv.Key)
            .OrderBy(x => x)
            .ToList();
        foreach (var cs in _customAvatarSets)
            enabledSets.Add(cs.SetNum);
        if (enabledSets.Count < _avatarSetCheckboxes.Count + _customAvatarSets.Count || _customAvatarSets.Count > 0)
            manifest.SupportedAvatarSets = enabledSets.Distinct().OrderBy(x => x).ToList();

        if (_customAvatarSets.Count > 0)
        {
            manifest.CustomAvatarSets = _customAvatarSets.Select(cs => new CustomAvatarSet
            {
                SetNumber = cs.SetNum,
                Label = cs.LabelBox.Text?.Trim() ?? "",
                UnlockLevel = int.TryParse(cs.LevelBox.Text?.Trim() ?? "", out var lv) ? lv : 200
            }).ToList();
        }

        return manifest;
    }

    private async void BtnExport_Click(object? sender, RoutedEventArgs e)
    {
        var name = GetTextBoxValue(_txtModName);
        var author = GetTextBoxValue(_txtAuthor);
        if (string.IsNullOrWhiteSpace(name))
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_validation_error"),
                    Loc.Get("msg_mod_name_is_required"),
                    DialogSeverity.Warning);
            }
            NavigateToSection("info");
            _txtModName?.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(author))
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_validation_error"),
                    Loc.Get("msg_author_is_required"),
                    DialogSeverity.Warning);
            }
            NavigateToSection("info");
            _txtAuthor?.Focus();
            return;
        }

        var manifest = BuildManifestFromForm();

        var filePath = await (_dialogService?.ShowSaveFileDialogAsync(
            "Export Mod Package",
            new[] { new FileFilter("CCP Mod Files", new[] { ".ccpmod" }) },
            $"{manifest.Id}.ccpmod") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"ccpmod_export_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var resourcesDir = Path.Combine(tempDir, "resources");
            Directory.CreateDirectory(resourcesDir);

            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(Path.Combine(tempDir, "mod.json"), json);

            foreach (var (key, path) in _imageSlots)
            {
                if (path == null) continue;
                var destPath = Path.Combine(resourcesDir, key.Replace('/', Path.DirectorySeparatorChar));
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                File.Copy(path, destPath, overwrite: true);
            }

            foreach (var (key, audioPath) in _audioSlots)
            {
                if (audioPath == null) continue;
                var destPath = Path.Combine(resourcesDir, key.Replace('/', Path.DirectorySeparatorChar));
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                File.Copy(audioPath, destPath, overwrite: true);
            }

            if (_voiceLines.Count > 0)
            {
                var voiceDir = Path.Combine(resourcesDir, "sounds", "flashes_audio");
                Directory.CreateDirectory(voiceDir);
                foreach (var (srcPath, _) in _voiceLines)
                {
                    if (!File.Exists(srcPath)) continue;
                    var destPath = Path.Combine(voiceDir, Path.GetFileName(srcPath));
                    File.Copy(srcPath, destPath, overwrite: true);
                }
            }

            if (File.Exists(filePath))
                File.Delete(filePath);
            ZipFile.CreateFromDirectory(tempDir, filePath);

            try { Directory.Delete(tempDir, recursive: true); } catch { }

            TxtStatus.Text = Loc.GetF("mod_exported_filename", Path.GetFileName(filePath));
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_export_complete"),
                    Loc.GetF("msg_mod_exported_successfully", filePath),
                    DialogSeverity.Info);
            }
        }
        catch (Exception ex)
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_export_error"),
                    Loc.GetF("msg_export_failed", ex.Message),
                    DialogSeverity.Warning);
            }
        }
    }

    private async void BtnLoad_Click(object? sender, RoutedEventArgs e)
    {
        var files = await (_dialogService?.ShowOpenFileDialogAsync(
            "Load Mod Package",
            new[] { new FileFilter("CCP Mod Files", new[] { ".ccpmod" }) }) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

        if (files.Count == 0) return;

        try
        {
            CleanupTempDir();
            _loadedTempDir = Path.Combine(Path.GetTempPath(), $"ccpmod_load_{Guid.NewGuid():N}");
            ZipFile.ExtractToDirectory(files[0], _loadedTempDir);

            var manifestPath = Path.Combine(_loadedTempDir, "mod.json");
            if (!File.Exists(manifestPath))
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowMessageAsync(
                        Loc.Get("title_load_error"),
                        Loc.Get("msg_invalid_mod_package_mod_json_not_found"),
                        DialogSeverity.Warning);
                }
                CleanupTempDir();
                return;
            }

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonConvert.DeserializeObject<ModManifest>(json);
            if (manifest == null)
            {
                if (_dialogService != null)
                {
                    await _dialogService.ShowMessageAsync(
                        Loc.Get("title_load_error"),
                        Loc.Get("msg_failed_to_parse_mod_json"),
                        DialogSeverity.Warning);
                }
                CleanupTempDir();
                return;
            }

            foreach (var key in _imageSlots.Keys.ToList())
                ClearImageSlot(key);

            PopulateFromManifest(manifest);

            var resourcesDir = Path.Combine(_loadedTempDir, "resources");
            if (Directory.Exists(resourcesDir))
            {
                foreach (var key in _imageSlots.Keys.ToList())
                {
                    var imgPath = Path.Combine(resourcesDir, key.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(imgPath))
                        SetImageSlot(key, imgPath);
                }
                LoadAudioFromResources(resourcesDir);
            }

            NavigateToSection("info");
            TxtStatus.Text = Loc.GetF("mod_loaded", manifest.Name);
        }
        catch (Exception ex)
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_load_error"),
                    Loc.GetF("msg_load_failed", ex.Message),
                    DialogSeverity.Warning);
            }
        }
    }

    private async void BtnReset_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_confirm_reset"),
            Loc.Get("msg_reset_all_fields_to_defaults_this_cannot_be_u")) ?? Task.FromResult(false));
        if (!confirmed) return;

        SetTextBoxValue(_txtModName, "");
        SetTextBoxValue(_txtAuthor, "");
        if (_txtVersion != null) { _txtVersion.Text = "1.0.0"; _txtVersion.Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!; }
        SetTextBoxValue(_txtDescription, "");

        if (_txtAccentHex != null) _txtAccentHex.Text = "#FF69B4";
        if (_txtLightHex != null) _txtLightHex.Text = "#FFB6C1";
        if (_txtDarkHex != null) _txtDarkHex.Text = "#FF1493";
        if (_txtBgHex != null) _txtBgHex.Text = "#1A1A2E";
        if (_txtPanelHex != null) _txtPanelHex.Text = "#252542";
        if (_txtSurfaceHex != null) _txtSurfaceHex.Text = "#1E1E3A";
        if (_txtFilterHex != null) _txtFilterHex.Text = "#FF69B4";

        SetTextBoxValue(_txtCompanionName, "");
        SetTextBoxValue(_txtUserTerm, "");
        SetTextBoxValue(_txtModeDisplayName, "");
        SetTextBoxValue(_txtTalkToLabel, "");
        SetTextBoxValue(_txtTakeoverLabel, "");

        SetTextBoxValue(_txtFreeze, "");
        SetTextBoxValue(_txtReset, "");
        SetTextBoxValue(_txtCumCollapse, "");
        SetTextBoxValue(_txtAutonomyOn, "");

        foreach (var key in _imageSlots.Keys.ToList())
            ClearImageSlot(key);

        _textReplacements.Clear();
        _replacementsPanel?.Children.Clear();

        foreach (var (cat, phrases) in _phraseData)
        {
            phrases.Clear();
            if (_phrasePanels.TryGetValue(cat, out var panel))
                panel.Children.Clear();
            UpdatePhraseHeaderByCategory(cat);
        }

        StopAudioPreview();
        foreach (var key in _audioSlots.Keys.ToList())
            ClearAudioSlot(key);
        _voiceLines.Clear();
        _voiceLinesPanel?.Children.Clear();

        NavigateToSection("info");
        UpdateStatusBar();
        TxtStatus.Text = Loc.Get("label_reset_to_defaults");
    }

    private void UpdateStatusBar()
    {
        var filled = _imageSlots.Count(kv => kv.Value != null);
        var total = _imageSlots.Count;
        var audioFilled = _audioSlots.Count(kv => kv.Value != null);
        var phraseCount = _phraseData.Values.Sum(l => l.Count(p => !string.IsNullOrWhiteSpace(p)));
        TxtStatus.Text = Loc.GetF("mod_status_bar", filled, total, audioFilled, _voiceLines.Count, phraseCount, _textReplacements.Count);
    }

    private static string SanitizeModId(string name)
    {
        var id = name.ToLowerInvariant();
        id = Regex.Replace(id, @"[^a-z0-9\-]", "-");
        id = Regex.Replace(id, @"-+", "-");
        id = id.Trim('-');
        if (string.IsNullOrEmpty(id)) id = "custom-mod";
        return id;
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = (Color)global::Avalonia.Application.Current!.Resources["PinkColor"]!;
        try
        {
            if (!hex.StartsWith("#")) hex = "#" + hex;
            color = Color.Parse(hex);
            return true;
}
        catch { return false; }
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        if (TryParseHex(hex, out var c))
            return new SolidColorBrush(c);
        return (SolidColorBrush)global::Avalonia.Application.Current!.Resources["PinkBrush"]!;
    }

    private void CleanupTempDir()
    {
        if (_loadedTempDir != null && Directory.Exists(_loadedTempDir))
        {
            try { Directory.Delete(_loadedTempDir, recursive: true); } catch { }
            _loadedTempDir = null;
        }
    }
}
