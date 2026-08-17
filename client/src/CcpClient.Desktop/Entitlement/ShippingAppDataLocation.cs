namespace CcpClient.Desktop.Entitlement;

/// <summary>
/// Where the SHIPPING WPF app keeps its user data. This is deliberately not the port's own
/// data root (<c>Lifecycle/CompositionRoot.DefaultSettingsPath()</c>): the port reads one file
/// out of another application's directory and writes nothing there, ever.
///
/// <para>Ported from <c>App.xaml.cs:157-171</c>: <c>CCP_USERDATA_DIR</c> wins when it is set and
/// rooted (the shipping app's own sandbox hook), otherwise
/// <c>%LOCALAPPDATA%/ConditioningControlPanel</c>. Honouring the override matters for honesty
/// rather than convenience — if the user redirected the shipping app and the port looked only in
/// the default place, it would report "no token file", and while that is still an
/// <c>Unavailable</c> rather than a refusal, it would be the wrong <c>Unavailable</c>.</para>
/// </summary>
public static class ShippingAppDataLocation
{
    /// <summary>The shipping app's data-root redirect (App.xaml.cs:163).</summary>
    public const string OverrideVariable = "CCP_USERDATA_DIR";

    /// <summary>The folder name under LocalApplicationData (App.xaml.cs:168-170).</summary>
    public const string FolderName = "ConditioningControlPanel";

    /// <summary>
    /// The directory to look in. Returns a path whether or not it exists — existence is the
    /// reader's question, and answering it here would collapse "not installed" into "no path".
    /// </summary>
    public static string Resolve()
    {
        try
        {
            var overrideDir = Environment.GetEnvironmentVariable(OverrideVariable);
            if (!string.IsNullOrWhiteSpace(overrideDir) && Path.IsPathRooted(overrideDir))
            {
                return overrideDir;
            }
        }
        catch (Exception)
        {
            // App.xaml.cs:167 swallows the same way: an unreadable environment must fall back
            // to the default location, not fail the lookup.
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FolderName);
    }
}
