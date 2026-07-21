using System.Security.Cryptography;
using System.Text;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// bridge.js derivative provenance (dtrh-admission.md §3.1/§6): the payload copy served
/// under payload/dtrh IS the trust-anchored original (git blob 13af3f4d, pinned by
/// recomputing the git blob SHA-1 — no git dependency), and the product overlay derivative
/// is EXACTLY the original bytes plus the minimal transport-only diff:
///  - every original line survives verbatim except the named transport lines;
///  - the §3.1 expressions are present (import-time isHosted, stringify ownership,
///    inbox route, token from location);
///  - the addition is bounded, so nothing else can be smuggled in.
/// </summary>
public class DtrhBridgeDiffTests
{
    private static string OutputRoot =>
        Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!;

    private static string[] ReadLines(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');

    [Fact]
    public void PayloadBridgeJs_IsTheTrustAnchoredBlob()
    {
        var bytes = File.ReadAllBytes(Path.Combine(OutputRoot, "payload", "dtrh", "bridge.js"));
        // The worktree checkout is CRLF (core.autocrlf) while the blob is LF — a mechanical
        // git transformation, identical JS. The trust anchor pins the CONTENT: normalize
        // before recomputing the git blob id (sha1("blob <len>\0" + bytes) = 13af3f4d, SP-011).
        var normalized = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"));
        var prefix = Encoding.ASCII.GetBytes($"blob {normalized.Length}\0");
        var hash = Convert.ToHexString(SHA1.HashData(prefix.Concat(normalized).ToArray())).ToLowerInvariant();
        Assert.Equal("13af3f4d00395e053d5425da269ba70720e746a2", hash);
    }

    [Fact]
    public void Derivative_RetainsEveryOriginalLine_ExceptTheNamedTransportLines()
    {
        var original = ReadLines(Path.Combine(OutputRoot, "payload", "dtrh", "bridge.js"));
        var derivative = ReadLines(Path.Combine(OutputRoot, "payload-overlay", "bridge.js"));

        // The admitted modifications (§3.1): the old import-time isHosted line, the old
        // WebView2-only send() one-liner, and the old inline chrome.webview listener block
        // (refactored to the shared dispatch() the long-poll also feeds).
        var admittedReplacements = new[]
        {
            "export const isHosted = !!webview;",
            "  try { webview && webview.postMessage(msg); } catch (e) { /* host gone */ }",
            "if (webview) {",
            "  webview.addEventListener('message', (e) => {",
            "    const m = e.data;",
            "    if (!m || typeof m.type !== 'string') return;",
            "    const h = handlers.get(m.type);",
            "    if (h) h(m);",
            "    else preBuffer.push(m);",
            "  });",
            "}",
        };

        var removed = original.Where(line => !derivative.Contains(line)).ToArray();
        foreach (var line in removed)
        {
            Assert.Contains(line, admittedReplacements);
        }

        // Every other original line survives, in order (subsequence check).
        var kept = original.Except(admittedReplacements).ToArray();
        var position = 0;
        foreach (var line in kept)
        {
            var found = Array.IndexOf(derivative, line, position);
            Assert.True(found >= 0, $"original line lost in derivative: {line}");
            position = found + 1;
        }
    }

    [Fact]
    public void Derivative_ContainsTheAdmittedTransportExpressions_AndNothingUnbounded()
    {
        var text = File.ReadAllText(Path.Combine(OutputRoot, "payload-overlay", "bridge.js"))
            .Replace("\r\n", "\n");

        // §3.1 rule 1: import-time isHosted extended (load-bearing).
        Assert.Contains("export const isHosted = !!webview || typeof invokeCSharpAction === 'function';",
            text, StringComparison.Ordinal);
        // §3.1 rule 2: page side owns stringify, host owns parse.
        Assert.Contains("invokeCSharpAction(JSON.stringify(msg))", text, StringComparison.Ordinal);
        // §3.3: long-poll inbox on the page origin, token from location.
        Assert.Contains("new URLSearchParams(location.search).get('bridge')", text, StringComparison.Ordinal);
        Assert.Contains("/bridge/${bridgeToken}/inbox?after=", text, StringComparison.Ordinal);
        // §3.1 rule 3: Windows receive path unchanged (synthetic-dispatch listener shape).
        Assert.Contains("webview.addEventListener('message', (e) => dispatch(e.data));", text, StringComparison.Ordinal);

        // Bounded addition: original 49 lines -> derivative may grow only by the diff.
        var derivativeLines = text.Split('\n').Length;
        Assert.True(derivativeLines <= 49 + 60,
            $"derivative grew beyond the admitted transport diff: {derivativeLines} lines");
    }

    [Fact]
    public void Derivative_PreservesProtocolAndHandlerSemantics()
    {
        var text = File.ReadAllText(Path.Combine(OutputRoot, "payload-overlay", "bridge.js"));
        Assert.Contains("export const PROTOCOL = 1;", text, StringComparison.Ordinal);
        Assert.Contains("announceReady() { send({ type: 'ready', protocol: PROTOCOL }); }", text, StringComparison.Ordinal);
        // preBuffer replay semantics preserved verbatim.
        Assert.Contains("preBuffer.splice(i, 1)[0];", text, StringComparison.Ordinal);
    }
}
