using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Commands
{
    public interface IAiCommandService
    {
        /// <summary>Reset the per-AI-response command counter. Call before dispatching a new batch.</summary>
        void BeginBatch();

        /// <summary>Dispatch a single AI command. Subject to master/per-effect/cap gating.</summary>
        void ExecuteCommand(AiCommandData commandData);

        /// <summary>Cancel all in-flight token-tracked commands (e.g. pending getbacktome scheduled actions).</summary>
        void CancelAllCommands();
    }
}
