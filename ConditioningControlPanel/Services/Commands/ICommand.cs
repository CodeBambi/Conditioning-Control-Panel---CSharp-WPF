using System.Threading.Tasks;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands
{
    public interface ICommand
    {
        /// <summary>
        /// Executes the command and returns a typed result describing whether it was
        /// executed, rejected, failed, or was a no-op.
        /// </summary>
        Task<CommandResult> ExecuteAsync();
    }
}
