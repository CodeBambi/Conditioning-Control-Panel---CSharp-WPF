namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// The leash rides the friends poll (CONTRACT "The poll piggyback"). While the account is leashed
/// or holds anyone, the poll runs every 20 s whenever the app is open, drawer or no drawer,
/// foreground or not. Pure: it only ever makes the friends cadence faster, never slower, and it
/// never polls a signed-out account.
/// </summary>
public static class LeashPollRule
{
    public const int FastSeconds = 20;

    /// <param name="friendsSeconds">What <c>FriendsPollRule</c> said (0 = do not poll).</param>
    /// <param name="leashActive">The last snapshot says leashed or holding (<see cref="LeashSnapshot.Active"/>).</param>
    public static int NextIntervalSeconds(int friendsSeconds, bool leashActive)
    {
        if (friendsSeconds <= 0) return friendsSeconds;
        return leashActive && friendsSeconds > FastSeconds ? FastSeconds : friendsSeconds;
    }
}
