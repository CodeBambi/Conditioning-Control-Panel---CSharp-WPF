using System.Threading;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class GetBackToMeCommand(GetBackToMe commandData, CancellationToken cancellationToken) : ICommand
{
    public bool Execute()
    {
        var delay = Math.Min(commandData.Delay, 5);
        Task.Delay(delay * 1000, cancellationToken).ContinueWith(async task =>
        {
            if (task.IsCanceled) return;
            
            try
            {
                await SendTokenMessage(commandData.Token, commandData.JsonOnly);
                if (commandData.Commands != null)
                {
                    foreach (var subCommand in commandData.Commands)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        CommandFactory.CreateCommand(subCommand, cancellationToken)?.Execute();
                    }
                }
            }
            finally
            {
                AiCommandService.RemoveToken(commandData.Token);
            }
        }, cancellationToken);
        return true;
    }
    
    private async Task SendTokenMessage(string token, bool jsonOnly = false)
    {
        Console.WriteLine($"Sending token: {token}");
        await App.Ai.GetBambiReplyAsync($"[Token={token}, JsonOnly={jsonOnly}]");
    }
}