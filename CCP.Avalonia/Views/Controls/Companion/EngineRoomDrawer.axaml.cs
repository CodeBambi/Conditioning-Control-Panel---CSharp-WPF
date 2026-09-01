using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z7 — the Engine Room drawer. See the XAML header for the visual spec.
    ///
    /// <para>Two things the WPF code-behind does are NOT done here, and neither is stubbed:</para>
    /// <list type="bullet">
    ///   <item><description><c>CompanionWheelRelay.Attach(LiveActionsFeed)</c> — the relay is a WPF
    ///   helper built on WPF's routed <c>MouseWheel</c> event and has not been ported. Until an
    ///   Avalonia equivalent exists, a wheel notch over the capped live-actions list may be eaten
    ///   by that list instead of reaching the page.</description></item>
    ///   <item><description>The typed <c>ViewModel</c> accessor — <c>IEngineRoomDrawerVm</c> lives
    ///   in the WPF head, so it cannot be named from this assembly. Hosts set
    ///   <see cref="StyledElement.DataContext"/> directly, which is what that property did.</description></item>
    /// </list>
    /// </summary>
    public partial class EngineRoomDrawer : UserControl
    {
        public EngineRoomDrawer()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>The view's own strings. Bound as <c>#Root.Strings.LocX</c>, because the
        /// DataContext belongs to the host's view model.</summary>
        public EngineRoomDrawerViewModel Strings { get; } = new EngineRoomDrawerViewModel();

        /// <summary>
        /// The hero AI pill's deep link: expand the drawer, then scroll it into view.
        ///
        /// <para>The BringIntoView is deferred one dispatcher turn so it runs after the Expander's
        /// body has been measured — otherwise it scrolls to the collapsed height. The priority is
        /// <see cref="DispatcherPriority.Normal"/> and never <c>Loaded</c>: Loaded-priority work is
        /// starved in this app and the scroll would silently never happen.</para>
        ///
        /// <para>WPF wrote the viewmodel's IsExpanded when there was one and the Expander's
        /// otherwise. Here the Expander's IsExpanded is two-way bound to the viewmodel's, so one
        /// write covers both cases.</para>
        /// </summary>
        public void ExpandAndReveal()
        {
            var drawer = this.FindControl<Expander>("Drawer");
            if (drawer != null) drawer.IsExpanded = true;

            Dispatcher.UIThread.Post(() =>
            {
                try { this.BringIntoView(); }
                catch (InvalidOperationException) { /* torn down mid-scroll */ }
            }, DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// Supplies the static strings the view binds to, every one from CCP.Core's <see cref="Loc"/> —
    /// the same runtime and the same JSON the WPF head reads. This exists because WPF's
    /// {loc:Str key} markup extension derives from System.Windows.Markup.MarkupExtension and stays
    /// in the head.
    ///
    /// <para>Only the keys the markup carried literally are here. Everything else the drawer shows
    /// (DrawerNote, StatusLine, LoginPrompt, DailyLimitLabel, the clear-conversation copy, the live
    /// actions placeholder) is a viewmodel property in the original too, so it stays one.</para>
    /// </summary>
    public sealed class EngineRoomDrawerViewModel
    {
        public string LocHeader => Loc.Get("companion_engine_header");

        public string LocProviderOff => Loc.Get("companion_engine_provider_off");
        public string LocProviderCloud => Loc.Get("companion_engine_provider_cloud");
        public string LocProviderLocal => Loc.Get("companion_engine_provider_local");
        public string LocProviderCustom => Loc.Get("companion_engine_provider_custom");

        public string LocOffNote => Loc.Get("companion_engine_off_note");

        public string LocGroupCloud => Loc.Get("companion_engine_group_cloud");
        public string LocGroupLocal => Loc.Get("companion_engine_group_local");
        public string LocGroupCustom => Loc.Get("companion_engine_group_custom");

        public string LocBtnTest => Loc.Get("companion_engine_btn_test");
        public string LocBtnSetupLocal => Loc.Get("companion_engine_btn_setup_local");
        public string LocBtnSampler => Loc.Get("companion_engine_btn_sampler");

        public string LocFieldOllamaModel => Loc.Get("companion_engine_field_ollama_model");
        public string LocFieldOllamaHost => Loc.Get("companion_engine_field_ollama_host");
        public string LocFieldCustomEndpoint => Loc.Get("companion_engine_field_custom_endpoint");
        public string LocFieldCustomModel => Loc.Get("companion_engine_field_custom_model");
        public string LocFieldApiKey => Loc.Get("companion_engine_field_api_key");
        public string LocApiKeyNote => Loc.Get("companion_engine_api_key_note");
    }
}
