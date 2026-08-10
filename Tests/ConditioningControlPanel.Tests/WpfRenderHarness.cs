using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The themed <see cref="Application"/> that the Settings section render suites need, and the one
/// STA thread they share.
///
/// <para><b>Why the host-panel trick is not enough.</b> The Companion suites merge the theme into a
/// host <see cref="System.Windows.Controls.Grid"/> and add the control to it. That works there
/// because those cells reach app-level keys through <c>{DynamicResource}</c>, resolved lazily once
/// the control has a parent. The Settings sections use <c>{StaticResource}</c> for the same kind of
/// key (<c>SectionHeader</c>, <c>SettingLabel</c>, <c>ToggleStyle</c>, <c>DarkComboBoxStyle</c>) —
/// and a StaticResource is resolved DURING <c>InitializeComponent()</c>, before the control has been
/// added to anything. At that moment the only dictionaries in scope are the control's own and
/// <c>Application.Current.Resources</c>. With no Application, every section throws in its
/// constructor. That is a harness gap, not a product bug: at runtime App.xaml puts those
/// dictionaries on the Application and the lookup succeeds.</para>
///
/// <para><b>Why the theme comes from the product's own App.xaml.</b> Every hand-built equivalent
/// tried here is subtly broken. <c>Resources/Theme/MainWindow.xaml</c> carries setters like
/// <c>&lt;Setter Property="Foreground" Value="{StaticResource TextLightBrush}"/&gt;</c> whose key
/// lives in the sibling <c>Brushes.xaml</c>, and that cross-dictionary StaticResource resolves only
/// under App.xaml's own compiled document. A <c>ResourceDictionary</c> per file built in C#, a
/// synthesized merge parsed with <c>XamlReader</c> (absolute pack URIs, or relative sources under a
/// <c>ParserContext.BaseUri</c>), and a compiled mirror in this test assembly were all tried: each
/// leaves those setters holding <c>DependencyProperty.UnsetValue</c>, and the damage surfaces
/// nowhere near the cause — a TextBlock wearing <c>SettingLabel</c> throws
/// "'{DependencyProperty.UnsetValue}' is not a valid value for property 'Foreground'" inside
/// <c>MeasureOverride</c>. Constructing <see cref="ConditioningControlPanel.App"/> and calling its
/// generated <c>InitializeComponent()</c> loads the real thing. It is safe: that only loads
/// resources, while <c>OnStartup</c> (services, windows, logging) runs from <c>Run()</c>, which is
/// never called here, and the App type's static initializers are pure (<c>UserDataPath</c> reads an
/// environment variable and combines a path — it creates nothing).</para>
///
/// <para><b>Why the theme is installed only while a body runs.</b> The Companion, wardrobe and
/// session-lock suites each render on their own short-lived STA thread, and they are left exactly as
/// they were. The Brushes and Styles in a shared dictionary take thread affinity from whichever
/// thread first realizes them, so a permanently themed <c>Application.Resources</c> would put this
/// thread's Freezables in their <c>{DynamicResource}</c> fall-through path and they would die with
/// "The calling thread cannot access this object because a different thread owns it". Swapping the
/// whole dictionary in and out around each body keeps the two worlds apart; every WPF suite shares
/// one xUnit collection, so nothing else can be mid-render while it is installed.</para>
/// </summary>
internal static class WpfRenderHarness
{
    /// <summary>
    /// The dictionaries App.xaml merges, for suites that also want a themed local host. Kept in
    /// App.xaml's order.
    /// </summary>
    internal static readonly string[] AppThemeDictionaries =
    {
        "Resources/Theme/Colors.xaml",
        "Resources/Theme/Brushes.xaml",
        "Resources/Theme/Controls.xaml",
        "Resources/Theme/Converters.xaml",
        "Resources/Theme/MainWindow.xaml"
    };

    private static readonly object Gate = new();
    private static bool _ready;
    private static Dispatcher? _dispatcher;

    /// <summary>The app theme, parked while no render body is running.</summary>
    private static ResourceDictionary? _theme;

    /// <summary>What Application.Resources holds the rest of the time: nothing.</summary>
    private static readonly ResourceDictionary Empty = new();

    /// <summary>
    /// Brings up the STA thread and the themed Application. Safe to call from every test; the first
    /// call does the work and the rest are a lock and a bool.
    /// </summary>
    internal static void EnsureApplication()
    {
        lock (Gate)
        {
            if (_ready) return;

            Exception? escaped = null;
            using var ready = new ManualResetEventSlim(false);

            var pump = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null)
                    {
                        _ = new Application
                        {
                            // Nothing here opens a Window, so the default OnLastWindowClose would
                            // shut the Application down the moment a test closed one.
                            ShutdownMode = ShutdownMode.OnExplicitShutdown
                        };
                    }

                    // Merged into a live, Application-owned dictionary one at a time and in
                    // App.xaml's order. That ordering is not cosmetic: Resources/Theme/
                    // MainWindow.xaml carries setters like
                    //   <Setter Property="Foreground" Value="{StaticResource TextLightBrush}"/>
                    // whose key lives in Brushes.xaml, and those resolve against what is already
                    // merged when MainWindow.xaml's deferred content is first realized. Build the
                    // set off to the side and assign it in one go and they resolve to
                    // DependencyProperty.UnsetValue instead - which surfaces far from the cause, as
                    // a TextBlock wearing SettingLabel throwing inside MeasureOverride.
                    _theme = new ResourceDictionary();
                    Application.Current!.Resources = _theme;
                    foreach (var relative in AppThemeDictionaries)
                    {
                        _theme.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri(
                                "pack://application:,,,/ConditioningControlPanel;component/" + relative,
                                UriKind.Absolute)
                        });
                    }

                    Application.Current.Resources = Empty;

                    _dispatcher = Dispatcher.CurrentDispatcher;
                }
                catch (Exception ex) { escaped = ex; }
                finally { ready.Set(); }

                // Keep the Application's dispatcher alive for the rest of the run, so anything
                // reaching for Application.Current.Dispatcher finds a pump and not a dead thread.
                if (escaped == null) Dispatcher.Run();
            });

            pump.SetApartmentState(ApartmentState.STA);
            pump.IsBackground = true;   // never keeps the test host alive
            pump.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(30)))
                throw new InvalidOperationException("WPF render harness: Application bootstrap timed out");
            if (escaped != null)
                throw new InvalidOperationException("WPF render harness: Application bootstrap failed", escaped);

            _ready = true;
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> on the harness thread with the app theme installed, and
    /// rethrows whatever escaped it with the original stack attached.
    /// </summary>
    internal static void OnStaThread(Action body, int timeoutSeconds = 60)
    {
        EnsureApplication();

        Exception? escaped = null;
        var op = _dispatcher!.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            var app = Application.Current;
            try
            {
                if (app != null && _theme != null) app.Resources = _theme;
                body();
            }
            catch (Exception ex) { escaped = ex; }
            finally
            {
                if (app != null) app.Resources = Empty;
            }
        }));

        if (op.Wait(TimeSpan.FromSeconds(timeoutSeconds)) != DispatcherOperationStatus.Completed)
            throw new Xunit.Sdk.XunitException("STA render thread did not finish in time");
        if (escaped != null)
            throw new Xunit.Sdk.XunitException(escaped.ToString());
    }
}
