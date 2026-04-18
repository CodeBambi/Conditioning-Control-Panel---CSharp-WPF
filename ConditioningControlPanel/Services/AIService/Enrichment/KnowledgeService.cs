using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ConditioningControlPanel.Models.AiEnrichment;

namespace ConditioningControlPanel.Services.AIService.Enrichment;

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
        // Check in assets folder relative to base directory
        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", fileName);
        
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
                App.Logger?.Information("KnowledgeService: Loaded {Count} facts from {FilePath}", _context.Count, filePath);
                return;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "KnowledgeService: Error loading {FilePath}, falling back", filePath);
            }
        }

        // Fallback to searching in assets folder if not in bin
        var projectAssetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "assets", fileName);
        if (System.IO.File.Exists(projectAssetsPath))
        {
             try
             {
                 var json = System.IO.File.ReadAllText(projectAssetsPath);
                 _context = JsonSerializer.Deserialize<List<Knowledge>>(json, options) ?? new();
                 App.Logger?.Information("KnowledgeService: Loaded {Count} facts from project assets {FilePath}", _context.Count, projectAssetsPath);
                 return;
             }
             catch (Exception ex) 
             {
                 App.Logger?.Warning(ex, "KnowledgeService: Error loading project assets {FilePath}", projectAssetsPath);
             }
        }
        
        // As a last resort, try embedded resource (we'll need to add it to csproj)
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "ConditioningControlPanel.assets.knowledge.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            try
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                _context = JsonSerializer.Deserialize<List<Knowledge>>(json, options) ?? new();
                App.Logger?.Information("KnowledgeService: Loaded {Count} facts from embedded resource", _context.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "KnowledgeService: Error loading embedded resource");
            }
        }
        else
        {
            App.Logger?.Warning("KnowledgeService: Could not find any knowledge source!");
        }
    }

    public List<Knowledge> GetKnowledge(string keyword)
    {
        return _context.ToList();
    }
}
