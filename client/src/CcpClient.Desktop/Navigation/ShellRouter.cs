namespace CcpClient.Desktop.Navigation;

/// <summary>
/// The shell's navigation model: an ordered rail and one current door. It knows nothing about
/// pages — the shell mounts those and <see cref="ShellRouteBinding"/> checks the correspondence.
///
/// <para>Unknown and null ids change NOTHING and return false, the WPF <c>else return</c>
/// posture the port already follows for quick-toggle dispatch (<c>MainWindow.Presets.cs:818</c>);
/// a typo in a route id must never leave the shell on a blank page.</para>
/// </summary>
public sealed class ShellRouter
{
    private readonly IReadOnlyList<ShellRoute> _routes;

    public ShellRouter(IReadOnlyList<ShellRoute> routes, string initialId)
    {
        ArgumentNullException.ThrowIfNull(routes);
        if (routes.Count == 0)
        {
            throw new ArgumentException("the rail must declare at least one door", nameof(routes));
        }

        var duplicate = routes.GroupBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"duplicate route id '{duplicate.Key}' — route ids are dispatch identity", nameof(routes));
        }

        _routes = routes;
        Current = routes.FirstOrDefault(r => string.Equals(r.Id, initialId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"initial route '{initialId}' is not declared", nameof(initialId));
    }

    /// <summary>The rail, in rail order.</summary>
    public IReadOnlyList<ShellRoute> Routes => _routes;

    /// <summary>The door the shell is currently showing. Never null: the shell always has a page.</summary>
    public ShellRoute Current { get; private set; }

    /// <summary>Raised only when <see cref="Current"/> actually changes.</summary>
    public event Action<ShellRoute>? Navigated;

    /// <summary>
    /// Navigate to a declared door. Returns false — and changes nothing — for null, unknown,
    /// or the door already showing. The "already showing" case returning false is deliberate:
    /// it means "nothing happened", which is what the caller needs to know.
    /// </summary>
    public bool Navigate(string? id)
    {
        if (id is null)
        {
            return false;
        }

        var target = _routes.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        if (target is null || ReferenceEquals(target, Current))
        {
            return false;
        }

        Current = target;
        Navigated?.Invoke(target);
        return true;
    }
}

/// <summary>
/// The anti-decoration invariant, made mechanical: the rail's declared doors and the shell's
/// mounted pages must be the SAME set. A declared door with no page is a door that goes
/// nowhere; a mounted page with no door is a surface no gesture can reach. Both are refused at
/// composition, loudly, rather than shipping as a rail of dead doors.
/// </summary>
public static class ShellRouteBinding
{
    public static void ValidateOrThrow(IEnumerable<string> declaredIds, IEnumerable<string> mountedIds)
    {
        var declared = declaredIds.ToArray();
        var mounted = mountedIds.ToArray();

        var doorsWithNoPage = declared.Except(mounted, StringComparer.Ordinal).ToArray();
        if (doorsWithNoPage.Length > 0)
        {
            throw new InvalidOperationException(
                $"rail declares door(s) with no mounted page: {string.Join(", ", doorsWithNoPage)} — "
                + "a door whose destination does not work must be absent, not decorative");
        }

        var pagesWithNoDoor = mounted.Except(declared, StringComparer.Ordinal).ToArray();
        if (pagesWithNoDoor.Length > 0)
        {
            throw new InvalidOperationException(
                $"shell mounts page(s) no door reaches: {string.Join(", ", pagesWithNoDoor)} — "
                + "an unreachable surface is the condition this shell exists to end");
        }
    }
}
