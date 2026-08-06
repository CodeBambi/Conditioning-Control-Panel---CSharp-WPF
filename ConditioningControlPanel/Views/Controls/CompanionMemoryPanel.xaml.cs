using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.ViewModels;

namespace ConditioningControlPanel.Views.Controls
{
    /// <summary>
    /// "What she knows about you" (AI rework Train 1, doc 01 §2.4) — the companion-memory trust
    /// surface hosted by the Companion tab.
    ///
    /// <para>The control owns a <see cref="CompanionMemoryViewModel"/> over
    /// <c>App.Brain?.Memory</c>. It binds to the <see cref="IMemoryStore"/> interface only, so it is
    /// indifferent to whether that store is the Train 1 in-memory shell or the real persisted store
    /// the memory branch adds later. A null brain (kill switch off, or init failed) renders the
    /// "memory is off" state rather than throwing.</para>
    ///
    /// <para>Reads are refreshed when the panel becomes visible: the store is written by services
    /// (and, later, by the extractor) with no change notification of its own, so "refresh on show"
    /// is the cheap correct sync point. Nothing here polls.</para>
    /// </summary>
    public partial class CompanionMemoryPanel : UserControl
    {
        private CompanionMemoryViewModel? _vm;

        public CompanionMemoryPanel()
        {
            InitializeComponent();
            Loaded += CompanionMemoryPanel_Loaded;
            IsVisibleChanged += CompanionMemoryPanel_IsVisibleChanged;
        }

        /// <summary>The bound view model. Created on first use so tests/designers never need a brain.</summary>
        internal CompanionMemoryViewModel Model => _vm ??= new CompanionMemoryViewModel(ResolveStore());

        private static IMemoryStore? ResolveStore()
        {
            try { return App.Brain?.Memory; }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "CompanionMemoryPanel: no memory store available");
                return null;
            }
        }

        private void CompanionMemoryPanel_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureBound();
        }

        private void CompanionMemoryPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible) return;
            EnsureBound();
            Refresh();
        }

        private void EnsureBound()
        {
            if (LayoutRoot.DataContext == null)
                LayoutRoot.DataContext = Model;
        }

        /// <summary>Re-reads the store. Safe to call from the Companion tab when it opens the section.</summary>
        internal void Refresh()
        {
            try { Model.Refresh(); }
            catch (Exception ex) { App.Logger?.Error(ex, "CompanionMemoryPanel: refresh failed"); }
        }

        // ---------- row actions ----------

        private static MemoryFactRowViewModel? RowOf(object sender)
            => (sender as FrameworkElement)?.DataContext as MemoryFactRowViewModel;

        private void BtnPinFact_Click(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null) return;
            try
            {
                Model.TogglePin(row);
                App.Bark?.NotifyUiAction(row.Pinned ? "memory_fact_pinned" : "memory_fact_unpinned");
            }
            catch (Exception ex) { App.Logger?.Error(ex, "CompanionMemoryPanel: pin toggle failed"); }
        }

        private void BtnEditFact_Click(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null) return;
            Model.BeginEdit(row);
        }

        private void BtnSaveFact_Click(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null) return;
            try
            {
                if (Model.CommitEdit(row))
                    App.Bark?.NotifyUiAction("memory_fact_edited");
            }
            catch (Exception ex) { App.Logger?.Error(ex, "CompanionMemoryPanel: fact edit failed"); }
        }

        private void BtnCancelFact_Click(object sender, RoutedEventArgs e)
        {
            Model.CancelEdit(RowOf(sender));
        }

        private void EditBox_KeyDown(object sender, KeyEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null) return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                try
                {
                    if (Model.CommitEdit(row))
                        App.Bark?.NotifyUiAction("memory_fact_edited");
                }
                catch (Exception ex) { App.Logger?.Error(ex, "CompanionMemoryPanel: fact edit failed"); }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Model.CancelEdit(row);
            }
        }

        private void BtnDeleteFact_Click(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null) return;
            try
            {
                Model.Delete(row);
                App.Bark?.NotifyUiAction("memory_fact_forgotten");
            }
            catch (Exception ex) { App.Logger?.Error(ex, "CompanionMemoryPanel: fact delete failed"); }
        }

        // ---------- wipe ----------

        private void BtnForgetEverything_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);

            // Destructive and irreversible, so it asks first and defaults to No.
            var confirm = owner != null
                ? MessageBox.Show(owner,
                    Loc.Get("companion_memory_forget_all_body"),
                    Loc.Get("companion_memory_forget_all_title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(
                    Loc.Get("companion_memory_forget_all_body"),
                    Loc.Get("companion_memory_forget_all_title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                Model.ForgetEverything();
                App.Bark?.NotifyUiAction("memory_wiped");
                App.Logger?.Information("Companion memory wiped from the \"what she knows\" panel");

                if (owner != null)
                    MessageBox.Show(owner,
                        Loc.Get("companion_memory_forget_all_done_body"),
                        Loc.Get("companion_memory_forget_all_done_title"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show(
                        Loc.Get("companion_memory_forget_all_done_body"),
                        Loc.Get("companion_memory_forget_all_done_title"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "CompanionMemoryPanel: wipe failed");
            }
        }
    }
}
