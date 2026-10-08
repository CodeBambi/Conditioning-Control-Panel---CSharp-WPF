using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// <c>ShowDialog</c> that survives a hidden owner. Avalonia throws "Cannot show window with
    /// non-visible owner" when the shell sits in the tray or behind the launcher; WPF shows the
    /// dialog anyway, modal against every window. So a hidden owner is swapped for any visible
    /// window (the launcher) and the dialog stays modal; with none visible it opens ownerless and
    /// the task still yields the value passed to <c>Close(result)</c>. In the parent namespace,
    /// so every view sees it without a using.
    /// </summary>
    internal static class OwnedDialog
    {
        // Close(object) stores the result here; ShowDialog reads it back on Closed. No public getter.
        private static readonly FieldInfo? DialogResult =
            typeof(Window).GetField("_dialogResult", BindingFlags.Instance | BindingFlags.NonPublic);

        public static Task<T> ShowDialogSafe<T>(this Window dialog, Window? owner)
        {
            if (owner is not { IsVisible: true })
                owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                    .Windows.FirstOrDefault(w => w.IsVisible && w != dialog);
            if (owner != null) return dialog.ShowDialog<T>(owner);
            var done = new TaskCompletionSource<T>();
            dialog.Closed += (_, _) =>
                done.TrySetResult(DialogResult?.GetValue(dialog) is T r ? r : default!);
            dialog.Show();
            return done.Task;
        }

        public static Task ShowDialogSafe(this Window dialog, Window? owner) => dialog.ShowDialogSafe<object?>(owner);
    }
}
