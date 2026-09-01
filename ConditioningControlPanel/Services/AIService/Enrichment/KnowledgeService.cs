using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ConditioningControlPanel.Models.AiEnrichment;
using Serilog;

namespace ConditioningControlPanel.Services.AIService.Enrichment
{
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
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", fileName);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    _context = JsonSerializer.Deserialize<List<Knowledge>>(json, options) ?? new();
                    Log.Information("KnowledgeService: Loaded {Count} entries from {FilePath}", _context.Count, filePath);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "KnowledgeService: Error loading {FilePath}, falling back", filePath);
                }
            }

            var projectAssetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "assets", fileName);
            if (File.Exists(projectAssetsPath))
            {
                try
                {
                    var json = File.ReadAllText(projectAssetsPath);
                    _context = JsonSerializer.Deserialize<List<Knowledge>>(json, options) ?? new();
                    Log.Information("KnowledgeService: Loaded {Count} entries from project assets {FilePath}", _context.Count, projectAssetsPath);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "KnowledgeService: Error loading project assets {FilePath}", projectAssetsPath);
                }
            }

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
                    Log.Information("KnowledgeService: Loaded {Count} entries from embedded resource", _context.Count);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "KnowledgeService: Error loading embedded resource");
                }
            }
            else
            {
                Log.Debug("KnowledgeService: No knowledge.json found — using empty knowledge base");
            }
        }

        public List<Knowledge> GetKnowledge(string keyword)
        {
            return _context.ToList();
        }
    }
}
