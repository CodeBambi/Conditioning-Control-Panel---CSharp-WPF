using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class DtrhRemoteKindsTests
{
    [Fact]
    public void AllClipCacheReportsNoStills()
    {
        var cache = new System.Collections.Generic.List<DtrhAssetManifest.RemoteEntry>();
        for (int i = 0; i < 30; i++) cache.Add(new DtrhAssetManifest.RemoteEntry($"s/x/{i}", $"https://a/{i}.webm", false, 0));
        Assert.Equal((0, 30), DtrhAssetManifest.CountKinds(cache));
        cache.Add(new DtrhAssetManifest.RemoteEntry("s/x/p", "https://a/p.webp", true, 0));
        Assert.Equal((1, 30), DtrhAssetManifest.CountKinds(cache));
        Assert.Equal((0, 0), DtrhAssetManifest.CountKinds(null));
    }
}
