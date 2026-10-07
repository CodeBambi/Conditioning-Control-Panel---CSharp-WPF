using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            // The embedded-resource fallback is gone: knowledge.json ships as Content, never embedded (WPF csproj).
            Log.Debug("KnowledgeService: No knowledge.json found — using empty knowledge base");
        }

        public List<Knowledge> GetKnowledge(string keyword)
        {
            return _context.ToList();
        }
    }
}
