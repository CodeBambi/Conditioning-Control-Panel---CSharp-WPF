using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// WPF MainWindow.Enhancements.cs SkillCard_Click: confirm, buy through the server, then the burst on the
    /// node (and the prestige burst when the spend crossed a rank) or the refusal in a "Purchase Failed" box.
    /// The wallet is never touched here: only Core <see cref="SkillPurchase"/> writes it, from the server's answer.
    /// ponytail: no prestige sheen sweep across the window (WPF SweepSheen); the burst alone marks the rank.
    /// </summary>
    public partial class EnhancementsTabView
    {
        private const int EnhancementBurstCount = 100, PrestigeBurstCount = 150;   // WPF EventFx, verbatim

        private readonly EventBurstLayer _purchaseBurst = new();
        private Control? _prestigeRow;
        private bool _buying;

        /// <summary>Test seams: the Yes/No question, the failure box, the service.</summary>
        internal Func<Window?, string, string, Task<bool>> AskPurchase = (owner, title, message) =>
            owner == null ? Task.FromResult(false) : MessageDialog.ConfirmAsync(owner, title, message);
        internal Func<Window?, string, string, Task> TellFailure = (owner, title, message) =>
            owner == null ? Task.CompletedTask : MessageDialog.ShowAsync(owner, title, message);
        internal SkillPurchase? PurchaseService;

        /// <summary>Bursts fired by purchases (test hook).</summary>
        internal int PurchaseBursts => _purchaseBurst.Count;

        /// <summary>WPF attaches SkillCard_Click to MouseLeftButtonUp of a purchasable card only.</summary>
        private void MakePurchasable(Control card, string skillId)
        {
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                e.Handled = true;
                _ = PurchaseAsync(card, skillId);
            };
        }

        private static long PrestigeRankNow() => 1 + (App.Achievements?.Progress?.LifetimeSkillPointsSpent ?? 0) / 100;

        /// <summary>SkillCard_Click's body. One purchase at a time (WPF disabled the card while it ran).</summary>
        internal async Task PurchaseAsync(Control card, string skillId)
        {
            if (_buying) return;
            var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
            if (skill == null) return;
            _buying = true;
            try
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                var pointsLabel = (CoreMods.Service?.GetPointsLabel() ?? Loc.Get("label_sparkle_points")).ToLower();
                var message = Loc.GetF("msg_purchase_skill", CoreMods.MakeModAware(skill.LocalizedName), skill.Cost, pointsLabel,
                                  CoreMods.MakeModAware(skill.LocalizedFlavorText), CoreMods.MakeModAware(skill.LocalizedDescription))
                              + "\n\n" + Loc.Get("msg_skill_permanent_note");
                if (!await AskPurchase(owner, Loc.Get("dialog_purchase_enhancement"), message)) return;

                card.IsEnabled = false;
                var rankBefore = PrestigeRankNow();
                try
                {
                    var service = PurchaseService ?? SkillPurchase.Current ?? new SkillPurchase();
                    var (success, error) = await service.PurchaseSkillAsync(skillId);
                    if (success)
                    {
                        Serilog.Log.Information("Skill purchased via UI: {SkillId}", skillId);
                        // Before the repaint: it rebuilds the tree and a detached anchor cannot be mapped.
                        _purchaseBurst.Fire(card, EnhancementBurstCount);
                        if (PrestigeRankNow() > rankBefore && _prestigeRow != null)
                            _purchaseBurst.Fire(_prestigeRow, PrestigeBurstCount, color: Env.GlowColor);
                    }
                    else if (!string.IsNullOrEmpty(error))
                        await TellFailure(owner, Loc.Get("dialog_purchase_failed"), error);
                }
                finally
                {
                    card.IsEnabled = true;
                    Repaint();
                }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Skill purchase click failed"); }
            finally { _buying = false; }
        }
    }
}
