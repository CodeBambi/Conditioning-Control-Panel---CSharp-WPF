using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;

// Expose internal decision helpers (flash cap, ambient flash duration, vout mid-play state machine,
// overlay z-order) to the unit-test assembly. GenerateAssemblyInfo is off, so this lives here rather
// than as an MSBuild <InternalsVisibleTo> item.
[assembly: InternalsVisibleTo("ConditioningControlPanel.Tests")]

[assembly: AssemblyTitle("Conditioning Control Panel")]
[assembly: AssemblyDescription("A professional visual conditioning application with gamification features.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("CodeBambi")]
[assembly: AssemblyProduct("Conditioning Control Panel")]
[assembly: AssemblyCopyright("Copyright © 2024-2025 CodeBambi")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]

// Version is set in .csproj - do not set here to avoid conflicts
// [assembly: AssemblyVersion] and [assembly: AssemblyFileVersion] are auto-generated
