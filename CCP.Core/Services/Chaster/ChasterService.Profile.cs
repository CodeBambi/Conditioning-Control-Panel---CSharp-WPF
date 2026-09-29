using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// Who is linked: the Chaster username and picture, shown on the account strip and in Settings
/// so a player can see at a glance WHICH account this PC adds time to.
///
/// <para>Asked once per link: on the first lock fetch after launch, and again right after a new
/// link. Held in memory only. A failed ask leaves the strip as it was (no error on screen) and
/// is tried again on the next lock fetch, at most <see cref="ProfileTries"/> times a link.</para>
/// </summary>
public sealed partial class ChasterService
{
    private const int ProfileTries = 3;

    private ChasterProfile? _profile;
    private byte[]? _avatarBytes;
    private int _profileTriesLeft = ProfileTries;
    private int _profileGeneration;
    private int _profileBusy;

    /// <summary>The linked account, or null when unlinked or not fetched yet.</summary>
    public ChasterProfile? Profile { get { lock (_gate) return _profile; } }

    /// <summary>The picture's raw bytes (capped at <see cref="ChasterClient.MaxAvatarBytes"/>),
    /// or null. The screen decodes them; the service never touches WPF.</summary>
    public byte[]? AvatarBytes { get { lock (_gate) return _avatarBytes; } }

    /// <summary>The profile or its picture changed. Raised on whatever thread found out.</summary>
    public event Action? ProfileChanged;

    /// <summary>Fetch the profile if this link has not got it yet. Never throws, never asks twice
    /// at once, and does nothing once the profile is in hand.</summary>
    public async Task EnsureProfileAsync(CancellationToken ct = default)
    {
        int generation;
        lock (_gate)
        {
            if (_profile != null || _profileTriesLeft <= 0) return;
            generation = _profileGeneration;
        }
        if (!IsLinked || Interlocked.Exchange(ref _profileBusy, 1) == 1) return;
        try
        {
            if (await AccessTokenAsync(ct).ConfigureAwait(false) == null) return;
            lock (_gate) _profileTriesLeft--;
            var answer = await CallWithAccessAsync(a => _client.GetProfileAsync(a, ct), ct).ConfigureAwait(false);
            if (answer is not { Ok: true } result || result.Value == null) return;

            lock (_gate)
            {
                if (generation != _profileGeneration) return; // unlinked or relinked meanwhile
                _profile = result.Value;
            }
            ProfileChanged?.Invoke();

            if (result.Value.Avatar is { } avatar)
            {
                var bytes = await _client.GetAvatarBytesAsync(avatar, ct).ConfigureAwait(false);
                if (bytes == null) return;
                lock (_gate)
                {
                    if (generation != _profileGeneration) return;
                    _avatarBytes = bytes;
                }
                ProfileChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Diag.Swallowed(ex, "chaster profile, the strip stays as it was"); }
        finally { Interlocked.Exchange(ref _profileBusy, 0); }
    }

    /// <summary>The link changed hands: forget who it was, so the next ask fetches afresh.</summary>
    private void ForgetProfile()
    {
        bool had;
        lock (_gate)
        {
            had = _profile != null || _avatarBytes != null;
            _profile = null;
            _avatarBytes = null;
            _profileTriesLeft = ProfileTries;
            _profileGeneration++;
        }
        if (had) ProfileChanged?.Invoke();
    }
}
