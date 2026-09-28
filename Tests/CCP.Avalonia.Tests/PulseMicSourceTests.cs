using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The mic picker's Linux list: OS default first, real sources by name, no sink monitors
/// (a monitor is other apps' output, not a microphone).</summary>
public sealed class PulseMicSourceTests
{
    [Fact]
    public void Lists_default_then_real_sources_without_monitors()
    {
        const string pactl =
            "55\talsa_output.usb-BEACN.pro-output-0.monitor\tPipeWire\ts32le 2ch 48000Hz\tSUSPENDED\n" +
            "56\talsa_input.usb-BEACN.pro-input-0\tPipeWire\ts32le 2ch 48000Hz\tSUSPENDED\n" +
            "57\tbluez_input.headset\tPipeWire\ts16le 1ch 16000Hz\tIDLE\n";
        var devices = PulseMicSource.ParseSources(pactl);
        Assert.Equal(new[] { "System default", "alsa_input.usb-BEACN.pro-input-0", "bluez_input.headset" },
            devices.Select(d => d.Name));
        Assert.Equal(new[] { -1, 0, 1 }, devices.Select(d => d.Index));
    }
}
