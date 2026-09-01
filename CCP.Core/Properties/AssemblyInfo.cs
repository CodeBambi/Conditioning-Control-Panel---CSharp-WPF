using System.Runtime.CompilerServices;

// Files moving into Core keep their original accessibility; several are `internal` and are
// reached both by the app and by the existing test project. GenerateAssemblyInfo is off, so
// this lives here rather than as an MSBuild <InternalsVisibleTo> item. The assembly is
// unsigned, so unkeyed names are correct.
[assembly: InternalsVisibleTo("ConditioningControlPanel")]
[assembly: InternalsVisibleTo("ConditioningControlPanel.Tests")]
