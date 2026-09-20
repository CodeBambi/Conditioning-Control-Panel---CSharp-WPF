using System.Windows;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>The UI half of the host seam: hands the lifecycle its window.</summary>
public static partial class LauncherHost
{
    static partial void CreateWindow(ref Window? window)
    {
        window = new ConditioningControlPanel.Launcher.LauncherWindow();
    }
}
