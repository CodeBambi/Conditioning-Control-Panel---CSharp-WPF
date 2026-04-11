using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public interface ICommand
{
    public bool Execute();
}