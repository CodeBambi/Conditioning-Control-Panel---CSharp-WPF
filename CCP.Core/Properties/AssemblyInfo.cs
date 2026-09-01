using System.Runtime.CompilerServices;

// BOTH ATTRIBUTES ARE LOAD-BEARING — do not delete them. Core now holds `internal` types whose
// accessibility must not change: HighPassFilter / NoiseGate / PreRollBuffer
// (Services/Speech/MicFrontEnd.cs), CompanionTitleMatcher, FunScriptJsonExtensions and
// LovensePatterns.Serialize. The app constructs the first three (Services/Speech/
// SherpaWakeService.cs, SpeechService.cs) and the test project (which reaches Core
// transitively through the app) calls into CompanionTitleMatcher. Removing either attribute
// breaks that project's build. Having this here is what keeps such moves a pure `git mv`.
// GenerateAssemblyInfo is off, so this lives here rather than as an MSBuild
// <InternalsVisibleTo> item. The assembly is unsigned, so unkeyed names are correct.
[assembly: InternalsVisibleTo("ConditioningControlPanel")]
[assembly: InternalsVisibleTo("ConditioningControlPanel.Tests")]
