using System.Runtime.CompilerServices;

// Both attributes are now load-bearing. `internal` types live here (FrameFormatter,
// DeeperConfig, ArcademyEconomy, ArcademyPunchCards), and the app and the test project (which
// reaches Core transitively through the app) call into them. Their accessibility must not
// change, so these lines are what keeps such a move a pure `git mv`. Deleting either one
// breaks the app build.
// GenerateAssemblyInfo is off, so this lives here rather than as an MSBuild
// <InternalsVisibleTo> item. The assembly is unsigned, so unkeyed names are correct.
[assembly: InternalsVisibleTo("ConditioningControlPanel")]
[assembly: InternalsVisibleTo("ConditioningControlPanel.Tests")]
