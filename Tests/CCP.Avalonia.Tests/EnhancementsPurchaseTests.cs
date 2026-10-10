using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Skill Tree buying against WPF MainWindow.Enhancements.cs SkillCard_Click: only a purchasable node
/// takes the click, the click asks first, "no" buys nothing, a debited answer repaints the node as owned and a
/// refusal shows the Purchase Failed box and leaves the wallet alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class EnhancementsPurchaseTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public string Answer = "{}";
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(Answer.Replace('\'', '"'), Encoding.UTF8, "application/json") });
        }
    }

    private static void Setup()
    {
        if (global::Avalonia.Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Dictionary<string, Control> Nodes(EnhancementsTabView tab) =>
        tab.FindControl<Canvas>("SkillTreeCanvas")!.Children.OfType<Control>()
            .Where(c => c.Tag is string).ToDictionary(c => (string)c.Tag!);

    [Fact]
    public Task AClickAsksThenBuysAndARefusalKeepsTheWallet() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        Window? w = null;
        try
        {
            var s = CoreSettings.Current;
            s.MotionLevel = MotionLevel.Off;
            s.PerformanceMode = true;
            s.SkillPoints = 1000;
            s.UnifiedId = "u_k2";
            s.UnlockedSkills = new List<string> { "sparkle_boost_1" };
            var buyable = SkillDefinition.All.Single(x => x.Id == "sparkle_boost_2");

            var http = new Fake();
            var questions = new List<string>();
            var failures = new List<string>();
            bool answer = false;
            var tab = new EnhancementsTabView
            {
                PurchaseService = new SkillPurchase(() => CoreSettings.Current, http) { Save = () => { } },
            };
            tab.AskPurchase = (_, title, message) => { questions.Add(title + "|" + message); return Task.FromResult(answer); };
            tab.TellFailure = (_, title, message) => { failures.Add(title + "|" + message); return Task.CompletedTask; };
            w = new Window { Width = 1400, Height = 700, Content = tab };
            w.Show();
            Dispatcher.UIThread.RunJobs();

            // A real click on the purchasable node asks; "no" never reaches the server.
            var node = Nodes(tab)["sparkle_boost_2"];
            var p = node.TranslatePoint(new Point(40, 40), w)!.Value;
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var asked = Assert.Single(questions);
            Assert.StartsWith(Loc.Get("dialog_purchase_enhancement") + "|", asked);
            Assert.Contains(Loc.Get("msg_skill_permanent_note"), asked);
            Assert.Equal(0, http.Calls);
            Assert.Equal(1000, s.SkillPoints);

            // An owned node and a locked one take no click.
            foreach (var id in new[] { "sparkle_boost_1", "lucky_bimbo" })
            {
                var other = Nodes(tab)[id];
                var q = other.TranslatePoint(new Point(40, 40), w)!.Value;
                w.MouseDown(q, MouseButton.Left);
                w.MouseUp(q, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Single(questions);

            // A refusal: the box, the wallet kept, the node still for sale.
            answer = true;
            http.Answer = "{'success':false,'error':'Server says no'}";
            await tab.PurchaseAsync(Nodes(tab)["sparkle_boost_2"], "sparkle_boost_2");
            Assert.Equal(Loc.Get("dialog_purchase_failed") + "|Server says no", Assert.Single(failures));
            Assert.Equal(1000, s.SkillPoints);
            Assert.DoesNotContain("sparkle_boost_2", s.UnlockedSkills);

            // A debited answer: the server's balance, the node repainted as owned.
            http.Answer = "{'success':true,'skill_points':" + (1000 - buyable.Cost) + ",'unlocked_skills':['sparkle_boost_1','sparkle_boost_2']}";
            await tab.PurchaseAsync(Nodes(tab)["sparkle_boost_2"], "sparkle_boost_2");
            Dispatcher.UIThread.RunJobs();
            Assert.Single(failures);
            Assert.Equal(1000 - buyable.Cost, s.SkillPoints);
            Assert.Contains("sparkle_boost_2", s.UnlockedSkills);
            var texts = Nodes(tab)["sparkle_boost_2"].GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains($"💎{buyable.Cost} {Loc.Get("label_skill_permanent")}", texts);

            // Signed out: the WPF message, no request.
            s.UnifiedId = null;
            int calls = http.Calls;
            var next = SkillDefinition.All.First(x => SkillTreeRules.CanPurchaseSkill(s, x.Id));
            await tab.PurchaseAsync(tab, next.Id);
            Assert.Equal(calls, http.Calls);
            Assert.Equal(Loc.Get("dialog_purchase_failed") + "|" + Loc.Get("skill_err_login_required"), failures.Last());
        }
        finally
        {
            w?.Close();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
