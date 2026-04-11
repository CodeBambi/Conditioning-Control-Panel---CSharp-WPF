using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Commands;

public interface IAiCommandService
{
    public void ExecuteCommand(AiCommandData commandData);
    public void CancelAllCommands();
}