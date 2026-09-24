using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Views.Controls.Companion.V2;

public partial class ConversationPage : UserControl
{
    private readonly ConversationPageVm _vm;
    private readonly CompanionRoomView _legacy;
    private bool _followBottom = true;
    private UIElement? _borrowed;
    private Panel? _parent;
    private int _index;
    private BindingBase? _oldBinding;
    private object? _oldContext;
    private IInputElement? _returnFocus;
    public Action<Window>? PersonalityEditor { get; set; }
    internal ConversationPage(CompanionRoomRuntimeVm room, CompanionRoomView legacy)
    {
        InitializeComponent();
        StageDust.StartLayers(new ConditioningControlPanel.Controls.AmbientFxConfig
        {
            Layers = ConditioningControlPanel.Controls.AmbientFxLayers.DustField,
            Intensity = 0.8, DustDensity = 0.65,
            Tint = System.Windows.Media.Color.FromRgb(128, 228, 197)
        });
        _legacy = legacy;
        _vm = new(room);
        DataContext = _vm;
        _vm.Turns.CollectionChanged += TurnsChanged;
        _vm.AccountChanged += CloseSheet;
        IsVisibleChanged += (_, _) => { if (IsVisible) _vm.Resume(); else { CloseSheet(); _vm.Detach(); } };
        Unloaded += (_, _) => { _vm.Stop(); CloseSheet(); _vm.Detach(); };
        SizeChanged += (_, _) => { if (SheetOverlay.Children[0] is FrameworkElement sheet) sheet.Width = Math.Max(240, Math.Min(580, ActualWidth - 48)); };
    }
    private void TurnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_followBottom) return;
        Dispatcher.BeginInvoke(new Action(() => Transcript.ScrollToEnd()), DispatcherPriority.Normal);
    }
    private void Transcript_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.ExtentHeightChange) < .1) _followBottom = Transcript.VerticalOffset >= Transcript.ScrollableHeight - 24;
    }
    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        _followBottom = true;
        await _vm.SendAsync();
        if (IsVisible) Composer.Focus();
    }
    private void Composer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || e.ImeProcessedKey != Key.None) return;
        e.Handled = true;
        Send_Click(sender, e);
    }
    private void Starter_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Busy || sender is not Button button) return;
        _vm.Draft = button.Content?.ToString() ?? string.Empty;
        Composer.Focus();
    }
    private void Start_Click(object sender, RoutedEventArgs e) { _vm.Start(); Composer.Focus(); }
    private void Stop_Click(object sender, RoutedEventArgs e) => _vm.Stop();
    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this), Loc.Get("companion_v2_history_note"), Loc.Get("companion_v2_new"), MessageBoxButton.OKCancel) == MessageBoxResult.OK) _vm.NewChat();
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text })
            try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.COMException) { App.Logger?.Debug("Companion copy deferred: clipboard is busy"); }
    }
    private void Sheet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string kind }) OpenSheet(kind);
    }
    internal void OpenSheet(string kind)
    {
        CloseSheet();
        _returnFocus = Keyboard.FocusedElement;
        App.Brain?.EnsureCurrentAccount();
        _vm.Room.Sync();
        string? source = kind switch { "memory" => "MemoryZone", "permissions" => "PermissionsZone", "connection" => "EngineZone", "personality" => "PersonalityZone", _ => null };
        SheetTitle.Text = Loc.Get(kind switch { "memory" => "companion_v2_memory", "permissions" => "companion_v2_allowed", "connection" => "companion_v2_connection", _ => "companion_v2_who" });
        if (kind == "personality" && Services.Companion.EmiPersonality.IsActive)
        {
            SheetContent.Content = new TextBlock { Text = Loc.Get("companion_v2_emi_fixed"), TextWrapping = TextWrapping.Wrap };
        }
        else if (source != null && _legacy.FindName(source) is FrameworkElement existing && existing.Parent is Panel parent)
        {
            _borrowed = existing;
            _oldBinding = BindingOperations.GetBindingBase(existing, DataContextProperty);
            _oldContext = existing.ReadLocalValue(DataContextProperty);
            _parent = parent;
            _index = parent.Children.IndexOf(existing);
            parent.Children.Remove(existing);
            // Preserve a binding's explicit source when its inherited room context changes.
            existing.DataContext = kind switch { "memory" => _vm.Room.Memory, "connection" => _vm.Room.Engine, "personality" => _vm.Room.Personality, _ => existing.DataContext };
            if (kind == "connection") _vm.Room.Engine.IsExpanded = true;
            var contents = new StackPanel();
            if (kind == "memory") contents.Children.Add(new TextBlock { Text = Loc.Get("companion_v2_memory_note"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            if (kind == "memory") { contents.Children.Add(new PreferredNameEditor()); contents.Children.Add(new ConversationRecap()); }
            contents.Children.Add(existing);
            SheetContent.Content = contents;
        }
        else if (kind == "who")
        {
            var contents = new StackPanel();
            contents.Children.Add(new CompanionPickerCard());
            var advanced = new Button { Content = Loc.Get("companion_v2_personality"), Margin = new Thickness(0, 14, 0, 0) };
            advanced.Click += (_, _) => { if (PersonalityEditor != null && Window.GetWindow(this) is Window owner) { PersonalityEditor(owner); _vm.Room.Sync(); _vm.Refresh(); } else OpenSheet("personality"); };
            var fixedVoice = new TextBlock { Text = Loc.Get("companion_v2_emi_fixed"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
            contents.Children.Add(fixedVoice);
            void SyncEditor()
            {
                var fixedEmi = Services.Companion.EmiPersonality.IsActive;
                advanced.Visibility = fixedEmi ? Visibility.Collapsed : Visibility.Visible;
                fixedVoice.Visibility = fixedEmi ? Visibility.Visible : Visibility.Collapsed;
            }
            SyncEditor();
            contents.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler((_, _) => Dispatcher.BeginInvoke(new Action(SyncEditor))));
            contents.Children.Add(advanced);
            SheetContent.Content = contents;
        }
        SheetOverlay.Visibility = Visibility.Visible;
        KeyboardNavigation.SetTabNavigation(SheetOverlay, KeyboardNavigationMode.Cycle);
        SheetOverlay.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
    private void Close_Click(object sender, RoutedEventArgs e) => CloseSheet();
    private void Page_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && SheetOverlay.Visibility == Visibility.Visible) { CloseSheet(); e.Handled = true; }
    }
    private void CloseSheet()
    {
        if (_legacy.FindName("MemoryZone") is MemoryDiaryView diary) diary.ForgetConfirm.Disarm();
        SheetOverlay.Visibility = Visibility.Collapsed;
        if (_borrowed != null && _parent != null)
        {
            if (_borrowed is FrameworkElement { Parent: Panel current }) current.Children.Remove(_borrowed);
            _parent.Children.Insert(Math.Min(_index, _parent.Children.Count), _borrowed);
            if (_borrowed is FrameworkElement restored)
            {
                if (_oldBinding != null) BindingOperations.SetBinding(restored, DataContextProperty, _oldBinding);
                else if (_oldContext == DependencyProperty.UnsetValue) restored.ClearValue(DataContextProperty);
                else restored.DataContext = _oldContext;
            }
        }
        SheetContent.Content = null;
        _borrowed = null;
        _parent = null;
        if (_returnFocus != null) Keyboard.Focus(_returnFocus);
        _returnFocus = null;
        _vm.Refresh();
    }
}
