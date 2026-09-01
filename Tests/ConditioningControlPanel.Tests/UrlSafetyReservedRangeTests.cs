using System.Net;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Boundary tests for <see cref="UrlSafety.IsPrivateOrReservedIp"/>.
///
/// The multicast guard used to read <c>b[0] &gt;= 224 &amp;&amp; b[0] &lt;= 239</c>, so 240.0.0.0/4
/// (RFC 1112 class E) and 255.255.255.255 fell through as "public". That is reachable:
/// EnhancementFetcher resolves user-supplied URLs through CreateGuardedHandler and
/// IsSafePublicHttpsAsync, both of which gate on this method.
/// </summary>
public class UrlSafetyReservedRangeTests
{
    [Theory]
    // The regression: reserved and broadcast space above the old 239 bound.
    [InlineData("240.0.0.1")]
    [InlineData("250.1.2.3")]
    [InlineData("255.255.255.254")]
    [InlineData("255.255.255.255")]
    public void ReservedAndBroadcastSpace_IsRejected(string address)
        => Assert.True(UrlSafety.IsPrivateOrReservedIp(IPAddress.Parse(address)));

    [Theory]
    // Ranges that were already rejected — proving the widened bound did not disturb them.
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]      // CGNAT
    [InlineData("224.0.0.1")]       // multicast, low edge
    [InlineData("239.255.255.255")] // multicast, high edge
    public void PrivateAndMulticast_StillRejected(string address)
        => Assert.True(UrlSafety.IsPrivateOrReservedIp(IPAddress.Parse(address)));

    [Theory]
    // Genuinely routable addresses must still pass, or the fetcher blocks everything.
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("223.255.255.255")] // last address below the multicast boundary
    public void PublicAddresses_StillAllowed(string address)
        => Assert.False(UrlSafety.IsPrivateOrReservedIp(IPAddress.Parse(address)));

    [Fact]
    public void IPv4MappedReservedAddress_IsRejectedThroughTheV6Path()
        => Assert.True(UrlSafety.IsPrivateOrReservedIp(IPAddress.Parse("240.0.0.1").MapToIPv6()));

    [Fact]
    public void NullAddress_FailsClosed()
        => Assert.True(UrlSafety.IsPrivateOrReservedIp(null!));
}
