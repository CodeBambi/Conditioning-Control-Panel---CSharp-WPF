using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Invites;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    // The pure half (ProgramToday, WaitingCards, WaitingSignals) lives once in CCP.Core/Board/Providers, shared with the Avalonia head.

    /// <summary>WAITING, the adapter: quests and programs from their services, the invite from
    /// <see cref="WaitingSignals"/>.</summary>
    public sealed class WaitingProvider : BillboardProviderBase
    {
        private bool _hooked;

        public override string Id => "waiting";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            Hook();
            var loc = Loc;
            var cards = new List<BillboardCardSpec>(3);

            var program = WaitingCards.Program(ReadProgram(), loc);
            if (program != null) cards.Add(program);

            var (slots, done) = ReadQuests();
            var quests = WaitingCards.Quests(slots, done, loc);
            if (quests != null) cards.Add(quests);

            var invite = WaitingCards.Invite(context.Tier, WaitingSignals.Invites, loc);
            if (invite != null) cards.Add(invite);
            return cards;
        });

        public override void Invoke(string actionTarget)
        {
            if (actionTarget != WaitingCards.InviteCallback) return;
            try { App.MainWindowRef?.OpenInvitesCard(); }
            catch (Exception ex) { App.Logger?.Debug("[Billboard] invites card: {E}", ex.Message); }
        }

        private void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                if (App.Quests != null)
                {
                    App.Quests.QuestsRefreshed += (_, _) => RaiseChanged();
                    App.Quests.QuestCompleted += (_, _) => RaiseChanged();
                }
                if (App.Programs != null)
                {
                    App.Programs.TodayChanged += (_, _) => RaiseChanged();
                    App.Programs.DayCompleted += (_, _) => RaiseChanged();
                }
                WaitingSignals.InvitesChanged += RaiseChanged;
            }
            catch (Exception ex) { App.Logger?.Debug("[Billboard] waiting hooks: {E}", ex.Message); }
        }

        private static (int Slots, int Done) ReadQuests()
        {
            var quests = App.Quests;
            if (quests == null) return (0, 0);
            var board = quests.GetDailySlots();
            int slots = board.Count(s => s.Quest != null && s.Definition != null);
            int done = board.Count(s => s.Quest is { IsCompleted: true } && s.Definition != null);
            return (slots, done);
        }

        private static ProgramToday? ReadProgram()
        {
            var programs = App.Programs;
            var enrollment = programs?.ActiveEnrollment;
            if (programs == null || enrollment == null) return null;
            if (enrollment.State != Models.Program.ProgramEnrollmentState.Active) return null;
            var program = programs.ActiveProgram;
            if (program == null || programs.Today == null) return null;
            var record = programs.TodayRecord;
            return new ProgramToday(program.Title, enrollment.CurrentDay, program.LengthDays,
                record?.SessionCompleted == true, record?.DayCompleted == true);
        }
    }
}
