using System.Runtime.CompilerServices;

// Placed ahead of need: every type moved here so far is public, so neither attribute
// does anything yet. Later units move `internal` types whose accessibility must not change,
// and both the app and the test project (which reaches Core transitively through the app) call
// into them. Having this in the bootstrap keeps those moves to a pure `git mv`.
// GenerateAssemblyInfo is off, so this lives here rather than as an MSBuild
// <InternalsVisibleTo> item. The assembly is unsigned, so unkeyed names are correct.
[assembly: InternalsVisibleTo("ConditioningControlPanel")]
[assembly: InternalsVisibleTo("ConditioningControlPanel.Tests")]
