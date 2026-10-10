using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// WPF 7.1.5 Services/JustDrop/CreditedOrders.cs: the once-per-order-ever ledger behind the drop
    /// XP grant. A file in the user-data folder, not a process-lifetime set: a restart must not make
    /// an already-paid drop payable again. Capped at 200 codes, oldest out first.
    /// </summary>
    internal static class CreditedOrders
    {
        private const int MaxEntries = 200;

        private static readonly object Gate = new();
        private static List<string>? _codes;

        /// <summary>Test seam: the ledger file (default: justdrop_credited.json under UserData).</summary>
        internal static string? FilePathOverride
        {
            get => _filePathOverride;
            set { lock (Gate) { _filePathOverride = value; _codes = null; } }
        }
        private static string? _filePathOverride;

        private static string FilePath => _filePathOverride ?? Path.Combine(CorePaths.UserData, "justdrop_credited.json");

        /// <summary>True the first time a code is seen (and it is written down); false ever after.</summary>
        public static bool TryCredit(string orderCode)
        {
            if (string.IsNullOrWhiteSpace(orderCode)) return false;
            var code = orderCode.Trim();
            lock (Gate)
            {
                var codes = Load();
                if (codes.Contains(code, StringComparer.Ordinal)) return false;

                codes.Add(code);
                if (codes.Count > MaxEntries) codes.RemoveRange(0, codes.Count - MaxEntries);
                Save(codes);
                return true;
            }
        }

        public static bool IsCredited(string? orderCode)
        {
            if (string.IsNullOrWhiteSpace(orderCode)) return false;
            lock (Gate) return Load().Contains(orderCode.Trim(), StringComparer.Ordinal);
        }

        private static List<string> Load()
        {
            if (_codes != null) return _codes;
            try
            {
                var path = FilePath;
                if (File.Exists(path))
                {
                    var parsed = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path));
                    if (parsed != null)
                    {
                        _codes = parsed.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
                        return _codes;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "JustDrop: credited-orders ledger unreadable; starting empty");
            }
            _codes = new List<string>();
            return _codes;
        }

        private static void Save(List<string> codes)
        {
            _codes = codes;
            try
            {
                var path = FilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(codes));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "JustDrop: could not persist the credited-orders ledger");
            }
        }
    }
}
