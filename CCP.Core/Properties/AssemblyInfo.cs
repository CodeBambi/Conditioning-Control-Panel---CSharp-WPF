using System.Runtime.CompilerServices;

// Both attributes are LOAD-BEARING - do not delete either. `Services/OneShotGate.cs` is
// `internal`, and it is called from the app (Services/Flash/FlashService.cs,
// Services/Subliminal/SubliminalService.cs) and from the test project, which reaches Core
// transitively through the app. Without these, those call sites are CS0122. Keeping them here
// is what lets a move of an `internal` type stay a pure `git mv` with its accessibility
// unchanged.
// GenerateAssemblyInfo is off, so this lives here rather than as an MSBuild
// <InternalsVisibleTo> item. The assembly is unsigned, so unkeyed names are correct.
[assembly: InternalsVisibleTo("ConditioningControlPanel")]
[assembly: InternalsVisibleTo("ConditioningControlPanel.Tests")]
