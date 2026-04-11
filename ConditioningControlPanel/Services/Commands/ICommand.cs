using System.Threading.Tasks;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public interface ICommand
{
    public Task<bool> ExecuteAsync();
}