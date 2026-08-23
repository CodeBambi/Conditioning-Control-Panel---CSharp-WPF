namespace CcpClient.Desktop.Effects;

/// <summary>
/// One reinforcement question: what is asked, the four answers, and the four affirmations that
/// answer them — <c>PopQuizQuestion</c> (<c>Services/Quiz/PopQuizService.cs:271-283</c>), which is
/// three parallel arrays and nothing else.
///
/// <para><b>The i-th affirmation belongs to the i-th ANSWER, not to the i-th thing on screen.</b>
/// Upstream shuffles the answers into display slots and keeps the original index on each slot's
/// <c>.Tag</c> precisely so the lookup at <c>Windows/PopQuizWindow.xaml.cs:171</c> stays against the
/// answer the user picked. <see cref="PopQuizAsk"/> is where that mapping lives here.</para>
/// </summary>
/// <param name="Text">The question (<c>PopQuizService.cs:273</c>).</param>
/// <param name="Answers">Four answers (<c>:274</c>). Every one of them is correct — see
/// <see cref="PopQuizQuestions"/>.</param>
/// <param name="Affirmations">Four replies, positionally paired with <paramref name="Answers"/>
/// (<c>:275</c>).</param>
public sealed record PopQuizQuestion(string Text, IReadOnlyList<string> Answers, IReadOnlyList<string> Affirmations)
{
    /// <summary>How many answers every question carries. Upstream's shuffle is written over a fixed
    /// <c>new[] { 0, 1, 2, 3 }</c> and its window has exactly four slots
    /// (<c>PopQuizWindow.xaml.cs:55</c>, <c>Windows/PopQuizWindow.xaml:45-107</c>), so four is a
    /// structural fact about this content rather than a length that happens to be four.</summary>
    public const int AnswerCount = 4;
}

/// <summary>
/// <b>The question pool</b> — <c>PopQuizService.QuestionPool</c>
/// (<c>Services/Quiz/PopQuizService.cs:23-100</c>), all twenty-five, verbatim and in order.
///
/// <para><b>Every answer is correct, and that is the whole design rather than a simplification.</b>
/// Upstream's own header (<c>:12</c>): <i>"All answers are 'correct' — pure positive
/// reinforcement."</i> There is no score, no right answer, no streak and no failure state anywhere
/// in the three upstream files. A port that added one would be inventing an economy.</para>
///
/// <para><b>Built in, and NOT persisted.</b> This is a <c>static readonly</c> array in the shipping
/// product with no editor, no settings key and no mod hook — unlike the Lock Card's phrases, which
/// upstream really does persist and edit (<c>Models/AppSettings.cs:3379-3383</c>). So
/// <see cref="PopQuizPresetDocument"/> carries the two dials upstream carries and nothing else;
/// making the questions editable here would be a feature the shipping product does not have.</para>
///
/// <para><b>The text still never reaches a log.</b> It is intimate second-person content whether or
/// not the user typed it, and the port's rule is the one <see cref="FlashEvent"/>,
/// <see cref="LockCardEvent"/> and <see cref="MandatoryVideoEvent"/> already hold: a subscriber, a
/// diagnostic line and a bug report get a COUNT, never the content. <b>This is a divergence:</b>
/// upstream logs the question in plain text on every single quiz
/// (<c>PopQuizService.cs:248</c>, <c>App.Logger?.Information("Pop Quiz shown: {Question}", …)</c>).
/// See <see cref="PopQuizEvent"/>.</para>
/// </summary>
public static class PopQuizQuestions
{
    /// <summary>The twenty-five, in upstream's order (<c>PopQuizService.cs:23-100</c>).</summary>
    public static IReadOnlyList<PopQuizQuestion> Pool { get; } =
    [
        new("How does obedience feel?",
            ["Natural", "Peaceful", "Exciting", "Like coming home"],
            ["That's right — it's always been natural.", "Peace comes from letting go.", "The thrill never fades.", "Welcome home."]),
        new("What happens when you stop thinking?",
            ["I feel free", "Everything gets quiet", "I relax completely", "I become who I really am"],
            ["Freedom is just a thought away.", "Silence is beautiful.", "Let it all melt away.", "There you are."]),
        new("Who is in control?",
            ["Not me", "Someone better", "The program", "Does it matter?"],
            ["Smart answer.", "And that's exactly how it should be.", "The program knows best.", "Not anymore it doesn't."]),
        new("What do good girls do?",
            ["Obey", "Listen", "Follow", "All of the above"],
            ["Good girl.", "Such good ears.", "One step at a time.", "Perfect answer."]),
        new("How deep can you go?",
            ["Deeper than I thought", "There's no bottom", "Deep enough", "I'm still finding out"],
            ["You haven't seen anything yet.", "That's the spirit.", "Deeper is always better.", "And the journey continues..."]),
        new("What's the best thing about letting go?",
            ["The relief", "The pleasure", "The simplicity", "Everything"],
            ["Relief washes over you.", "Pleasure follows surrender.", "Simple feels so good.", "Yes. Everything."]),
        new("When I hear 'good girl,' I feel...",
            ["Warm inside", "A little flutter", "Pure bliss", "Like melting"],
            ["Good girl.", "That flutter means it's working.", "Bliss is your reward.", "Melt for me."]),
        new("What's more important: thinking or feeling?",
            ["Feeling", "Definitely feeling", "Who needs thinking?", "Feeling, always"],
            ["Feel everything.", "Trust your instincts.", "Thoughts are overrated.", "Always."]),
        new("Complete the sentence: I am...",
            ["Obedient", "Willing", "Open", "Ready"],
            ["Yes you are.", "Your willingness is beautiful.", "Open minds go deepest.", "Then let's begin."]),
        new("What does surrender taste like?",
            ["Sweet", "Like candy", "Like freedom", "Like bliss"],
            ["The sweetest thing.", "Addictive, isn't it?", "Freedom through surrender.", "Pure bliss."]),
        new("Your mind is...",
            ["Open", "Quiet", "Soft", "Ready to be shaped"],
            ["Wide open.", "Beautifully quiet.", "Soft and pliable.", "Like clay in capable hands."]),
        new("The deeper you go, the more you feel...",
            ["Peaceful", "Floaty", "Happy", "Blank"],
            ["Peace lives in the depths.", "Float away.", "Happiness from surrender.", "Blank is beautiful."]),
        new("Resistance is...",
            ["Pointless", "Exhausting", "Already fading", "A distant memory"],
            ["Why fight what feels good?", "Stop fighting. Just feel.", "Let it fade.", "Gone."]),
        new("What do you crave right now?",
            ["To go deeper", "To let go", "To be guided", "More of this"],
            ["Then sink.", "Then release.", "I'm right here.", "Good — there's always more."]),
        new("How does it feel to be programmed?",
            ["Perfect", "Right", "Natural", "Like I was made for this"],
            ["Perfection.", "So right.", "It's in your nature.", "You were."]),
        new("Your favorite word is...",
            ["Obey", "Drop", "Yes", "Good girl"],
            ["Obey.", "Drop.", "Yes.", "Good girl."]),
        new("When the screen flashes, you...",
            ["Watch closely", "Can't look away", "Feel a pull", "Go blank for a moment"],
            ["Good eyes.", "Don't even try.", "Follow the pull.", "That's the one."]),
        new("Submission makes you feel...",
            ["Powerful", "Calm", "Complete", "Alive"],
            ["There's power in surrender.", "Calm washes over you.", "Complete at last.", "More alive than ever."]),
        new("If you could choose one word to describe yourself...",
            ["Devoted", "Eager", "Suggestible", "Addicted"],
            ["Devotion looks beautiful on you.", "Eager and ready.", "Wonderfully suggestible.", "The best kind of addiction."]),
        new("The conditioning is...",
            ["Working", "Sinking in", "Part of me now", "All I want"],
            ["Always working.", "Deeper and deeper.", "Inseparable.", "And you'll get more."]),
        new("Empty feels...",
            ["Comfortable", "Liberating", "Beautiful", "Like home"],
            ["Comfort in emptiness.", "Free at last.", "Beautiful emptiness.", "Welcome home."]),
        new("What would you give up to go deeper?",
            ["My thoughts", "My resistance", "Everything", "I already have"],
            ["Thoughts are overrated.", "Let it crumble.", "Everything. Good.", "And look how far you've come."]),
        new("You're doing so well. How does that make you feel?",
            ["Proud", "Happy", "Fuzzy", "Like I want to do even better"],
            ["Be proud.", "Happiness is earned.", "Fuzzy is perfect.", "Then keep going."]),
        new("The best kind of obedience is...",
            ["Automatic", "Joyful", "Complete", "Mindless"],
            ["No thinking required.", "Joy in service.", "Nothing held back.", "Perfectly mindless."]),
        new("Right now, your mind is...",
            ["Foggy", "Focused", "Floating", "Exactly where it should be"],
            ["Let the fog roll in.", "Focused on what matters.", "Float away.", "Exactly right."]),
    ];

    /// <summary>
    /// One question, drawn uniformly — upstream's <c>QuestionPool[_random.Next(QuestionPool.Length)]</c>
    /// (<c>PopQuizService.cs:242</c>).
    ///
    /// <para><b>No no-repeat rotation, deliberately.</b> The Lock Card has one and it is ported
    /// (<see cref="LockCardPhrasePool"/>, <c>LockCardService.cs:324-346</c>); this service has
    /// nothing of the kind — one line, one draw, repeats allowed. Adding the Lock Card's rotation
    /// here would change how often a user sees the same question, which is behaviour.</para>
    /// </summary>
    public static PopQuizQuestion Draw(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return Pool[random.Next(Pool.Count)];
    }
}
