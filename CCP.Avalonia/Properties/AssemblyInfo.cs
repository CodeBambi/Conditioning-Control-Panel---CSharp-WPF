using System.Runtime.CompilerServices;

// Headless integration suites drive real internal controls under Avalonia's headless platform;
// keep the production surface internal while granting those dedicated processes test access.
[assembly: InternalsVisibleTo("CCP.Avalonia.Consent.Tests")]
[assembly: InternalsVisibleTo("CCP.Avalonia.Report.Tests")]
[assembly: InternalsVisibleTo("CCP.Avalonia.Tests")]
