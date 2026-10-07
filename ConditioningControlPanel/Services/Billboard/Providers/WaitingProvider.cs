using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Invites;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>Today's program day as the Waiting card needs it.</summary>
    public sealed record ProgramToday(string ProgramTitle, int Day, int LengthDays, bool SessionDone, bool DayDone);

    /// <summary>
    /// WAITING, the pure half: things the app already knows are left for today.
    /// Quests left, today's program day, and an invite code nobody used yet.
    /// The Back Room's free spin is not here: its state lives in the room page and the app
    /// only learns it while the room is open, so a card would need a new request.
    /// </summary>
    public static class WaitingCards
    {
        public const string CardQuests = "waiting.quests";
        public const string CardProgram = "waiting.program";
        public const string CardInvite = "waiting.invite";
        public const string InviteCallback = "invites";

        /// <param name="slots">Today's daily slots that hold a quest.</param>
        /// <param name="done">How many of them are finished.</param>
        public static BillboardCardSpec? Quests(int slots, int done, Func<string, string> loc)
        {
            if (slots <= 0) return null;
            done = Math.Clamp(done, 0, slots);
            int left = slots - done;
            if (left <= 0) return null;
            var title = left == 1
                ? loc("billboard_card_quests_title_one")
                : CardText.F(loc, "billboard_card_quests_title_many", left);
            return Card(CardQuests, 1, loc, title, CardText.F(loc, "billboard_card_quests_line", done, slots),
                new BillboardAction(BillboardActionKind.Tab, "quests", loc("billboard_card_quests_button")),
                CardArt.Quests, new Dictionary<string, int> { ["done"] = done, ["total"] = slots });
        }

        /// <summary>A program day not finished yet. Null when there is no running day or it is done.</summary>
        public static BillboardCardSpec? Program(ProgramToday? today, Func<string, string> loc)
        {
            if (today == null || today.DayDone || today.Day <= 0 || string.IsNullOrWhiteSpace(today.ProgramTitle)) return null;
            var line = loc(today.SessionDone ? "billboard_card_program_line_tasks" : "billboard_card_program_line_session");
            var data = new Dictionary<string, int> { ["day"] = today.Day, ["days"] = Math.Max(today.Day, today.LengthDays) };
            return Card(CardProgram, 0, loc,
                CardText.F(loc, "billboard_card_program_title", today.Day, today.ProgramTitle.Trim()), line,
                new BillboardAction(BillboardActionKind.Tab, "programs", loc("billboard_card_program_button")),
                CardArt.Calendar, data);
        }

        /// <summary>A subscriber's monthly code nobody has used, as the header ticket reads it.</summary>
        public static BillboardCardSpec? Invite(BillboardTier tier, InviteMine? mine, Func<string, string> loc)
        {
            if (tier == BillboardTier.Free) return null;
            if (!InviteTicketRule.ShouldShow(mine)) return null;
            return Card(CardInvite, 2, loc, loc("billboard_card_invite_title"), loc("billboard_card_invite_line"),
                new BillboardAction(BillboardActionKind.Callback, InviteCallback, loc("billboard_card_invite_button")),
                CardArt.Invite, null);
        }

        private static BillboardCardSpec Card(string id, int priority, Func<string, string> loc, string title, string line,
            BillboardAction action, string art, object? data) =>
            new(id, BillboardCardKind.Waiting, priority, loc("billboard_card_waiting_eyebrow"), title, line,
                CardHues.Waiting, art, data, action);
    }

    /// <summary>
    /// What the header invite ticket last read (MainWindow.InviteTicket.cs hands it here), so the
    /// board knows about an unused code without asking the server a second time.
    /// </summary>
    public static class WaitingSignals
    {
        private static InviteMine? _invites;

        public static InviteMine? Invites => _invites;

        public static event Action? InvitesChanged;

        public static void NoteInvites(InviteMine? mine)
        {
            bool before = InviteTicketRule.ShouldShow(_invites);
            _invites = mine;
            if (before != InviteTicketRule.ShouldShow(mine))
            {
                try { InvitesChanged?.Invoke(); } catch { }
            }
        }

        internal static void ResetForTests() { _invites = null; InvitesChanged = null; }
    }

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
