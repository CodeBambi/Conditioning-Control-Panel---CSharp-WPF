using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Services.Commands;

public class AiCommandService : IAiCommandService
{
    private static readonly Dictionary<string, CancellationTokenSource> TokenCancellationSources = new();

    public async void ExecuteCommand(AiCommandData commandData)
    {
        if (commandData.Data == null) return;

        var token = commandData.Data.Token;
        CancellationTokenSource? cts = null;

        if (!string.IsNullOrEmpty(token))
        {
            CancelToken(token);
            cts = new CancellationTokenSource();
            TokenCancellationSources[token] = cts;
        }

        try
        {
            var command = CommandFactory.CreateCommand(commandData, cts?.Token ?? default);
            if (command != null)
            {
                await command.ExecuteAsync();
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(token))
            {
                RemoveToken(token);
            }
        }
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