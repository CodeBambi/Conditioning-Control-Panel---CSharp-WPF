using System.Collections.Generic;

namespace ConditioningControlPanel
{
    /// <summary>
    /// WPF facade for <see cref="Core.Services.Chaos.ChaosImagePool"/>. The TTL-cached image listing now
    /// lives in CCP.Core (shared with the Avalonia head); this keeps the legacy parameterless
    /// <c>ChaosImagePool.GetFiles()</c> call sites working by binding the assets path from
    /// <see cref="App.EffectiveAssetsPath"/>.
    /// </summary>
    internal static class ChaosImagePool
    {
        public static List<string> GetFiles() =>
            Core.Services.Chaos.ChaosImagePool.GetFiles(App.EffectiveAssetsPath ?? "");
    }
}
