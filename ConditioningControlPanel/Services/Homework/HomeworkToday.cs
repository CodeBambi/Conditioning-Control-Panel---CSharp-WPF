namespace ConditioningControlPanel.Services.Homework;

/// <summary>Today's pick, as the proxy names it (the <c>homework:current</c> record minus its ballot id).</summary>
public sealed record HomeworkCurrent(string Day, string Url, string Title);

/// <summary>One answer from <c>/v2/homework/*</c>. <see cref="Idle"/> is what every failure turns into.</summary>
public sealed record HomeworkToday(bool Enabled, bool OptedIn, bool DiscordLinked, HomeworkCurrent? Current, bool Done)
{
    public static readonly HomeworkToday Idle = new(false, false, false, null, false);

    /// <summary>There is homework and this account has not handed it in.</summary>
    public bool Due => Enabled && OptedIn && Current != null && !Done;
}
