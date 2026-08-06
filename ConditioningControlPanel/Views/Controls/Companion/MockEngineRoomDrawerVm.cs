using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IEngineRoomDrawerVm"/>.
    /// </summary>
    public sealed class MockEngineRoomDrawerVm : CompanionObservable, IEngineRoomDrawerVm
    {
        private bool _isExpanded;
        private CompanionProviderMode _provider = CompanionProviderMode.Cloud;
        private string _ollamaModel = "qwen3.5:latest";
        private string _ollamaHost = "http://localhost:11434/";
        private string _customEndpoint = string.Empty;
        private string _customApiKey = string.Empty;
        private string _customModel = string.Empty;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockEngineRoomDrawerVm()
        {
            LiveActions = Array.Empty<string>();
            LoginCommand = CompanionRelayCommand.NoOp("engine.login");
            TestConnectionCommand = CompanionRelayCommand.NoOp("engine.test");
            SetupLocalCommand = CompanionRelayCommand.NoOp("engine.setupLocal");
            SamplerSettingsCommand = CompanionRelayCommand.NoOp("engine.sampler");
            DailyLimitCommand = CompanionRelayCommand.NoOp("engine.dailyLimit");
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public CompanionProviderMode Provider
        {
            get => _provider;
            set => Set(ref _provider, value);
        }

        public string DrawerNote { get; init; } = "wiring lives here on purpose. she'd rather you didn't stare.";

        public bool IsLoggedIn { get; init; } = true;
        public string LoginPrompt { get; init; } = "cloud needs a Lab login before she can think out there.";
        public string LoginButtonLabel { get; init; } = "Log in";
        public string StatusLine { get; init; } =
            "● Connected — cloud proxy · logged in as GoodGirl#4127 · purpose tiers: chat / reaction / utility";
        public bool IsHealthy { get; init; } = true;

        public string OllamaModel { get => _ollamaModel; set => Set(ref _ollamaModel, value); }
        public string OllamaHost { get => _ollamaHost; set => Set(ref _ollamaHost, value); }
        public string CustomEndpoint { get => _customEndpoint; set => Set(ref _customEndpoint, value); }
        public string CustomApiKey { get => _customApiKey; set => Set(ref _customApiKey, value); }
        public string CustomModel { get => _customModel; set => Set(ref _customModel, value); }

        public string DailyLimitLabel { get; init; } = "Daily limit: 200";

        public bool ShowLiveActions { get; init; }
        public IReadOnlyList<string> LiveActions { get; init; }
        public string LiveActionsPlaceholder { get; init; } =
            "Live actions feed (local effects channel) docks here when Local is active.";

        public ICommand LoginCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand SetupLocalCommand { get; }
        public ICommand SamplerSettingsCommand { get; }
        public ICommand DailyLimitCommand { get; }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>The artboard: cloud, connected, drawer open so the interior is visible.</summary>
        public static MockEngineRoomDrawerVm Cloud() => new() { IsExpanded = true };

        /// <summary>Logged out — an inline row here, never a veil over the whole page any more.</summary>
        public static MockEngineRoomDrawerVm LoggedOut() => new()
        {
            IsExpanded = true,
            IsLoggedIn = false,
            IsHealthy = false,
            StatusLine = "○ Not connected — log in to use the cloud proxy"
        };

        /// <summary>Local Ollama, with the live actions feed docked at the bottom.</summary>
        public static MockEngineRoomDrawerVm LocalOllama() => new()
        {
            IsExpanded = true,
            Provider = CompanionProviderMode.LocalOllama,
            StatusLine = "● Local — qwen3.5:latest on localhost:11434",
            ShowLiveActions = true,
            LiveActions = new[]
            {
                "flash burst · 6 images",
                "spiral opacity → 80%",
                "whisper: “good girl~”"
            }
        };

        /// <summary>
        /// Custom (BYO): the endpoint / key / model panel, its sampler + daily-limit buttons, and
        /// nothing else. Without this exhibit the BYO panel would never be rendered by the gallery
        /// or the smoke test — the provider grouping hides it under every other exhibit.
        /// </summary>
        public static MockEngineRoomDrawerVm Custom() => new()
        {
            IsExpanded = true,
            Provider = CompanionProviderMode.Custom,
            StatusLine = "● Custom — your endpoint, your key, your bill",
            CustomEndpoint = "https://api.example.com/v1",
            CustomApiKey = "••••••••••",
            CustomModel = "your-model-here"
        };

        /// <summary>Provider Off: everything is present but nothing is thinking.</summary>
        public static MockEngineRoomDrawerVm Off() => new()
        {
            Provider = CompanionProviderMode.Off,
            IsHealthy = false,
            StatusLine = "○ Off — she runs on her voice alone"
        };

        /// <summary>The resting state on the page: a closed, unglamorous gray drawer.</summary>
        public static MockEngineRoomDrawerVm Collapsed() => new();
    }
}
