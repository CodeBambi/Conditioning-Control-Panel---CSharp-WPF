using System.Runtime.CompilerServices;

// LOAD-BEARING — do not delete as unused scaffolding. Helpers/SchedulerTime.cs is an
// `internal static class` whose accessibility must not change on the move, and both the app
// (MainWindow/MainWindow.StartStop.cs calls SchedulerTime.TryParse) and the test project
// (which reaches Core transitively through the app) call into it. Removing either attribute
// is CS0122 in that project. Keeping this here is what lets such moves stay a pure `git mv`.
// GenerateAssemblyInfo is off, so this lives here rather than as an MSBuild
// <InternalsVisibleTo> item. The assembly is unsigned, so unkeyed names are correct.
[assembly: InternalsVisibleTo("ConditioningControlPanel")]
[assembly: InternalsVisibleTo("ConditioningControlPanel.Tests")]
