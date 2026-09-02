using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Commands
{
    public interface ICommand
    {
        Task<bool> ExecuteAsync();
    }
}
