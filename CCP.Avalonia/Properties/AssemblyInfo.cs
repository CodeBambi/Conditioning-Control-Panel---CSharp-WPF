using System.Runtime.CompilerServices;

// The consent regression suite drives the real internal shell door under Avalonia's headless
// platform; keep the production surface internal while granting that dedicated process its test
// access.
[assembly: InternalsVisibleTo("CCP.Avalonia.Consent.Tests")]
[assembly: InternalsVisibleTo("CCP.Avalonia.Report.Tests")]
