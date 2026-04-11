using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class AiCommandService : IAiCommandService
{
    private static readonly Dictionary<string, CancellationTokenSource> TokenCancellationSources = new();

    public void ExecuteCommand(AiCommandData commandData)
    {
        if (commandData.Data == null) return;

        if (commandData.Command == AICommandType.getbacktome && commandData.Data is GetBackToMe getBackToMe)
        {
            if (getBackToMe.Stop)
            {
                CancelToken(getBackToMe.Token);
                return;
            }

            // Cancel any existing task with the same token
            CancelToken(getBackToMe.Token);

            var cts = new CancellationTokenSource();
            TokenCancellationSources[getBackToMe.Token] = cts;

            var command = CommandFactory.CreateCommand(commandData, cts.Token);
            command?.Execute();
            return;
        }

        CommandFactory.CreateCommand(commandData)?.Execute();
    }

    public void CancelAllCommands()
    {
        var tokens = new List<string>(TokenCancellationSources.Keys);
        foreach (var token in tokens)
        {
            CancelToken(token);
        }
    }

    private void CancelToken(string token)
    {
        if (TokenCancellationSources.TryGetValue(token, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
                TokenCancellationSources.Remove(token);
            }
        }
    }

    public static void RemoveToken(string token)
    {
        if (TokenCancellationSources.TryGetValue(token, out var cts))
        {
            cts.Dispose();
            TokenCancellationSources.Remove(token);
        }
    }
}