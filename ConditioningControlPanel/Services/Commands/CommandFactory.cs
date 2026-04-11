using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class CommandFactory
{
    public static ICommand? CreateCommand(AiCommandData commandData, CancellationToken cancellationToken = default)
    {
        if (commandData.Data == null) return null;

        ICommand? command = commandData.Command switch
        {
            AICommandType.flash_image => new FlashImageCommand((FlashImage)commandData.Data),
            AICommandType.bubbles     => new BubbleCommand((Bubbles)commandData.Data),
            AICommandType.video    => new MediaCommand((Media)commandData.Data),
            AICommandType.audio    => new MediaCommand((Media)commandData.Data),
            AICommandType.getbacktome => new GetBackToMeCommand((GetBackToMe)commandData.Data, cancellationToken),
            AICommandType.mantra_lockscreen => new MantraLockScreenCommand((MantraLockscreen)commandData.Data),
            AICommandType.pink    => new PinkCommand((SpiralPinkFiler)commandData.Data),
            AICommandType.spiral    => new SpiralCommand((SpiralPinkFiler)commandData.Data),
            AICommandType.subliminal    => new SubliminalCommand((Subliminal)commandData.Data),
            AICommandType.bounce    => new BounceCommand((Bounce)commandData.Data),
            AICommandType.haptic    => new HapticCommand((HapticCommandData)commandData.Data),
            _ => null
        };
        return command;
    }
}