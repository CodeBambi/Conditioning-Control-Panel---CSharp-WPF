namespace ConditioningControlPanel.Models;

/// <summary>
/// How an activity is counted. Lives in Core rather than beside QuestDefinition because
/// ProgramDay.Verifier is typed on it - a program day is proven through the same choke point a
/// quest is (QuestService.UpdateQuestProgress), which is the whole reason the program ledger did
/// not need a second tracking pass. Quest.cs itself stays in the WPF head: every quest icon is a
/// pack:// URI, and only PresentationFramework resolves those.
/// </summary>
public enum QuestCategory
{
    Flash,          // View flash images
    Video,          // Watch video minutes
    Spiral,         // Use spiral overlay
    PinkFilter,     // Use pink filter
    Bubbles,        // Pop bubbles
    LockCard,       // Complete lock cards
    Session,        // Complete sessions
    Streak,         // Daily streak
    BubbleCount,    // Bubble count minigame
    Mantra,         // Complete mantras
    Combined,       // Multiple activities (overlay time, XP earned)

    // Patreon-exclusive categories (quests carrying these set RequiresPremium = true)
    Autonomy,       // Minutes Bambi Takeover (autonomy) is running
    Lockdown,       // Lockdowns completed
    Remote,         // Remote-control commands RECEIVED (subject side)
    KeywordTrigger, // Keyword/OCR triggers fired
    BlinkTrainer,   // Blinks logged in the live blink trainer

    // --- Giving side of remote control. NOT premium-gated. ---
    // Counts commands the user ISSUES to another subject as a Controller. Deliberately open
    // to every tier: browsing Available Subjects is free by design (MainWindow.RemoteControl.cs)
    // and the matchmaking pool needs Controllers at least as badly as it needs subjects.
    // It counts a command at ANY intensity level - the quest must never be a reason to push a
    // subject onto a heavier tier.
    RemoteIssue     // Remote-control commands ISSUED to another subject (controller side)
}

