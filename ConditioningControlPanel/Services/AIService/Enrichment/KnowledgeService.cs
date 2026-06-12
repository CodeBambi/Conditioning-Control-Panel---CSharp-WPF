using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ConditioningControlPanel.Models.AiEnrichment;

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
                    App.Logger?.Information("KnowledgeService: Loaded {Count} entries from {FilePath}", _context.Count, filePath);
                    return;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "KnowledgeService: Error loading {FilePath}, falling back", filePath);
                }
            }

            var projectAssetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "assets", fileName);
            if (File.Exists(projectAssetsPath))
            {
                try
                {
                    var json = File.ReadAllText(projectAssetsPath);
                    _context = JsonSerializer.Deserialize<List<Knowledge>>(json, options) ?? new();
                    App.Logger?.Information("KnowledgeService: Loaded {Count} entries from project assets {FilePath}", _context.Count, projectAssetsPath);
                    return;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "KnowledgeService: Error loading project assets {FilePath}", projectAssetsPath);
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
                    App.Logger?.Information("KnowledgeService: Loaded {Count} entries from embedded resource", _context.Count);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "KnowledgeService: Error loading embedded resource");
                }
            }
            else
            {
                App.Logger?.Debug("KnowledgeService: No knowledge.json found — using empty knowledge base");
            }
        }

        /// <summary>
        /// Returns knowledge entries relevant to the given keyword, capped to
        /// <paramref name="maxResults"/>. When <paramref name="keyword"/> is empty,
        /// the first <paramref name="maxResults"/> entries are returned. If there
        /// are fewer keyword matches than <paramref name="maxResults"/>, the rest
        /// are filled with the remaining entries so the model always has some context.
        /// </summary>
        public List<Knowledge> GetKnowledge(string keyword, int maxResults = 20)
        {
            if (maxResults <= 0)
                return new List<Knowledge>();

            if (string.IsNullOrWhiteSpace(keyword))
                return _context.Take(maxResults).ToList();

            var normalizedKeyword = keyword.Trim();
            var matches = _context.Where(k => MatchesKeyword(k, normalizedKeyword)).ToList();

            if (matches.Count >= maxResults)
                return matches.Take(maxResults).ToList();

            // Pad with non-matching entries so the prompt doesn't collapse to nothing.
            var matchingIds = new HashSet<Knowledge>(matches);
            var remainder = _context.Where(k => !matchingIds.Contains(k)).Take(maxResults - matches.Count);
            matches.AddRange(remainder);
            return matches;
        }

        private static bool MatchesKeyword(Knowledge k, string keyword)
        {
            return k.Files.Any(f =>
                Contains(f.Title, keyword) ||
                Contains(f.FileName, keyword) ||
                Contains(f.FileType, keyword) ||
                f.Triggers.Any(t => Contains(t, keyword)) ||
                f.Links.Any(l => Contains(l, keyword)) ||
                f.LocalPaths.Any(p => Contains(p, keyword)))
                || k.Triggers.Any(t =>
                    Contains(t.Trigger, keyword) ||
                    Contains(t.Description, keyword))
                || k.Kinks.Any(kk =>
                    Contains(kk.Name, keyword) ||
                    Contains(kk.Description, keyword));
        }

        private static bool Contains(string? text, string keyword)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
    }
}
