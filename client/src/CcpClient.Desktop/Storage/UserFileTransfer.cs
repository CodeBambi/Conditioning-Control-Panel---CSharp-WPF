using System.Text;

namespace CcpClient.Desktop.Storage;

/// <summary>
/// The bytes half of the open-or-save seam: everything that happens to a file AFTER the user has
/// chosen it, expressed over plain streams.
///
/// <para><b>Why it is a separate class from <see cref="AvaloniaUserFilePicker"/>.</b> Avalonia 12
/// marks <c>IStorageProvider</c>, <c>IStorageItem</c> and <c>IStorageFile</c>
/// <c>[NotClientImplementable]</c> and enforces it with a member user code cannot write, so a fake
/// storage provider does not compile — a real, checked constraint of the v12 API rather than a
/// preference. Splitting here is what keeps the behaviour that can go WRONG (truncation, encoding,
/// byte-order marks, the size cap, which failures are refusals) provable on a machine with no
/// desktop, and reduces the part that only a human at a dialog can exercise to the capability
/// probe and one picker call.</para>
///
/// <para>Every failure is a typed <see cref="UserFileRefusal"/>. The exception's own text never
/// escapes, because an <see cref="IOException"/>'s message carries the path of the file that
/// failed.</para>
/// </summary>
public static class UserFileTransfer
{
    /// <summary>
    /// Writes <paramref name="contents"/> to the stream the chosen file opens, as UTF-8 with no
    /// byte-order mark.
    /// </summary>
    public static async Task<UserFileSave> WriteTextAsync(Func<Task<Stream>> openWrite, string contents)
    {
        try
        {
            var stream = await openWrite();
            await using (stream)
            {
                // TRUNCATE FIRST. The desktop implementation opens FileMode.Create (Avalonia
                // 12.1.1 BclStorageItem.OpenWriteCore), but the INTERFACE promises only "opens
                // stream for writing", so overwriting a longer document with a shorter one would
                // otherwise leave the old tail behind on any backend that does not truncate — and
                // a backup file with a corrupt tail is worse than no backup at all.
                if (stream.CanSeek)
                {
                    stream.SetLength(0);
                }

                // No mark: this port's own persisted documents are written the same way
                // (Persistence/PersistenceStore.cs AtomicWriteHooks), and a byte-order mark breaks
                // a strict JSON reader on the other side.
                var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                await using (writer)
                {
                    await writer.WriteAsync(contents);
                    await writer.FlushAsync();
                }
            }

            return UserFileSave.Saved.Instance;
        }
        catch (Exception ex) when (IsStorageFault(ex))
        {
            return new UserFileSave.Refused(UserFileRefusal.WriteFailed);
        }
    }

    /// <summary>
    /// Reads the text of the stream the chosen file opens, refusing anything larger than
    /// <see cref="UserFilePicker.MaxTextBytes"/> before it is held.
    /// </summary>
    public static async Task<UserFileOpen> ReadTextAsync(Func<Task<Stream>> openRead)
    {
        try
        {
            var stream = await openRead();
            await using (stream)
            {
                using var buffered = new MemoryStream();
                var chunk = new byte[64 * 1024];
                while (true)
                {
                    var read = await stream.ReadAsync(chunk);
                    if (read == 0)
                    {
                        break;
                    }

                    if (buffered.Length + read > UserFilePicker.MaxTextBytes)
                    {
                        // Refused while the rest is still on disk, not after it is in memory.
                        return new UserFileOpen.Refused(UserFileRefusal.TooLarge);
                    }

                    buffered.Write(chunk, 0, read);
                }

                buffered.Position = 0;
                // Byte-order marks are DETECTED and dropped, which is what upstream's
                // File.ReadAllText does (Services/PhraseBackupService.cs:96) — a UTF-8 mark left
                // on the front of the string makes System.Text.Json refuse a good document.
                using var reader = new StreamReader(buffered, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return new UserFileOpen.Opened(await reader.ReadToEndAsync());
            }
        }
        catch (Exception ex) when (IsStorageFault(ex))
        {
            return new UserFileOpen.Refused(UserFileRefusal.ReadFailed);
        }
    }

    /// <summary>
    /// The faults a chosen file can legitimately produce: permissions, a removed volume, a backend
    /// that cannot do what was asked. Anything else is a defect in this program and is left to
    /// surface as one rather than laundered into a refusal code.
    /// </summary>
    private static bool IsStorageFault(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ObjectDisposedException
            or System.Security.SecurityException;
}
