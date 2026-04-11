using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class GetBackToMeCommand(GetBackToMe commandData, CancellationToken cancellationToken) : ICommand
{
    public async Task<bool> ExecuteAsync()
    {
        var delay = Math.Max(commandData.Delay, 0);
        try
        {
            await Task.Delay(delay * 1000, cancellationToken);
            
            await SendTokenMessage(commandData.Token, commandData.JsonOnly);
            
            if (commandData.Commands != null)
            {
                foreach (var subCommand in commandData.Commands)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    var cmd = CommandFactory.CreateCommand(subCommand, cancellationToken);
                    if (cmd != null)
                    {
                        await cmd.ExecuteAsync();
                    }
                }
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
    
    private async Task SendTokenMessage(string token, bool jsonOnly = false)
    {
        Console.WriteLine($"Sending token: {token}");
        await App.Ai.GetBambiReplyAsync($"[Token={token}, JsonOnly={jsonOnly}]");
    }
}