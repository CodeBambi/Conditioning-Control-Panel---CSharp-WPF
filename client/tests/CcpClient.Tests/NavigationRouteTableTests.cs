using CcpClient.Desktop.Navigation;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-091 guards on the declared rail. These are deliberately NOT the packet's proof that
/// navigation works — driving a gesture to a destination is, and that lives in
/// <c>NavigationShellHeadlessTests</c>. What lives here is the pure-logic half no rendered
/// frame can express: which doors may exist at all, and what the router does with an id that
/// does not name one.
/// </summary>
public class NavigationRouteTableTests
{
    [Fact]
    public void DeclaredDoors_HaveUniqueStableIds_AndNoDtrhDoorExists()
    {
        var declared = ShellRoutes.Declared;
        Assert.NotEmpty(declared);

        // Ids are dispatch identity: stable, lowercase, never a display string. A door whose id
        // drifts with its label is the SP-014 failure re-created one layer up.
        Assert.Equal(declared.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count(), declared.Count);
        foreach (var route in declared)
        {
            Assert.False(string.IsNullOrWhiteSpace(route.Id));
            Assert.Equal(route.Id.ToLowerInvariant(), route.Id);
            Assert.NotEqual(route.Id, route.Label);
            Assert.False(string.IsNullOrWhiteSpace(route.Label));
            Assert.False(string.IsNullOrWhiteSpace(route.Tooltip));
        }

        // The SP-092 boundary, made mechanical. DTRH is Tier-2 gated in WPF
        // (MainWindow.Lab.cs:228,313) and the port has no entitlement service, so an ungated
        // DTRH door would hand out paid content. It may not be added by a quiet edit; it is
        // added by SP-092, with its gate, and this assertion is what it must change.
        foreach (var text in declared.SelectMany(r => new[] { r.Id, r.Label, r.Tooltip }))
        {
            Assert.DoesNotContain("dtrh", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rabbit", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(declared, r => r.Id == ShellRoutes.Default); // the shell can always open
    }

    [Fact]
    public void Navigate_UnknownOrNullOrCurrentId_ChangesNothing_AndReturnsFalse()
    {
        // WPF `else return` posture (MainWindow.Presets.cs:818, the precedent the port's
        // quick-toggle dispatch already follows): an id that names no door must leave the shell
        // exactly where it was, never on a blank page.
        var router = new ShellRouter(ShellRoutes.Declared, ShellRoutes.Default);
        var navigations = 0;
        router.Navigated += _ => navigations++;

        Assert.False(router.Navigate(null));
        Assert.False(router.Navigate(""));
        Assert.False(router.Navigate("no-such-door"));
        Assert.False(router.Navigate(ShellRoutes.Default.ToUpperInvariant())); // ids are ordinal
        Assert.False(router.Navigate(ShellRoutes.Default));                    // already showing
        Assert.Equal(ShellRoutes.Default, router.Current.Id);
        Assert.Equal(0, navigations);

        // The positive control: a declared id moves and raises exactly once.
        var other = ShellRoutes.Declared.First(r => r.Id != ShellRoutes.Default).Id;
        Assert.True(router.Navigate(other));
        Assert.Equal(other, router.Current.Id);
        Assert.Equal(1, navigations);
    }

    [Fact]
    public void ARailWithADoorThatHasNoPage_OrAPageNoDoorReaches_IsRefused()
    {
        // The anti-decoration invariant the whole packet turns on, in both directions. It runs
        // at composition in MainWindow, so a decorative door cannot reach a user.
        var declared = ShellRoutes.Declared.Select(r => r.Id).ToArray();

        ShellRouteBinding.ValidateOrThrow(declared, declared); // the honest rail is accepted

        var doorWithNoPage = Assert.Throws<InvalidOperationException>(
            () => ShellRouteBinding.ValidateOrThrow(declared.Append("play"), declared));
        Assert.Contains("play", doorWithNoPage.Message, StringComparison.Ordinal);
        Assert.Contains("no mounted page", doorWithNoPage.Message, StringComparison.Ordinal);

        var pageWithNoDoor = Assert.Throws<InvalidOperationException>(
            () => ShellRouteBinding.ValidateOrThrow(declared, declared.Append("orphan")));
        Assert.Contains("orphan", pageWithNoDoor.Message, StringComparison.Ordinal);
        Assert.Contains("no door reaches", pageWithNoDoor.Message, StringComparison.Ordinal);

        // A duplicate id is refused too: two doors sharing an id is one door that cannot be
        // addressed and one that is unreachable.
        Assert.Throws<ArgumentException>(
            () => new ShellRouter([.. ShellRoutes.Declared, ShellRoutes.Declared[0]], ShellRoutes.Default));
    }
}
