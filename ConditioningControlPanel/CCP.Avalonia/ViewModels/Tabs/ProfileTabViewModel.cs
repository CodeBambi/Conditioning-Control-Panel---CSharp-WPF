using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Services.Auth;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs
{
    /// <summary>
    /// Minimal profile/account tab. Shows the current unified identity and
    /// linked providers, and exposes logout. A full profile viewer/Discord
    /// integration can be layered on top later without changing the DI contract.
    /// </summary>
    public sealed partial class ProfileTabViewModel : TabItemViewModel
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger<ProfileTabViewModel>? _logger;

        [ObservableProperty]
        private string _displayName = "";

        [ObservableProperty]
        private string _unifiedId = "";

        [ObservableProperty]
        private bool _isLoggedIn;

        [ObservableProperty]
        private bool _hasLinkedDiscord;

        [ObservableProperty]
        private bool _hasLinkedPatreon;

        [ObservableProperty]
        private string _loginStatusText = "";

        [ObservableProperty]
        private ObservableCollection<string> _linkedProviders = new();

        [ObservableProperty]
        private int _playerLevel;

        [ObservableProperty]
        private string _playerXpText = "0";

        [ObservableProperty]
        private string _totalHoursText = "0h";

        [ObservableProperty]
        private string _peakRankText = "—";

        [ObservableProperty]
        private string _avatarInitials = "?";

        [ObservableProperty]
        private ObservableCollection<string> _badges = new();

        public ProfileTabViewModel()
            : base("discord", "Profile", "👤")
        {
            _settingsService = null!;
            DisplayName = "Bambi";
            AvatarInitials = "B";
            PlayerLevel = 5;
            PlayerXpText = "1 240";
            TotalHoursText = "12.5h";
            PeakRankText = "#3 / 1 200";
            Badges = new ObservableCollection<string> { "Member", "Conditioned" };
        }

        public ProfileTabViewModel(ISettingsService settingsService, ILogger<ProfileTabViewModel>? logger = null)
            : base("discord", "Profile", "👤")
        {
            _settingsService = settingsService;
            _logger = logger;
            RefreshState();
        }

        public override void OnSelected()
        {
            base.OnSelected();
            RefreshState();
        }

        [RelayCommand]
        private void RefreshState()
        {
            var settings = _settingsService?.Current;
            if (settings == null)
            {
                IsLoggedIn = false;
                DisplayName = "";
                UnifiedId = "";
                HasLinkedDiscord = false;
                HasLinkedPatreon = false;
                LinkedProviders = new ObservableCollection<string>();
                LoginStatusText = Loc.Get("label_not_connected");
                return;
            }

            DisplayName = settings.UserDisplayName ?? "";
            UnifiedId = settings.UnifiedId ?? "";
            IsLoggedIn = !string.IsNullOrEmpty(settings.UnifiedId);
            HasLinkedDiscord = settings.HasLinkedDiscord;
            HasLinkedPatreon = settings.HasLinkedPatreon;

            var providers = new ObservableCollection<string>();
            if (settings.HasLinkedPatreon) providers.Add("Patreon");
            if (settings.HasLinkedDiscord) providers.Add("Discord");
            LinkedProviders = providers;

            PlayerLevel = settings.PlayerLevel;
            PlayerXpText = $"{settings.PlayerXP:F0}";
            TotalHoursText = $"{settings.TotalConditioningMinutes / 60.0:F1}h";
            PeakRankText = settings.SeasonPeakRank > 0 && settings.SeasonPeakRankTotal > 0
                ? $"#{settings.SeasonPeakRank} / {settings.SeasonPeakRankTotal}"
                : "—";
            AvatarInitials = string.IsNullOrWhiteSpace(DisplayName)
                ? "?"
                : DisplayName.Trim().Substring(0, 1).ToUpperInvariant();

            var badges = new ObservableCollection<string>();
            if (IsLoggedIn) badges.Add(Loc.Get("badge_member"));
            if (HasLinkedPatreon) badges.Add(Loc.Get("badge_patreon_supporter"));
            if (settings.TotalConditioningMinutes >= 60) badges.Add(Loc.Get("badge_conditioned"));
            if (settings.PlayerLevel >= 10) badges.Add(Loc.Get("badge_veteran"));
            if (settings.SeasonPeakRank > 0 && settings.SeasonPeakRank <= 10) badges.Add(Loc.Get("badge_top_ten"));
            Badges = badges;

            LoginStatusText = IsLoggedIn
                ? Loc.Get("label_logged_in_as")
                : Loc.Get("label_not_connected");
        }

        [RelayCommand]
        private async Task LogoutAsync()
        {
            var settings = _settingsService?.Current;
            if (settings == null) return;

            settings.UnifiedId = null;
            settings.UserDisplayName = null;
            settings.HasLinkedDiscord = false;
            settings.HasLinkedPatreon = false;
            settings.PlayerXP = 0;
            settings.PlayerLevel = 1;
            settings.SkillPoints = 0;
            settings.UnlockedSkills = new List<string>();
            settings.TotalConditioningMinutes = 0;

            // AC-1/AC-2: revoke every provider's OAuth tokens and cached premium and clear
            // settings.AuthToken via the shared helper, then persist.
            AuthLogoutHelper.LogoutAll(App.Services, _settingsService);

            _logger?.LogInformation("User logged out from profile tab.");
            RefreshState();
        }

        public string HeaderTitle => Loc.Get("label_profile_viewer");
        public string CommunitySettingsTitle => Loc.Get("label_community_settings");
        public string LinkedAccountsTitle => Loc.Get("label_link_accounts");
        public string LogoutText => Loc.Get("btn_logout");
        public string LoginPrompt => Loc.Get("label_login_to_unlock_exclusive_features");
    }
}
