using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Polymorphic factory: turns a parsed <see cref="AiCommandData"/> into the matching
    /// <see cref="ICommand"/> executor. <c>getbacktome</c> commands carry a recursion depth
    /// so the dispatcher can enforce a max nesting cap.
    /// </summary>
    public static class CommandFactory
    {
        // Hard cap on getbacktome recursion. Two means: AI can schedule a follow-up, and
        // that follow-up can include another scheduled follow-up — but no deeper.
        public const int MaxGetBackToMeDepth = 2;

        public static ICommand? CreateCommand(AiCommandData commandData, CancellationToken cancellationToken = default, int depth = 0)
        {
            if (commandData.Data == null) return null;

            return commandData.Command switch
            {
                AICommandType.flash_image => new FlashImageCommand((FlashImage)commandData.Data),
                AICommandType.bubbles => new BubbleCommand((Bubbles)commandData.Data),
                AICommandType.video => new MediaCommand((Media)commandData.Data),
                AICommandType.audio => new MediaCommand((Media)commandData.Data),
                AICommandType.getbacktome => new GetBackToMeCommand((GetBackToMe)commandData.Data, cancellationToken, depth),
                AICommandType.mantra_lockscreen => new MantraLockScreenCommand((MantraLockscreen)commandData.Data),
                AICommandType.pink => new PinkCommand((SpiralPinkFiler)commandData.Data),
                AICommandType.spiral => new SpiralCommand((SpiralPinkFiler)commandData.Data),
                AICommandType.subliminal => new SubliminalCommand((Subliminal)commandData.Data),
                AICommandType.bounce => new BounceCommand((Bounce)commandData.Data),
                AICommandType.haptic => new HapticCommand((HapticCommandData)commandData.Data),
                _ => null
            };
        }
    }
}
