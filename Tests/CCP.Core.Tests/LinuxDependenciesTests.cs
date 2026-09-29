using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// docs/avalonia-linux-install.md: the missing-dependency message names the exact command per distro
// family (fake /etc/os-release), and only for libraries the (fake) probe cannot load.
public sealed class LinuxDependenciesTests
{
    private static bool NoVlcNoSecret(string lib) => lib is not ("libvlc.so.5" or "libsecret-1.so.0");

    [Theory]
    [InlineData("NAME=\"CachyOS Linux\"\nID=cachyos\nID_LIKE=arch\n", "sudo pacman -S --needed vlc libsecret")]
    [InlineData("ID=endeavouros\nID_LIKE=arch\n", "sudo pacman -S --needed vlc libsecret")]
    [InlineData("ID=ubuntu\nID_LIKE=debian\n", "sudo apt install libvlc5 vlc-plugin-base libsecret-1-0")]
    [InlineData("ID=linuxmint\nID_LIKE=\"ubuntu debian\"\n", "sudo apt install libvlc5 vlc-plugin-base libsecret-1-0")]
    [InlineData("ID=fedora\n", "sudo dnf install vlc-libs libsecret")]
    [InlineData("ID=\"rocky\"\nID_LIKE=\"rhel centos fedora\"\n", "sudo dnf install vlc-libs libsecret")]
    public void MessageNamesTheDistroCommandForWhatIsMissing(string osRelease, string command)
    {
        var msg = LinuxDependencies.Message(LinuxDependencies.Missing(NoVlcNoSecret), osRelease)!.Value;
        Assert.Equal(command, msg.Command);
        Assert.Contains(command, msg.Text);
        Assert.Contains("audio and video, sign-in token store", msg.Text);
    }

    [Fact]
    public void UnknownDistroFallsBackToLibraryNamesWithoutACommand()
    {
        var msg = LinuxDependencies.Message(LinuxDependencies.Missing(NoVlcNoSecret), "ID=opensuse-tumbleweed\nID_LIKE=\"opensuse suse\"\n")!.Value;
        Assert.Null(msg.Command);
        Assert.Contains("libvlc.so.5, libsecret-1.so.0", msg.Text);
        Assert.Null(LinuxDependencies.Message(LinuxDependencies.Missing(NoVlcNoSecret), null)!.Value.Command);
    }

    [Fact]
    public void WebKitEitherSonameSatisfiesWebViewsButWpeNeedsAllThree()
    {
        Assert.Empty(LinuxDependencies.Missing(_ => true));
        Assert.Null(LinuxDependencies.Message(LinuxDependencies.Missing(_ => true), "ID=arch"));
        // Only 4.0 loads: still enough for web views.
        Assert.Empty(LinuxDependencies.Missing(l => !l.StartsWith("libwebkit2gtk") || l == "libwebkit2gtk-4.0.so.37"));
        Assert.Equal(new[] { "web views" },
            LinuxDependencies.Missing(l => !l.StartsWith("libwebkit2gtk")).Select(d => d.Need));
        Assert.Equal(new[] { "web views (WPE)" },
            LinuxDependencies.Missing(l => l != "libWPEWebKit-2.0.so.1").Select(d => d.Need));
        Assert.Equal(new[] { "overlays and panic key" },
            LinuxDependencies.Missing(l => l != "libXi.so.6").Select(d => d.Need));
    }
}
