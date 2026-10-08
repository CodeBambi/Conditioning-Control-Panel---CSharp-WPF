// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Controls/Companion/V2/ConversationPage.xaml.cs.
// Deviations: the sheets are always pages here (see the .axaml header), so OpenSheet only navigates;
// the confirm for "New chat" is the head's MessageDialog; visibility is the effective-visibility
// chain watch (Avalonia's IsVisible is a local flag, see CompanionRoomView).
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Asks;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.V2;

public partial class ConversationPage : UserControl
{
    private readonly ConversationPageVm? _vm;
    private readonly List<IDisposable> _visibilityWatch = new();
    private bool _followBottom = true;
    private bool _shown;

    /// <summary>The previewer's constructor: no room, nothing live.</summary>
    public ConversationPage() => InitializeComponent();

    private readonly Action? _onShown;

    /// <param name="onShown">Runs each time the page comes on screen (the host re-reads the hero's
    /// art: the room that would do it is collapsed under this page).</param>
    internal ConversationPage(CompanionHeroCardViewModel hero, EngineRoomVm engine, Action? onShown = null) : this()
    {
        _onShown = onShown;
        this.FindControl<AmbientFxCanvas>("StageDust")?.StartLayers(new AmbientFxConfig
        {
            Layers = AmbientFxLayers.DustField,
            Intensity = 0.8,
            DustDensity = 0.65,
            Tint = Color.FromRgb(244, 134, 211),
        });
        _vm = new ConversationPageVm(hero, engine)
        {
            StartSignIn = () => _ = Shell?.OpenUnifiedLoginDialog(),
        };
        DataContext = _vm;
        _vm.Turns.CollectionChanged += TurnsChanged;
        _vm.PropertyChanged += VmChanged;
        CompanionAskService.Instance.FocusChatRequested += FocusComposer;
        Composer.AddHandler(KeyDownEvent, Composer_KeyDown, RoutingStrategies.Tunnel);
        Transcript.ScrollChanged += Transcript_ScrollChanged;
        Unloaded += (_, _) => { _vm.Stop(); _vm.Detach(); };
        PaintStatus();
    }

    internal ConversationPageVm? Vm => _vm;

    private Windows.MainShellWindow? Shell => TopLevel.GetTopLevel(this) as Windows.MainShellWindow;

    // ---- effective visibility: resume while on screen, detach when not (WPF IsVisibleChanged) ----

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var v in this.GetSelfAndVisualAncestors())
            _visibilityWatch.Add(v.GetObservable(IsVisibleProperty).Subscribe(new VisibilityObserver(this)));
        SyncShown();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var d in _visibilityWatch) d.Dispose();
        _visibilityWatch.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void SyncShown()
    {
        var now = IsEffectivelyVisible;
        if (now == _shown || _vm == null) return;
        _shown = now;
        if (now)
        {
            try { _onShown?.Invoke(); } catch (Exception ex) { Log.Debug("Conversation page show hook: {E}", ex.Message); }
            _vm.Resume();
        }
        else _vm.Detach();
    }

    private sealed class VisibilityObserver : IObserver<bool>
    {
        private readonly ConversationPage _owner;
        public VisibilityObserver(ConversationPage owner) => _owner = owner;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(bool value) => _owner.SyncShown();
    }

    // ---- the transcript follows the bottom unless the reader scrolled up ----

    private void TurnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_followBottom) return;
        Dispatcher.UIThread.Post(() => Transcript.ScrollToEnd(), DispatcherPriority.Normal);
    }

    private void Transcript_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.ExtentDelta.Y) < .1)
            _followBottom = Transcript.Offset.Y >= Transcript.Extent.Height - Transcript.Viewport.Height - 24;
    }

    private void VmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConversationPageVm.StatusColor) or nameof(ConversationPageVm.Status)) PaintStatus();
    }

    private void PaintStatus()
    {
        if (_vm != null && Color.TryParse(_vm.StatusColor, out var c)) TxtStatus.Foreground = new SolidColorBrush(c);
    }

    // ---- composer ----

    private async void Send_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        _followBottom = true;
        await _vm.SendAsync();
        if (IsEffectivelyVisible) Composer.Focus();
    }

    private void Composer_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        Send_Click(sender, e);
    }

    private void Starter_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || _vm.Busy || sender is not Button { Content: TextBlock label }) return;
        _vm.Draft = label.Text ?? string.Empty;
        Composer.Focus();
    }

    private void Start_Click(object? sender, RoutedEventArgs e) { _vm?.Start(); Composer.Focus(); }

    private void Stop_Click(object? sender, RoutedEventArgs e) => _vm?.Stop();

    private async void New_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return;
        if (await Dialogs.MessageDialog.ConfirmAsync(owner, Loc.Get("companion_v2_new"), Loc.Get("companion_v2_history_note")))
            _vm.NewChat();
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text }) return;
        try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clip) await clip.SetTextAsync(text); }
        catch (Exception ex) { Log.Debug("Companion copy failed: {Type}", ex.GetType().Name); }
    }

    private void Activity_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConversationAction action }) _vm?.OpenActivity(action);
    }

    private void AskChoice_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConversationAskChoice choice }) _vm?.AnswerAsk(choice);
    }

    private void Link_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConversationLine line }) _vm?.OpenLink(line);
    }

    private void FocusComposer()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(FocusComposer); return; }
        if (IsEffectivelyVisible) Composer.Focus();
        else AvatarTubeWindow.Live?.OpenChatInput();
    }

    // ---- the sheets are pages (Companion section) ----

    private void Sheet_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string kind }) OpenSheet(kind);
    }

    /// <summary>The page a sheet kind lives on now (WPF OpenSheet's first line).</summary>
    internal static string? PageFor(string kind) => kind switch
    {
        "who" or "personality" => "personality",
        "permissions" => "permissions",
        "videos" => "companionlinks",
        "memory" or "connection" => "companionai",
        _ => null,
    };

    internal void OpenSheet(string kind)
    {
        if (PageFor(kind) is { } page && Shell is { } host) host.ShowTab(page);
    }
}
