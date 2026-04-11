using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class CommandFactory
{
    private static readonly Dictionary<AICommandType, Func<IAiCommandData, CancellationToken, ICommand>> Registry = new()
    {
        { AICommandType.flash_image, (data, _) => new FlashImageCommand((FlashImage)data) },
        { AICommandType.bubbles, (data, _) => new BubbleCommand((Bubbles)data) },
        { AICommandType.video, (data, _) => new MediaCommand((Media)data) },
        { AICommandType.audio, (data, _) => new MediaCommand((Media)data) },
        { AICommandType.getbacktome, (data, ct) => new GetBackToMeCommand((GetBackToMe)data, ct) },
        { AICommandType.mantra_lockscreen, (data, _) => new MantraLockScreenCommand((MantraLockscreen)data) },
        { AICommandType.pink, (data, _) => new PinkCommand((SpiralPinkFiler)data) },
        { AICommandType.spiral, (data, _) => new SpiralCommand((SpiralPinkFiler)data) },
        { AICommandType.subliminal, (data, _) => new SubliminalCommand((Subliminal)data) },
        { AICommandType.bounce, (data, _) => new BounceCommand((Bounce)data) },
        { AICommandType.haptic, (data, _) => new HapticCommand((HapticCommandData)data) }
    };

    public static ICommand? CreateCommand(AiCommandData commandData, CancellationToken cancellationToken = default)
    {
        if (commandData.Data == null) return null;

        if (Registry.TryGetValue(commandData.Command, out var factory))
        {
            return factory(commandData.Data, cancellationToken);
        }

        return null;
    }

    public static void RegisterCommand(AICommandType type, Func<IAiCommandData, CancellationToken, ICommand> factory)
    {
        Registry[type] = factory;
    }
}