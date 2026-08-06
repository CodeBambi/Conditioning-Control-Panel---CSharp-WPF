using System;
using System.Windows;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// The one-line entry point MainWindow can call once it wants the preview harness reachable
    /// from a normal run:
    ///
    /// <code>
    /// ConditioningControlPanel.Views.Controls.Companion.CompanionRoomPreview.MaybeShow();
    /// </code>
    ///
    /// <para>Nothing calls it today — this package may not touch MainWindow, and the harness has no
    /// business opening in a user's app anyway. Until then the session driver uses
    /// <see cref="CompanionRoomPreviewWindow.Launch"/> directly.</para>
    ///
    /// <para><b>Why the check is here and not in a static constructor.</b> A type initializer that
    /// opens a window fires on first touch of the type — including from a unit test, on a thread
    /// with no dispatcher, in a process that then never exits. The env var is read when
    /// <see cref="MaybeShow"/> is called, by a caller that already knows it is on the UI thread, and
    /// that is the only moment anything happens.</para>
    /// </summary>
    public static class CompanionRoomPreview
    {
        /// <summary>Set this to "1" in the environment to arm <see cref="MaybeShow"/>.</summary>
        public const string EnvVarName = "CCP_CTAB_PREVIEW";

        /// <summary>True when the environment asks for the harness.</summary>
        public static bool IsRequested()
        {
            try
            {
                return string.Equals(Environment.GetEnvironmentVariable(EnvVarName), "1",
                                     StringComparison.Ordinal);
            }
            catch (Exception)
            {
                // A locked-down environment block is not a reason to fail startup.
                return false;
            }
        }

        /// <summary>
        /// Opens the harness when <c>CCP_CTAB_PREVIEW=1</c>, and returns whether it did. Safe to call
        /// from anywhere: off by default, never throws, and a failure to build the window is
        /// swallowed rather than taking the app's startup with it.
        ///
        /// <para><paramref name="variantKey"/> picks the opening page state; see
        /// <see cref="MockCompanionRoomVm.Variants"/>.</para>
        /// </summary>
        public static bool MaybeShow(string? variantKey = null)
        {
            if (!IsRequested()) return false;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                {
                    CompanionRoomPreviewWindow.Launch(variantKey);
                    return true;
                }

                dispatcher.Invoke(() => CompanionRoomPreviewWindow.Launch(variantKey));
                return true;
            }
            catch (Exception)
            {
                // A debug harness is never worth a crash on someone's machine.
                return false;
            }
        }
    }
}
