using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;

// Expose internal decision helpers (flash cap, ambient flash duration, vout mid-play state machine,
// overlay z-order) to the unit-test assembly.
[assembly: InternalsVisibleTo("ConditioningControlPanel.Tests")]

// Only attributes the SDK does NOT generate belong here. GenerateAssemblyInfo is ON, so
// AssemblyTitle/Description/Configuration/Company/Product/Copyright/Version/FileVersion/
// InformationalVersion all come from the .csproj - declaring any of them here is a
// CS0579 duplicate-attribute error.
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
