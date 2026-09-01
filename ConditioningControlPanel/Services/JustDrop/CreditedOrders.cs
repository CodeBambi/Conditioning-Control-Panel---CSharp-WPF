using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// Which Just Drop orders have already been paid XP, so a replay never pays twice.
    ///
    /// <para>This is the whole reason the Takeaway shelf can offer a replay button at all. Without
    /// it, <c>session-complete</c> paid a flat grant every time it arrived, and one 8-minute drop
    /// replayed on a loop was unlimited XP. The web side draws the same line for Pocket Money
    /// (host-bridge.ts: replays and embedded plays never pay), so this is the desktop keeping to a
    /// contract that already existed rather than inventing a rule.</para>
    ///
    /// <para><b>Local, and deliberately so.</b> The server is the authority on Pocket Money; XP is
    /// the desktop's own currency and its ledger belongs beside the rest of the user data. That does
    /// mean a user who wipes their profile folder can be paid once more per order - which is a
    /// trade, not an oversight: the alternative is a new server endpoint and a network round trip on
    /// the completion path, where being offline would then cost an honest user the XP they earned.
    /// Failing OPEN on a read error follows from the same choice.</para>
    ///
    /// <para>Not thread-safe by lock but only ever touched from the UI thread, because the only
    /// caller is a CoreWebView2 message handler.</para>
    /// </summary>
    internal static class CreditedOrders
    {
        /// <summary>
        /// Hard ceiling on the ledger. The server keeps 30 orders; this keeps more than that so a
        /// deleted-then-recovered receipt cannot become newly payable, but not without bound. When
        /// it overflows the OLDEST entries go, which is the safe direction: the codes most likely to
        /// be replayed are the ones most recently played.
        /// </summary>
        private const int MaxEntries = 200;

        private static List<string>? _codes;

        private static string FilePath => Path.Combine(CorePaths.UserData, "justdrop_credited.json");

        /// <summary>
        /// Records <paramref name="orderCode"/> as paid and returns whether it was NEW - i.e.
        /// whether the caller should actually grant the XP. Returns false for anything already
        /// present, and false if the write fails after the fact (the grant still happened in memory,
        /// so within this app session it will not double-pay either way).
        /// </summary>
        public static bool TryCredit(string orderCode)
        {
            if (string.IsNullOrWhiteSpace(orderCode)) return false;
            var code = orderCode.Trim();

            var codes = Load();
            if (codes.Contains(code, StringComparer.Ordinal)) return false;

            codes.Add(code);
            if (codes.Count > MaxEntries) codes.RemoveRange(0, codes.Count - MaxEntries);
            Save(codes);
            return true;
        }

        /// <summary>True when this order has already been paid. Read-only companion to
        /// <see cref="TryCredit"/>, for anything that wants to LABEL a card without crediting it.</summary>
        public static bool IsCredited(string? orderCode)
        {
            if (string.IsNullOrWhiteSpace(orderCode)) return false;
            return Load().Contains(orderCode.Trim(), StringComparer.Ordinal);
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
                // FAIL OPEN. A corrupt or unreadable ledger means we cannot prove a code was paid,
                // and the honest user who just finished a drop matters more than the dishonest one
                // who corrupted a file to be paid twice. Starting empty also self-heals: the next
                // successful write replaces the bad file.
                App.Logger?.Warning(ex, "JustDrop: credited-orders ledger unreadable; starting empty");
            }
            _codes = new List<string>();
            return _codes;
        }

        private static void Save(List<string> codes)
        {
            _codes = codes;
            try
            {
                Directory.CreateDirectory(CorePaths.UserData);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(codes));
            }
            catch (Exception ex)
            {
                // In-memory state stands, so this session still will not double-pay.
                App.Logger?.Warning(ex, "JustDrop: could not persist the credited-orders ledger");
            }
        }
    }
}
