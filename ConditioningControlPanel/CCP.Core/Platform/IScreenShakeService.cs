namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Applies a brief camera-shake jitter to the app's main window content, ported
/// from the WPF <c>ScreenShakeService</c> (Services/UI/ScreenShakeService.cs).
/// The shake translates the window CONTENT (a RenderTransform), it never moves
/// the window itself and never touches overlay/compositor windows. Cross-platform
/// heads that cannot host a shakeable content root register a safe no-op.
/// </summary>
public interface IScreenShakeService
{
    /// <summary>
    /// Shake the main window's content for <paramref name="durationMs"/>
    /// milliseconds at <paramref name="intensity"/> (clamped to 0..1; an
    /// intensity &lt;= 0 or a duration &lt;= 0 is a no-op). Safe to call from any
    /// thread; the implementation marshals to the UI thread.
    /// WPF: Services/UI/ScreenShakeService.cs Shake(double,int):43-49.
    /// </summary>
    void Shake(double intensity, int durationMs);
}
