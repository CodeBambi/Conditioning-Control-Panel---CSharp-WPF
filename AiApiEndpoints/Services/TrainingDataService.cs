using System.Text.Json;
using AiApiEndpoints.Enum;
using AiApiEndpoints.Models;

namespace AiApiEndpoints.Services;

/// <summary>
/// Service for extracting and saving training data in OpenAI JSONL format.
/// </summary>
public interface ITrainingDataService
{
    /// <summary>
    /// Processes a chat interaction and saves it to a training data file.
    /// </summary>
    /// <param name="requestMessages">The messages sent to the model.</param>
    /// <param name="assistantContent">The full content of the assistant's response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessAndSaveTrainingDataAsync(List<MessageDto> requestMessages, string assistantContent, CancellationToken cancellationToken);
}

public class TrainingDataService : ITrainingDataService
{
    private const string TrainingDirectoryName = "TrainingData";
    private const string TrainingFileName = "openai_training_data.jsonl";

    /// <inheritdoc />
    public async Task ProcessAndSaveTrainingDataAsync(List<MessageDto> requestMessages, string assistantContent, CancellationToken cancellationToken)
    {
        try
        {
            var messages = new List<object>();

            // 1) Add system role for manual prompt entry
            messages.Add(new { role = "system", content = Constants.SystemMessage });

            // 2) Process Request Messages (User + System/Context)
            foreach (var msg in requestMessages)
            {
                messages.Add(new { role = msg.Role, content = msg.Content });
            }

            // 3) Add Assistant response
            messages.Add(new { role = "assistant", content = assistantContent });

            var trainingExample = new { messages = messages };

            // Save training data to a file (JSONL format, one example per line)
            var trainingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TrainingDirectoryName);
            if (!Directory.Exists(trainingDirectory))
            {
                Directory.CreateDirectory(trainingDirectory);
            }

            var trainingFilePath = Path.Combine(trainingDirectory, TrainingFileName);
            
            // We want to save each training example on a single line for standard JSONL
            var singleLineTrainingJson = JsonSerializer.Serialize(trainingExample, new JsonSerializerOptions { WriteIndented = false });

            await System.IO.File.AppendAllTextAsync(trainingFilePath, singleLineTrainingJson + Environment.NewLine, cancellationToken);
            Console.WriteLine($"--- OPENAI TRAINING DATA SAVED TO {trainingFilePath} ---");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to save training data: {ex.Message}");
        }
    }
}
