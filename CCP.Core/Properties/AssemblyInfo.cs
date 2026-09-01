using System.Runtime.CompilerServices;

// Both attributes are LOAD-BEARING - do not delete either as unused scaffolding.
//
// Core holds `internal` types that are called from the app and from the test project (which
// reaches Core transitively through the app). Without these attributes those call sites are
// CS0122. Keeping them here is what lets a move of an `internal` type stay a pure `git mv`
// with its accessibility unchanged - which is the whole contract the migration relies on.
//
// Deliberately not naming specific types: every unit that moves an internal one would have to
// edit this comment, and four of them have already collided here.
//
// GenerateAssemblyInfo is off, so this lives here rather than as an MSBuild
// <InternalsVisibleTo> item. The assembly is unsigned, so unkeyed names are correct.
[assembly: InternalsVisibleTo("ConditioningControlPanel")]
[assembly: InternalsVisibleTo("ConditioningControlPanel.Tests")]
