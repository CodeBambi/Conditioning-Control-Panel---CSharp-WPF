using System.Reflection;
using System.Text.Json;
using AiApiEndpoints.Models;
using File = AiApiEndpoints.Models.File;

namespace AiApiEndpoints.Functions;

public class KnowledgeService
{
    private List<Knowledge> _context = new();

    public KnowledgeService()
    {
        LoadKnowledge();
    }

    private void LoadKnowledge()
    {
        const string fileName = "knowledge.json";
        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        if (System.IO.File.Exists(filePath))
        {
            try
            {
                var json = System.IO.File.ReadAllText(filePath);
                _context = JsonSerializer.Deserialize<List<Knowledge>>(json, options) ?? new();
                return;
            }
            catch (Exception)
            {
                // Fallback to embedded resource if the external file is invalid
            }
        }

        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "AiApiEndpoints.knowledge.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            _context = JsonSerializer.Deserialize<List<Knowledge>>(json, options) ?? new();
        }
    }

    public List<Knowledge> GetKnowlage(string keyword)
    {
        return _context.ToList();
    }
}