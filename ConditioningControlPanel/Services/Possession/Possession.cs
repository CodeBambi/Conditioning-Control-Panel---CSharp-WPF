using System.Windows;

namespace ConditioningControlPanel.Services.Possession;

/// <summary>
/// The XAML side of the target registry. A control opts in to being haunted by carrying
/// <c>poss:Possession.Role</c>; the host (MainWindow.Possession.cs) walks the visual tree and turns
/// every tagged, visible element into a <see cref="PossessionTarget"/>.
///
/// <para>Attached properties rather than a code-side list on purpose: the curated set of victims is a
/// DESIGN decision that belongs next to the markup it describes, and a control that is deleted or
/// renamed takes its tag with it instead of leaving a dangling x:Name lookup that silently stops
/// finding anything.</para>
///
/// <para>Usage:
/// <code>
/// xmlns:poss="clr-namespace:ConditioningControlPanel.Services.Possession"
/// ...
/// &lt;Button x:Name="BtnStart" poss:Possession.Role="Button" poss:Possession.Name="the Start button" /&gt;
/// </code>
/// </para>
/// </summary>
public static class Possession
{
    /// <summary>
    /// What this control IS, so effects can pick a fitting victim. <see cref="PossessionRole.None"/>
    /// (the default) means "never possess me".
    ///
    /// <para>Inherits is deliberately FALSE: tagging a card must not silently enrol every label,
    /// button and toggle inside it. Each victim is opted in by hand - that is the whole point of a
    /// curated set, and it is what keeps the timer's value, the secret exit box and the premium gate
    /// out of the deck without a blocklist.</para>
    /// </summary>
    public static readonly DependencyProperty RoleProperty =
        DependencyProperty.RegisterAttached(
            "Role",
            typeof(PossessionRole),
            typeof(Possession),
            new FrameworkPropertyMetadata(PossessionRole.None, FrameworkPropertyMetadataOptions.None));

    public static void SetRole(DependencyObject element, PossessionRole value)
        => element?.SetValue(RoleProperty, value);

    public static PossessionRole GetRole(DependencyObject element)
        => element == null ? PossessionRole.None : (PossessionRole)element.GetValue(RoleProperty);

    /// <summary>
    /// The friendly name the warden says out loud when it names a big effect ("oops, the Stop button
    /// moved"). Written the way a sentence needs it - lower case, with its article - because the bark
    /// lines drop it straight into a phrase.
    ///
    /// <para>Deliberately NOT a loc key: the warden speaks through the bark packs, which are already
    /// authored per mod and per language, and a half-translated fragment glued into a translated line
    /// reads worse than an untranslated one. If that changes, resolve the key HERE, not in the
    /// effects.</para>
    /// </summary>
    public static readonly DependencyProperty NameProperty =
        DependencyProperty.RegisterAttached(
            "Name",
            typeof(string),
            typeof(Possession),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.None));

    public static void SetName(DependencyObject element, string value)
        => element?.SetValue(NameProperty, value);

    public static string GetName(DependencyObject element)
        => element == null ? string.Empty : (element.GetValue(NameProperty) as string) ?? string.Empty;

    /// <summary>
    /// "Never a target, whatever the auto-tagger thinks." Wave 2 walks the live visual tree and infers
    /// a role from the control TYPE, which is exactly the right default for a room full of buttons and
    /// exactly the wrong one for the three controls a haunted user needs to stay trustworthy: the
    /// Emergency Exit button, the secret exit box and the lockdown badge. Those carry
    /// <c>poss:Possession.Exclude="True"</c> and are skipped before any inference runs.
    ///
    /// <para>Inherits is TRUE here (unlike Role): excluding a container has to exclude what is inside
    /// it, or a panel marked safe would still leak its own buttons into the deck.</para>
    /// </summary>
    public static readonly DependencyProperty ExcludeProperty =
        DependencyProperty.RegisterAttached(
            "Exclude",
            typeof(bool),
            typeof(Possession),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetExclude(DependencyObject element, bool value)
        => element?.SetValue(ExcludeProperty, value);

    public static bool GetExclude(DependencyObject element)
        => element != null && element.GetValue(ExcludeProperty) is bool b && b;
}
