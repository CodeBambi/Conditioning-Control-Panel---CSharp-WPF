using System.Text;

namespace CcpClient.Desktop.Entitlement;

/// <summary>
/// A borrowed credential that is read, used, and dropped — never logged, never persisted,
/// never copied into the port's own store, never placed in a message or an exception.
///
/// <para>Three properties make that discipline mechanical rather than a convention:</para>
/// <list type="number">
/// <item><see cref="ToString"/> is <see cref="RedactedText"/>. String interpolation is how a
/// credential usually reaches a log, and here interpolation cannot produce the value.</item>
/// <item><see cref="Reveal"/> hands back a <see cref="ReadOnlySpan{T}"/>, not a string. A span
/// cannot be captured by a lambda, stored in a field, or held across an <c>await</c>, so a
/// call site cannot accidentally keep the value alive after the handle is dropped.</item>
/// <item><see cref="Dispose"/> zeroes the backing buffer. This mirrors the shipping app, which
/// calls <c>SecurityHelper.SecureClear(plainBytes)</c> the moment it is done with the decrypted
/// bytes (Services/Auth/SecureAuthTokenStore.cs:41,67).</item>
/// </list>
///
/// <para>Honest limit: a caller that materialises a string out of the span (an HTTP header, say)
/// creates an immutable CLR string this type cannot zero. That is unavoidable at a real network
/// boundary; it is also why the value never leaves this namespace in this packet.</para>
/// </summary>
public sealed class HostAuthToken : IDisposable
{
    /// <summary>What this type renders as anywhere a string is expected. Pinned by test.</summary>
    public const string RedactedText = "host-auth-token(redacted)";

    private char[]? _chars;
    private int _length;

    /// <summary>Takes a copy of <paramref name="value"/> into a buffer this handle can zero.</summary>
    public HostAuthToken(ReadOnlySpan<char> value)
    {
        _chars = value.ToArray();
        _length = value.Length;
    }

    /// <summary>
    /// Decodes decrypted UTF-8 bytes and CLEARS the source array, so the plaintext bytes do not
    /// outlive the decode (WPF discipline, SecureAuthTokenStore.cs:67).
    /// </summary>
    public static HostAuthToken FromDecryptedUtf8(byte[] plainUtf8)
    {
        ArgumentNullException.ThrowIfNull(plainUtf8);
        try
        {
            var count = Encoding.UTF8.GetCharCount(plainUtf8, 0, plainUtf8.Length);
            var chars = new char[count];
            Encoding.UTF8.GetChars(plainUtf8, 0, plainUtf8.Length, chars, 0);
            var token = new HostAuthToken(new ReadOnlySpan<char>(chars, 0, count));
            Array.Clear(chars);
            return token;
        }
        finally
        {
            Array.Clear(plainUtf8);
        }
    }

    /// <summary>True once the value has been dropped; every read after that throws.</summary>
    public bool IsDisposed => _chars is null;

    /// <summary>True when the decrypted value carried no characters — a login that cannot be presented.</summary>
    public bool IsEmpty => _length == 0;

    /// <summary>
    /// The value, for the single moment of use. Throws once dropped rather than returning an
    /// empty span, because a silent empty here would read downstream as "no login" — the same
    /// class of conflation this packet exists to prevent.
    /// </summary>
    public ReadOnlySpan<char> Reveal()
    {
        var chars = _chars ?? throw new ObjectDisposedException(nameof(HostAuthToken));
        return new ReadOnlySpan<char>(chars, 0, _length);
    }

    /// <summary>Never the value. See the type remarks.</summary>
    public override string ToString() => RedactedText;

    public void Dispose()
    {
        var chars = _chars;
        _chars = null;
        _length = 0;
        if (chars is not null)
        {
            Array.Clear(chars);
        }
    }
}
