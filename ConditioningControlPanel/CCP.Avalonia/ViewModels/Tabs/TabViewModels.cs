namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

// BlinkTrainerTabViewModel is defined in its own source file with ported WPF logic.
// RemoteControlTabViewModel is defined in RemoteControlTabViewModel.cs.

public sealed class BouncingTextTabViewModel : TabItemViewModel
{
    public BouncingTextTabViewModel() : base("bouncingtext", "Bouncing Text", "📝") { }
}

public sealed class BubbleCountTabViewModel : TabItemViewModel
{
    public BubbleCountTabViewModel() : base("bubblecount", "Bubble Count", "🫧") { }
}

public sealed class BubblePopTabViewModel : TabItemViewModel
{
    public BubblePopTabViewModel() : base("bubblepop", "Bubble Pop", "🫧") { }
}

public sealed class FlashTabViewModel : TabItemViewModel
{
    public FlashTabViewModel() : base("flash", "Flash", "⚡") { }
}

public sealed class PinkFilterTabViewModel : TabItemViewModel
{
    public PinkFilterTabViewModel() : base("pinkfilter", "Pink Filter", "💖") { }
}

public sealed class IntensityRampTabViewModel : TabItemViewModel
{
    public IntensityRampTabViewModel() : base("intensityramp", "Intensity Ramp", "📈") { }
}

public sealed class LockCardTabViewModel : TabItemViewModel
{
    public LockCardTabViewModel() : base("lockcard", "Lock Card", "🔐") { }
}

public sealed class MindWipeTabViewModel : TabItemViewModel
{
    public MindWipeTabViewModel() : base("mindwipe", "Mind Wipe", "🧠") { }
}

public sealed class SchedulerTabViewModel : TabItemViewModel
{
    public SchedulerTabViewModel() : base("scheduler", "Scheduler", "🗓") { }
}

public sealed class VisualsTabViewModel : TabItemViewModel
{
    public VisualsTabViewModel() : base("visuals", "Visuals", "👁", TabCapabilityRequirements.Overlays) { }
}

public sealed class SubliminalTabViewModel : TabItemViewModel
{
    public SubliminalTabViewModel() : base("subliminal", "Subliminal", "💬") { }
}

public sealed class SystemTabViewModel : TabItemViewModel
{
    public SystemTabViewModel() : base("system", "System", "⚙️", TabCapabilityRequirements.SystemTray) { }
}

public sealed class SpiralTabViewModel : TabItemViewModel
{
    public SpiralTabViewModel() : base("spiral", "Spiral", "🌀") { }
}

public sealed class SchedulerRampTabViewModel : TabItemViewModel
{
    public SchedulerRampTabViewModel() : base("schedulerramp", "Scheduler+", "📅") { }
}

public sealed class VideoTabViewModel : TabItemViewModel
{
    public VideoTabViewModel() : base("video", "Video", "🎬") { }
}

public sealed class WebcamTabViewModel : TabItemViewModel
{
    public WebcamTabViewModel() : base("webcam", "Webcam", "📷", TabCapabilityRequirements.ScreenCapture) { }
}
