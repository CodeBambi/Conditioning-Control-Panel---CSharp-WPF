using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public interface IAiCommandService
    {
        /// <summary>Reset the per-AI-response command counter. Call before dispatching a new batch.</summary>
        void BeginBatch();

        /// <summary>Dispatch a single AI command. Subject to master/per-effect/cap gating.</summary>
        /// <returns>An outcome summary for the AI context, or null if the command was unparseable.</returns>
        CommandOutcome? ExecuteCommand(AiCommandData commandData);

        /// <summary>Cancel all in-flight token-tracked commands (e.g. pending getbacktome scheduled actions).</summary>
        void CancelAllCommands();
    }
}
