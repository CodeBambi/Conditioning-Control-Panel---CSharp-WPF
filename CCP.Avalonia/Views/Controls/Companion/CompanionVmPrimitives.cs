using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    // PORTED (the used slice) from ConditioningControlPanel/Views/Controls/Companion/CompanionVmPrimitives.cs.
    // The WPF version leans on CommandManager.RequerySuggested; Avalonia has none, so
    // RaiseCanExecuteChanged is explicit. Plain net8.0 - folding into CCP.Core deletes this copy.

    /// <summary>Minimal ICommand. Port of CompanionRelayCommand's used surface.</summary>
    public sealed class CompanionRelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public CompanionRelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged;

        /// <summary>No CommandManager on Avalonia: whoever changes the answer says so.</summary>
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public abstract class CompanionObservable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
