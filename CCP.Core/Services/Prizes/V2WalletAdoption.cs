using System;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// WHETHER A COUNTER REPLY MAY MOVE THE WALLET, and to what.
///
/// <para>Two ways this went wrong before the rule existed. (1) A reply is adopted on the UI thread,
/// after an await, so the account can have changed between the server answering and the write
/// landing: account A's balance would be saved into account B's settings. (2) A plain
/// <c>counter/state</c> read carries <c>sp</c> too, and the client credits level-up and bubble
/// Sparkle Points LOCALLY before any sync pushes them - so opening the Flashes options right after
/// a level-up would overwrite the wallet with the older server number and persist it.
/// <see cref="Models.SparklePoints.MergeMax"/> cannot recover that: it takes the higher of the two,
/// and by then both are the lower value.</para>
///
/// <para>So: a BUY settlement is the server's word and is adopted in both directions, because the
/// server just debited under its own locks and the counter's netSp keeps a later sync from
/// refunding it. Anything else may only raise the balance, never lower it.</para>
///
/// <para>Pure. The caller does the account check and the write.</para>
/// </summary>
public static class V2WalletAdoption
{
    /// <summary>
    /// What to do with <paramref name="serverSp"/>.
    /// </summary>
    /// <param name="sameAccount">The account that the request was sent for is still the one signed in.</param>
    /// <param name="fromBuy">This is a buy settlement (a receipt, or an <c>owned</c> reply to a buy we sent).</param>
    /// <param name="serverSp">The balance in the reply.</param>
    /// <param name="localSp">The balance the wallet holds now.</param>
    /// <param name="next">What to write. Only meaningful when this answers true.</param>
    /// <returns>True when the wallet should be written.</returns>
    public static bool Decide(bool sameAccount, bool fromBuy, int serverSp, int localSp, out int next)
    {
        next = localSp;
        if (!sameAccount || serverSp < 0) return false;
        // A receipt is settled money and wins outright, down as well as up.
        if (fromBuy) { next = serverSp; return next != localSp; }
        // A read is a snapshot and may be behind a local credit, so it can only raise.
        next = Math.Max(serverSp, localSp);
        return next != localSp;
    }
}
