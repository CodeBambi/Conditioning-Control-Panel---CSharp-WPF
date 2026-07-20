using Avalonia.Controls;
using CcpClient.Desktop.Features;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-013 Step 2: one-at-a-time manager transitions + focus-restoration seam
/// (W-04, MainWindow.Presets.cs:846-873 parity). Window-free via the IPopup seam.
/// </summary>
public class FeaturePopupManagerTests
{
    private sealed class FakePopup : FeaturePopupManager.IPopup
    {
        private readonly List<string> _order;
        private readonly string _name;

        public FakePopup(List<string> order, string name)
        {
            _order = order;
            _name = name;
        }

        public event EventHandler? Closed;

        public void ShowOwned(Window owner) => _order.Add($"show:{_name}");

        public void Close()
        {
            _order.Add($"close:{_name}");
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void SimulateUserClose()
        {
            _order.Add($"user-close:{_name}");
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Fixture
    {
        public readonly List<string> Order = new();
        public int RestoreCount;
        private int _next;

        public Fixture()
        {
            Manager = new FeaturePopupManager(null!, Next, () => RestoreCount++);
            FeaturePopupManager.IPopup Next()
            {
                var name = ((char)('a' + _next++)).ToString();
                return new FakePopup(Order, name);
            }
        }

        public FeaturePopupManager Manager { get; }
    }

    [Fact]
    public void Show_First_ShowsOwnedAndBecomesActive()
    {
        var fixture = new Fixture();
        var popup = fixture.Manager.Show();
        Assert.Equal(new[] { "show:a" }, fixture.Order);
        Assert.Same(popup, fixture.Manager.Active);
    }

    [Fact]
    public void Show_Second_ClosesExistingBeforeShowingNew()
    {
        var fixture = new Fixture();
        fixture.Manager.Show();
        var second = fixture.Manager.Show();
        // Presets.cs:852 close-existing-before-new, then show (Presets.cs:873).
        Assert.Equal(new[] { "show:a", "close:a", "show:b" }, fixture.Order);
        Assert.Same(second, fixture.Manager.Active);
        // W-04 restoration fires on the displaced popup's close too (WPF event order parity).
        Assert.Equal(1, fixture.RestoreCount);
    }

    [Fact]
    public void UserClose_ClearsActive_AndRestoresFocusOnce()
    {
        var fixture = new Fixture();
        var popup = (FakePopup)fixture.Manager.Show();
        popup.SimulateUserClose();
        Assert.Null(fixture.Manager.Active);
        Assert.Equal(1, fixture.RestoreCount);
    }

    [Fact]
    public void Restoration_FiresExactlyOncePerTrackedClose()
    {
        // The production seam is CreateFocusRestoration(owner) (Presets.cs:862-870:
        // un-minimize-if-minimized + Activate, guarded for shutdown). Pin the contract
        // shape: the seam runs exactly once per tracked close — never zero, never twice.
        var fixture = new Fixture();
        fixture.Manager.Show();
        fixture.Manager.Show(); // displaces a -> restore #1
        var second = (FakePopup)fixture.Manager.Active!;
        second.SimulateUserClose(); // -> restore #2
        Assert.Null(fixture.Manager.Active);
        Assert.Equal(2, fixture.RestoreCount);
    }
}
