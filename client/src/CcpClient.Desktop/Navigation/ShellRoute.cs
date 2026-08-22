namespace CcpClient.Desktop.Navigation;

/// <summary>
/// One door in the shell's rail. <see cref="Id"/> is dispatch identity and never a display
/// string (the lesson learned: the rendered label is mutable display text and must never
/// participate in routing); <see cref="Label"/> and <see cref="Tooltip"/> are display text.
///
/// <para>The record carries NO page. The rail's pages are mounted by the shell, and the
/// door-to-page correspondence is checked by <see cref="ShellRouteBinding"/> — which is what
/// keeps this type free of Avalonia and unit-testable outside the headless runtime.</para>
/// </summary>
public sealed record ShellRoute(string Id, string Label, string Tooltip);
