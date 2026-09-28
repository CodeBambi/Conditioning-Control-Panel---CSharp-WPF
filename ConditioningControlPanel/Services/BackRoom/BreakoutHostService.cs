using System;

namespace ConditioningControlPanel.Services.BackRoom;

/// <summary>Dedicated Breakout doors share the room's media, panic and window lifecycle.</summary>
public static class BreakoutHostService
{
    public const string StartUrl = "https://ccp.game/backroom/stations/breakout/play.html?desktop=1";
    public static bool IsActive => BackRoomHostService.IsBreakoutActive;
    internal static Action<bool> OpenHost { get; set; } = BackRoomHostService.LaunchBreakout;
    internal static Func<bool> DemandFull { get; set; } = BreakoutAccess.DemandFull;

    public static void LaunchDemo() => OpenHost(true);
    public static void LaunchFull()
    {
        if (DemandFull()) OpenHost(false);
    }
}
