using System.Text;

namespace CcpClient.Desktop.Capabilities;

/// <summary>
/// Kernel mount-table evidence for the filesystem probe (contract §2 rule 4: environment
/// evidence may DOWNGRADE a probe result, never upgrade). Production reads
/// /proc/self/mounts; tests inject.
/// </summary>
public interface IMountTable
{
    /// <summary>The filesystem type serving <paramref name="path"/>, or null when unknown/not applicable (e.g., Windows).</summary>
    string? FilesystemTypeOf(string path);
}

/// <summary>
/// /proc/self/mounts as kernel-reported evidence. Longest mount-point prefix match on the
/// full resolved path. Absent /proc (Windows) yields null — no downgrade.
/// </summary>
public sealed class ProcMountsTable : IMountTable
{
    private const string MountsPath = "/proc/self/mounts";

    public string? FilesystemTypeOf(string path)
    {
        if (!File.Exists(MountsPath))
        {
            return null;
        }

        string? bestType = null;
        var bestLength = -1;
        foreach (var line in File.ReadLines(MountsPath))
        {
            // Format: device mountpoint fstype options dump pass (mountpoint octal-escaped).
            var fields = line.Split(' ');
            if (fields.Length < 3)
            {
                continue;
            }

            var mountPoint = Unescape(fields[1]);
            if ((path.Equals(mountPoint, StringComparison.Ordinal) ||
                 path.StartsWith(mountPoint.TrimEnd('/') + "/", StringComparison.Ordinal)) &&
                mountPoint.Length > bestLength)
            {
                bestType = fields[2];
                bestLength = mountPoint.Length;
            }
        }

        return bestType;
    }

    private static string Unescape(string value) =>
        value.Replace("\\040", " ", StringComparison.Ordinal)
             .Replace("\\011", "\t", StringComparison.Ordinal)
             .Replace("\\012", "\n", StringComparison.Ordinal)
             .Replace("\\134", "\\", StringComparison.Ordinal);
}

/// <summary>
/// Demonstrator 2 (contract §8.2): an exercised-backend capability. Performs the
/// persistence store's filesystem guarantees FOR REAL in the actual settings data
/// directory — create dir, temp write, flush-to-disk, rename-over-existing,
/// quarantine-style move, cleanup — then applies the mount-table downgrade on
/// passthrough/network filesystems. Never infers from OS strings; the I/O gates
/// Available, the kernel mount evidence may only downgrade.
/// </summary>
public static class AtomicFileSystemProbe
{
    /// <summary>
    /// Dedicated probe-file prefix: never the store's settings.json.tmp shape, so the
    /// store's crash-recovery temp handling can never adopt or delete probe files
    /// (contract §8.2).
    /// </summary>
    private const string ProbePrefix = "ccp-capability-probe-";

    private const string Surviving = "temp write, rename-over-existing, and quarantine move verified by real I/O";

    /// <summary>Passthrough/network filesystems whose fsync honoring cannot be verified in-process (contract §8.2).</summary>
    private static readonly HashSet<string> PassthroughFilesystems = new(StringComparer.Ordinal)
    {
        "9p", "drvfs", "cifs", "smbfs", "nfs", "nfs4",
    };

    public static CapabilityState Probe(string directory, IMountTable mounts)
    {
        try
        {
            ExerciseRealIo(directory);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new CapabilityState.PermissionRequired(new CapabilityReason(
                CapabilityReasonCodes.IoFailure, $"write access denied in '{directory}': {ex.Message}"));
        }
        catch (IOException ex)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                CapabilityReasonCodes.IoFailure, $"filesystem operation failed in '{directory}': {ex.Message}"));
        }

        var fsType = mounts.FilesystemTypeOf(ResolveFullPath(directory));
        if (fsType is not null && PassthroughFilesystems.Contains(fsType))
        {
            return new CapabilityState.Degraded(Surviving, new CapabilityReason(
                CapabilityReasonCodes.DurabilityUnverified,
                $"flush-to-disk honoring cannot be verified in-process: '{directory}' is served by passthrough/network filesystem '{fsType}'"));
        }

        return new CapabilityState.Available($"{Surviving} in '{directory}'" +
            (fsType is null ? "" : $" on filesystem '{fsType}'"));
    }

    private static void ExerciseRealIo(string directory)
    {
        Directory.CreateDirectory(directory);

        // Clean leftovers from an interrupted earlier probe run before starting.
        foreach (var leftover in Directory.EnumerateFiles(directory, ProbePrefix + "*"))
        {
            File.Delete(leftover);
        }

        var target = Path.Combine(directory, ProbePrefix + "target");
        var temp = Path.Combine(directory, ProbePrefix + "tmp");
        var quarantine = Path.Combine(directory, ProbePrefix + "quarantine");

        // First content exists so the second rename is genuinely rename-OVER-EXISTING.
        WriteFlush(temp, "first");
        File.Move(temp, target, overwrite: true);
        WriteFlush(temp, "second");
        File.Move(temp, target, overwrite: true);

        // Quarantine-style move (contract §8.2: preserved, never overwritten in place).
        File.Move(target, quarantine);
        if (File.ReadAllText(quarantine) != "second")
        {
            throw new IOException("quarantine move lost content");
        }

        File.Delete(quarantine);
    }

    private static void WriteFlush(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static string ResolveFullPath(string directory)
    {
        var full = Path.GetFullPath(directory);
        try
        {
            return new DirectoryInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
        }
        catch (IOException)
        {
            return full;
        }
    }
}
