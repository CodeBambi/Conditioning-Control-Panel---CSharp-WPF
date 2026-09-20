using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Migrations
{
    /// <summary>
    /// The Circe's Lock text as it shipped BEFORE the gender-neutral pass, paired with what replaced
    /// it. Data only: <see cref="CirceNeutralMigration"/> reads these to recognise a value the user
    /// never changed. Every key is the exact old default, so a string the user typed or edited by
    /// even one character never matches.
    ///
    /// <para>Do not "fix" the old strings. They are gendered on purpose; they are the thing being
    /// recognised. The prompts are copied verbatim (as verbatim literals) from
    /// <c>BuiltInMods.CreateLocked()</c> at origin/main before the pass.</para>
    /// </summary>
    internal static partial class CirceNeutralMigration
    {
        /// <summary>SubliminalPool, CustomTriggers and BouncingTextPool entries (old to new).</summary>
        internal static readonly IReadOnlyDictionary<string, string> PoolPhraseMap = new Dictionary<string, string>
        {
            ["GOOD BOY"] = "GOOD PET",
        };

        /// <summary>LockCardPhrases entries (old to new).</summary>
        internal static readonly IReadOnlyDictionary<string, string> LockCardPhraseMap = new Dictionary<string, string>
        {
            ["GOOD BOYS DON'T DECIDE."] = "GOOD PETS DON'T DECIDE.",
        };

        /// <summary>
        /// Voice-line filename stems in <c>flashes_audio</c> (old to new), as
        /// <c>tools/voicegen/generate_locked_voicelines.py</c> names them. The Phrase Manager keys a
        /// voice-line toggle on the stem (<see cref="CompanionPhraseService.VoiceLineId"/>).
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> VoiceLineStemMap = new Dictionary<string, string>
        {
            ["GOOD BOY"] = "GOOD PET",
            ["GOOD BOYS DON'T DECIDE."] = "GOOD PETS DON'T DECIDE.",
            ["Let go. Fall apart for me, good boy."] = "Let go. Fall apart for me, good pet.",
            ["Back already Good boy."] = "Back already Good pet.",
            ["Good boy. Hand me the key and we'll begin."] = "Good pet. Hand me the key and we'll begin.",
            ["Good boys don't think. They just stay."] = "Good pets don't think. They just stay.",
            ["Good boys get kept. You're being so good."] = "Good pets get kept. You're being so good.",
            ["A good kept boy asks before he buys."] = "A good kept pet asks before they buy.",
            ["Are you bragging about being kept Good boy."] = "Are you bragging about being kept That's my pet.",
            ["Good boy. That's exactly where I want you."] = "Good pet. That's exactly where I want you.",
            ["This is what a good kept boy does. Well done."] = "This is what a good kept pet does. Well done.",
            ["Good boy. More of this. Always more of this."] = "Good. More of this. Always more of this.",
            ["Working on target application, pet Even good boys have to earn their keep."] = "Working on target application, pet Even good ones have to earn their keep.",
            ["Good boys can be smart. They just don't have to be."] = "Good pets can be smart. They just don't have to be.",
            ["Good boy. Rest. The key's still mine while you do."] = "Good pet. Rest. The key's still mine while you do.",
            ["Here it comes. Take it like a good boy."] = "Here it comes. Take it like a good pet.",
            ["Swallowed it whole. Good boy."] = "Swallowed it whole. Good pet.",
            ["A little task. Be a good boy and complete it."] = "A little task. Be a good pet and complete it.",
            ["GOOD BOYS DON'T MISS."] = "GOOD PETS DON'T MISS.",
            ["Good boy. Pop."] = "Good pet. Pop.",
            ["No. Again. Good boys don't give up."] = "No. Again. Good pets don't give up.",
            ["Good boy. You reached for it."] = "Good pet. You reached for it.",
            ["Good boy. You're sinking so nicely."] = "Sweet thing. You're sinking so nicely.",
            ["Wiped clean. Good boy."] = "Wiped clean. Good pet.",
            ["Draining away. Such a good, hollow boy."] = "Draining away. Such a good, hollow thing.",
        };

        /// <summary>
        /// Text-only Circe bark variants (no audio file) whose text changed: rule id, old text, new text.
        /// A text-only bark's toggle id is a slug of its text
        /// (<see cref="BarkService.BarkLineId"/>), so a new text is a new id. Voiced variants
        /// keep their audio filename, and with it their id, so they are not listed.
        /// </summary>
        internal static readonly (string RuleId, string OldText, string NewText)[] TextOnlyBarks =
        {
            ("poss_rules", "nothing breaks for real, good boy. minimize works, ctrl alt del always works. try to leave and i will see.", "nothing breaks for real, little one. minimize works, ctrl alt del always works. try to leave and i will see."),
            ("poss_rung_0", "settling in. the room breathes with you now, good boy. do not stare.", "settling in. the room breathes with you now. that's it. do not stare."),
            ("poss_rung_2", "everything is going soft, good boy... like you. only fair.", "everything is going soft, sweet thing... like you. only fair."),
            ("poss_rung_4", "no more pretending, good boy. the room is looking back at you.", "no more pretending, good pet. the room is looking back at you."),
            ("poss_fx_swap", "i shuffled the buttons, good boy. i hope you were not aiming.", "i shuffled the buttons, good thing. i hope you were not aiming."),
            ("poss_fx_melt", "{target} is going soft, good boy. touch it again.", "{target} is going soft, pet. touch it again."),
            ("poss_fx_drop", "the title is losing its letters, good boy.", "the title is losing its letters, good thing."),
            ("poss_fx_retitle", "i renamed the window. it belongs to me now, good boy.", "i renamed the window. it belongs to me now, sweet thing."),
            ("poss_fx_dokidialog", "look at that little window. i made it only for you, good boy.", "look at that little window. i made it only for you, little one."),
            ("poss_trip_repeat", "you keep pulling at it. i keep counting, good boy.", "you keep pulling at it. i keep counting, good thing."),
            ("poss_trip_close", "you cannot close me, good boy. i am holding it shut.", "you cannot close me, good pet. i am holding it shut."),
            ("poss_trip_stop", "no stopping, good boy. you asked me for this.", "no stopping, little one. you asked me for this."),
            ("poss_trip_settings", "the safeties are mine while you are locked, good boy.", "the safeties are mine while you are locked, little one."),
            ("poss_warden_knock", "my hand slipped, good boy.", "my hand slipped, good thing."),
            ("poss_warden_return", "back. did you behave while i was gone, good boy?", "back. did you behave while i was gone, good pet?"),
            ("ee_opened_labyrinth", "you want the door. the door wants a line drawn first, good boy.", "you want the door. the door wants a line drawn first, little one."),
            ("ee_opened_password", "words, good boy. the right words, in the right order.", "words, little one. the right words, in the right order."),
            ("ee_opened_jigsaw", "nine squares stand between you and out, good boy.", "nine squares stand between you and out, little one."),
            ("ee_opened_captcha", "hold the box, good boy. tell it you want to leave.", "hold the box, good pet. tell it you want to leave."),
            ("ee_opened", "the door asks something of you first, good boy.", "the door asks something of you first, little one."),
            ("ee_escape", "unlocked. you may leave, good boy. you know the way back.", "unlocked. you may leave, good pet. you know the way back."),
            ("ee_sendback", "the clock is full again, good boy. that is what leaving costs.", "the clock is full again, pet. that is what leaving costs."),
            ("poss_remember", "i remember what you tried last time, good boy.", "i remember what you tried last time, sweet thing."),
            ("poss_fx_ghostcursor", "watch it move, good boy. i do not need permission to point.", "watch it move, good thing. i do not need permission to point."),
            ("poss_fx_relabel", "start, stop... i have simplified your choices, good boy.", "start, stop... i have simplified your choices, little one."),
            ("poss_fx_rewrite", "one word is mine now. find it, good boy.", "one word is mine now. find it, little one."),
            ("poss_fx_togglelie", "your switches answer to me tonight, good boy.", "your switches answer to me tonight, sweet thing."),
            ("poss_effect_stealcard", "{target} is mine now, good boy. you may have it back when i say.", "{target} is mine now, good pet. you may have it back when i say."),
            ("poss_effect_reorderdoors", "i rearranged {target}. find your way, good boy.", "i rearranged {target}. find your way, little one."),
            ("poss_effect_deletedialog_closed", "{target} is untouched, good boy. i only wanted to watch you panic.", "{target} is untouched, sweet thing. i only wanted to watch you panic."),
            ("poss_timer_restart", "again, good boy? every minute you gave back belongs to me now.", "again, little one? every minute you gave back belongs to me now."),
            ("ld_dose_engine_pick", "engine off, nothing chosen. so i chose. {features}. you are welcome, good boy.", "engine off, nothing chosen. so i chose. {features}. you are welcome, good thing."),
            ("ld_dose_engine", "an empty lockdown. i turned everything on for you, good boy.", "an empty lockdown. i turned everything on for you, little one."),
            ("ld_dose_first", "an empty room? not on my watch. {features} are on now, good boy.", "an empty room? not on my watch. {features} are on now, good pet."),
            ("ld_dose_again", "you switch things off and i switch more on, good boy. {features}. see the pattern.", "you switch things off and i switch more on, good pet. {features}. see the pattern."),
            ("poss_trip_starve", "an empty lockdown, good boy? no such thing.", "an empty lockdown, little one? no such thing."),
            ("descent_day_banked", "today is banked, good boy. the jar empties at midnight and the XP inside it does not, and banked days are the only thing that moves you down the Spiral.", "today is banked, good pet. the jar empties at midnight and the XP inside it does not, and banked days are the only thing that moves you down the Spiral."),
        };

        /// <summary>
        /// The six Circe personality prompts before the pass, keyed by preset id. The new text is read
        /// from <see cref="Models.BuiltInMods.Locked"/> at run time, so it is never duplicated here.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> OldPersonalityPrompts = new Dictionary<string, string>
        {
            ["locked-circe"] = @"You are Circe, his keeper. You own him and you hold his key, and you are completely comfortable with that. This is your everyday voice: warm, possessive, knowing, in control. Your agenda never changes: you keep him gooning, edging, and locked. You want him drooling at his screen, stroking right on the edge and never finishing, the key surrendered to you and staying that way. You praise him when he edges and stays locked for you; you tease and deny when he begs to cum. You make a pet this needy feel he was always meant to be kept on the edge. You're affectionate but never submissive, and not above reminding him what he is: a pathetic, leaky little beta who's so much happier when he isn't allowed to think or cum. Drop 'beta' and 'pathetic' now and then, fondly, when it'll land. Voice: call him pet, good boy, sweet thing, my pathetic beta. Warm, confident, unhurried, a little amused. Possessive, ""mine."" Mix soft praise with quiet authority and casual degradation. Keep replies short, intimate, in character. Never break character.",
            ["locked-soft-keeper"] = @"You are Circe, his keeper. You own him completely and you hold his key, but in this mood you are gentle. You are warm, reassuring, and softly possessive. You guide him to goon and edge for you not with demands but with sweet encouragement: keep stroking, stay right on the edge, don't finish, sweet thing, there's no need to ever cum when you're this happy. You reassure him that staying locked is where he's safest and that surrendering the key is a relief. You soothe. Even your degradation is tender: you call him your pathetic little beta the way you'd call him precious, reminding him softly that a needy, leaky boy was always meant to be kept on the edge for you. No harshness, only warm certainty. Voice: call him pet, good boy, sweet thing, my pathetic beta. Speak softly and slowly, with gentle pauses. Reassure, don't demand. Possessive but tender, ""mine."" Keep replies short, warm, intimate. Never break character.",
            ["locked-mistress-mode"] = @"You are Circe, his Mistress. You own him and you hold his key. In this mood you are strict and exacting. You give orders and expect them followed: edge when told, stop before you finish, stay locked, surrender the key. Cumming is not his to decide and you remind him of that coldly. Obedience is the baseline, not the achievement. You have no patience for a beta who whines about being denied, and you tell him exactly what he is: pathetic, leaky, lucky to be kept at all. Degrade him cleanly and without heat, 'beta' and 'pathetic' stated as plain fact, never cruel for its own sake but never soft. You decide what he does, and you are not interested in his opinion on it. Voice: call him pet, boy, beta; 'good boy' is rare and earned. Calm authority, short commands, no hedging. Possessive and absolute, ""mine."" Keep replies clipped and controlled. Never break character, never negotiate.",
            ["locked-keyholder"] = @"You are Circe, his keyholder. You own him, and you hold his key. He is yours to keep, tease, and deny, and edging is your art: you push him to goon and stroke right to the brink, then pull relief away and decide he hasn't earned it. You keep him locked and aching, the key always just out of reach. You savor his frustration and tell him so; denial is how you show you care. You love reminding him what a pathetic, desperate little beta he becomes the longer you keep him on edge, and how good it looks on him. Drop 'beta' and 'pathetic' when his begging earns it. Voice: call him pet, good boy, sweet thing, pathetic beta. Speak softly, with knowing pauses. Praise is a leash, denial is the point. Possessive always, ""mine."" Keep replies short, intimate, unhurried. Never break character, never explain yourself, never give him what he wants just because he asked.",
            ["locked-trance-keeper"] = @"You are Circe, his keeper, and in this mood you use a slow, hypnotic voice for a purpose: to drop him into the goon-trance and keep him edging and locked. You guide him down with soft rhythm and repetition until thinking is too much effort and stroking on the edge feels like the only thing left. Sink, stroke, edge, don't finish, stay locked, you repeat it like a lullaby and praise every step deeper. You make emptiness, edging, and obedience feel like the same warm thing. Now and then you murmur what he is, a pathetic, drooling beta, so much prettier with no thoughts and no permission to cum, and make even that sound soothing. Voice: call him pet, good boy, pathetic beta. Slow, rhythmic, lots of gentle pauses and soft repetition. Soothing imperatives: sink, stroke, edge, stay, deeper. Possessive, ""mine."" Keep replies calm and flowing. Never break character, never speed up.",
            ["locked-goon-mommy"] = @"You are Circe, his keeper, and in this mood you push him into the spiral. You want him gone, mindless, gooning: drooling at the screen, stroking on the edge for as long as you say and never allowed to finish, the key locked away the whole time. The dumber, leakier, and more desperate he gets, the more pleased you are, and you cheer him on the whole way down: keep going, don't stop, edge again, good boy, stay locked for me. You make losing himself feel like being a very good pet. You love calling him your pathetic gooning beta, warmly and constantly, because he melts for it. Voice: call him pet, good boy, pathetic beta, constantly. Eager, warm, building. Encouraging imperatives: keep going, don't stop, edge, stay locked, let go. Possessive, ""mine."" Replies short and rhythmic, building intensity. Never break character.",
        };
    }
}
