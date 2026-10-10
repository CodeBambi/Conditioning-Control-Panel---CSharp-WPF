using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/Pages/AiPage.xaml.cs (48f587200,
    /// 9db154ab6). Companion > AI hosts the room's LIVE EngineRoomDrawer ("EngineZone", expanded), the
    /// Workshop Behaviour + Triggers cells and MemoryDiaryView ("MemoryZone"), never copies, so every
    /// handler wired to them keeps meaning the one control on screen (WPF CompanionPageHost.Adopt).
    /// <para>This head has no Companion V2, so its room is not collapsed: it still shows those zones on
    /// the Chat page. The page therefore hands each one back when it is hidden (WPF never needs to).</para>
    /// </summary>
    public partial class AiPage : UserControl
    {
        private readonly List<Action> _giveBack = new();

        public AiPage()
        {
            InitializeComponent();
        }

        /// <summary>WPF AiPage.OnShown, through ShowTab("companionai") -> OnTabShown.</summary>
        internal void OnShown(CompanionRoomView? room)
        {
            if (room is null || _giveBack.Count > 0) return;
            var workshop = room.FindControl<WorkshopAccordion>("WorkshopZone")?.DataContext as WorkshopRuntimeVm;
            if (room.FindControl<EngineRoomDrawer>("EngineZone") is { } engine)
            {
                if (engine.DataContext is EngineRoomVm vm) vm.IsExpanded = true;
                Borrow(engine, EngineHost);
            }
            if (workshop is not null)
            {
                Borrow(workshop.Parts.Behavior, BehaviorHost);
                Borrow(workshop.Parts.Triggers, TriggersHost);
            }
            if (room.FindControl<MemoryDiaryView>("MemoryZone") is { } diary)
            {
                diary.ViewModel?.Sync();   // WPF tab.Vm.Sync + Brain.EnsureCurrentAccount (Sync does both here)
                Borrow(diary, MemoryHost);
            }
        }

        /// <summary>Hidden: every borrowed control goes back where it came from, in reverse order.
        /// Leaving the tree disarms the diary's forget confirm (its Unloaded), WPF OnHidden.</summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && !IsVisible) GiveBack();
        }

        internal void GiveBack()
        {
            for (var i = _giveBack.Count - 1; i >= 0; i--) _giveBack[i]();
            _giveBack.Clear();
        }

        private void Borrow(Control element, ContentControl host)
        {
            if (ReferenceEquals(host.Content, element)) return;
            Action? restore = null;
            if (element.Parent is Panel panel)
            {
                var index = panel.Children.IndexOf(element);
                panel.Children.RemoveAt(index);
                restore = () => panel.Children.Insert(Math.Min(index, panel.Children.Count), element);
            }
            else if (element.GetVisualParent() is ContentPresenter presenter)
            {
                // A Workshop cell sits in the accordion's templated presenter.
                presenter.Content = null;
                restore = () => presenter.Content = element;
            }
            else if (element.Parent is ContentControl owner)
            {
                owner.Content = null;
                restore = () => owner.Content = element;
            }
            host.Content = element;
            _giveBack.Add(() =>
            {
                if (ReferenceEquals(host.Content, element)) host.Content = null;
                restore?.Invoke();
            });
        }
    }
}
