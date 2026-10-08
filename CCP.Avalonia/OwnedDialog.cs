using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// <c>ShowDialog</c> that survives a hidden owner. Avalonia throws "Cannot show window with
    /// non-visible owner" when the shell sits in the tray or behind the launcher; WPF shows the
    /// dialog anyway. With no visible owner the dialog opens ownerless and the task still yields
    /// the value passed to <c>Close(result)</c>. In the parent namespace, so every view sees it
    /// without a using.
    /// </summary>
    internal static class OwnedDialog
    {
        // Close(object) stores the result here; ShowDialog reads it back on Closed. No public getter.
        private static readonly FieldInfo? DialogResult =
            typeof(Window).GetField("_dialogResult", BindingFlags.Instance | BindingFlags.NonPublic);

        public static Task<T> ShowDialogSafe<T>(this Window dialog, Window? owner)
        {
            if (owner is { IsVisible: true }) return dialog.ShowDialog<T>(owner);
            var done = new TaskCompletionSource<T>();
            dialog.Closed += (_, _) =>
                done.TrySetResult(DialogResult?.GetValue(dialog) is T r ? r : default!);
            dialog.Show();
            return done.Task;
        }

        public static Task ShowDialogSafe(this Window dialog, Window? owner) => dialog.ShowDialogSafe<object?>(owner);
    }
}
