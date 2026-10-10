using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The content-packs strip and the header pack buttons (WPF 7.1.5 MainWindow.Assets.cs:50-460 and
    /// :2043-2210): RefreshPacksAsync, the 1 s preview rotation, install with progress, uninstall,
    /// activate, the external link, Get Packs and Delete downloaded packs. The strip ships HIDDEN on
    /// 7.1.5 (<see cref="PacksSectionEnabled"/> false, PacksSection IsVisible="False"); Get Packs is
    /// the live door and opens the Discord pack catalogue.
    /// </summary>
    public partial class AssetsTabView
    {
        /// <summary>WPF MainWindow.xaml.cs PacksSectionEnabled: most packs live outside the app now.</summary>
        internal const bool PacksSectionEnabled = false;

        /// <summary>Tests hand in a service over a fake handler; null = <see cref="ContentPackService.Current"/>.</summary>
        public ContentPackService? PackServiceOverride { get; set; }

        private ContentPackService? PackService => PackServiceOverride ?? ContentPackService.Current;

        private DispatcherTimer? _packPreviewTimer;
        private ContentPackService? _packEventsOn;

        private void InitializePackCards()
        {
            BtnGetPacks.Click += BtnGetPacks_Click;
            BtnRefreshPacks.Click += (_, _) => _ = RefreshPacksAsync();
            BtnDeleteDownloadedPacks.Click += BtnDeleteDownloadedPacks_Click;
            PacksScrollViewer.AddHandler(PointerWheelChangedEvent, PacksScrollViewer_Wheel, RoutingStrategies.Tunnel);
            PacksScrollViewer.AddHandler(Button.ClickEvent, PackCardButton_Click);
            PacksSection.IsVisible = PacksSectionEnabled;
            DetachedFromVisualTree += (_, _) => StopPackPreviewRotation();
            IsVisibleChanged_Packs();
        }

        private void IsVisibleChanged_Packs()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (IsVisible && PacksSectionEnabled) _ = RefreshPacksAsync();
                else if (!IsVisible) StopPackPreviewRotation();
            };
        }

        private async Task Tell(string title, string message)
        {
            if (OwnerWindow is { } w) await Dialogs.MessageDialog.ShowAsync(w, title, message);
            else Log.Information("{Title}: {Message}", title, message);
        }

        private async Task<bool> Ask(string title, string message) =>
            OwnerWindow is { } w && await Dialogs.MessageDialog.ConfirmAsync(w, title, message);

        /// <summary>WPF RefreshPacksAsync: catalogue, previews (decrypted for installed packs, server
        /// urls otherwise), rotation, events. Never throws.</summary>
        internal async Task RefreshPacksAsync()
        {
            var svc = PackService;
            if (svc == null) return;
            try
            {
                var packs = await svc.GetAvailablePacksAsync();
                Browser.Packs.Clear();
                foreach (var p in packs) Browser.Packs.Add(new PackCardViewModel(p));

                var loads = Browser.Packs.Select(card => Task.Run(async () =>
                {
                    try
                    {
                        var bytes = card.Pack.IsDownloaded
                            ? svc.GetPackPreviewBytes(card.Pack.Id, 10)
                            : card.Pack.PreviewUrls?.Count > 0
                                ? await svc.GetPreviewBytesFromUrlsAsync(card.Pack.Id, card.Pack.PreviewUrls)
                                : new List<byte[]>();
                        var images = PackCardViewModel.Decode(bytes);
                        if (images.Count > 0) await Dispatcher.UIThread.InvokeAsync(() => card.SetPreviews(images));
                    }
                    catch (Exception ex) { Log.Debug("Failed to load preview images for {PackId}: {Error}", card.Pack.Id, ex.Message); }
                })).ToList();
                await Task.WhenAll(loads);

                StartPackPreviewRotation();
                if (!ReferenceEquals(_packEventsOn, svc))
                {
                    if (_packEventsOn != null)
                    {
                        _packEventsOn.AuthenticationRequired -= OnPackAuthenticationRequired;
                        _packEventsOn.RateLimitExceeded -= OnPackRateLimitExceeded;
                    }
                    svc.AuthenticationRequired += OnPackAuthenticationRequired;
                    svc.RateLimitExceeded += OnPackRateLimitExceeded;
                    _packEventsOn = svc;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to refresh packs"); }
        }

        private void StartPackPreviewRotation()
        {
            _packPreviewTimer?.Stop();
            _packPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _packPreviewTimer.Tick += (_, _) =>
            {
                foreach (var card in Browser.Packs.Where(c => c.HasPreviewImages)) card.AdvancePreviewImage();
            };
            _packPreviewTimer.Start();
        }

        private void StopPackPreviewRotation()
        {
            _packPreviewTimer?.Stop();
            _packPreviewTimer = null;
        }

        private void OnPackAuthenticationRequired(object? sender, string message) =>
            Dispatcher.UIThread.Post(() => _ = CoreAccount.IsLoggedIn
                ? Tell(Loc.Get("title_authentication_required"), message)
                : Tell(Loc.Get("title_login_required"), Loc.GetF("msg_0_n_nplease_log_in_from_the_settings_tab", message)));

        private void OnPackRateLimitExceeded(object? sender, (ContentPack Pack, string Message, DateTime ResetTime) e) =>
            Dispatcher.UIThread.Post(() =>
            {
                e.Pack.IsDownloading = false;
                _ = Tell(Loc.Get("title_download_limit_reached"), Loc.GetF("msg_download_limit_reached_0_1", e.Message, ResetText(e.ResetTime, DateTime.UtcNow)));
            });

        /// <summary>WPF: whole hours over one hour, else whole minutes.</summary>
        internal static string ResetText(DateTime resetUtc, DateTime nowUtc)
        {
            var left = resetUtc.ToUniversalTime() - nowUtc;
            return left.TotalHours > 1 ? Loc.GetF("label_0_hours", (int)left.TotalHours) : Loc.GetF("label_0_minutes", (int)left.TotalMinutes);
        }

        /// <summary>WPF BtnGetPacks_Click: straight to the #asset-packs channel, not the plain invite.</summary>
        private async void BtnGetPacks_Click(object? sender, RoutedEventArgs e)
        {
            try { await Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), DiscordLinks.PackCatalogue); }
            catch (Exception ex) { Log.Warning(ex, "Failed to open the pack catalogue link"); }
        }

        private async void BtnDeleteDownloadedPacks_Click(object? sender, RoutedEventArgs e)
        {
            var store = Packs;
            var ids = CoreSettings.Current.InstalledPackIds;
            if (store == null || ids == null || ids.Count == 0)
            {
                await Tell(Loc.Get("title_delete_downloaded_packs"), Loc.Get("msg_no_downloaded_packs_to_delete"));
                return;
            }
            if (!await Ask(Loc.Get("title_delete_downloaded_packs"), Loc.GetF("msg_delete_downloaded_packs_confirm_0", ids.Count))) return;
            foreach (var id in ids.ToList()) store.UninstallPack(id);
            RefreshAssetTree();
            await Tell(Loc.Get("btn_done"), Loc.Get("msg_all_downloaded_packs_have_been_deleted_nyour"));
        }

        private void PackCardButton_Click(object? sender, RoutedEventArgs e)
        {
            if (e.Source is not Control c) return;
            var button = c as Button ?? c.FindAncestorOfType<Button>();
            if (button == null) return;
            if (button.Classes.Contains("creatorDiscord"))
            {
                e.Handled = true;
                _ = Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), DiscordLinks.Invite);
                return;
            }
            if (button.Tag is not PackCardViewModel card) return;
            e.Handled = true;
            if (button.Classes.Contains("packActivate")) TogglePackActive(card);
            else if (button.Classes.Contains("packDownload")) _ = PackDownloadAsync(card);
        }

        /// <summary>WPF BtnPackDownload_Click: uninstall when installed, the external link for an
        /// external pack, else confirm -> install with progress -> activate.</summary>
        internal async Task PackDownloadAsync(PackCardViewModel card)
        {
            var pack = card.Pack;
            var svc = PackService;
            var store = Packs;
            if (svc == null || store == null) return;

            if (pack.IsDownloaded)
            {
                var size = pack.SizeBytes > 0 ? $"{pack.SizeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB" : "";
                if (!await Ask("Uninstall Content Pack",
                        $"Uninstall '{pack.Name}'?\n\nThis will delete {size} of downloaded content from your computer.\n\nYou can reinstall it later if needed.")) return;
                try
                {
                    store.UninstallPack(pack.Id);
                    pack.IsDownloaded = false;
                    pack.IsActive = false;
                    card.SetPreviews(new List<IImage>());
                    RefreshAssetTree();
                    await Tell("Uninstalled", $"'{pack.Name}' has been uninstalled.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to uninstall pack: {Name}", pack.Name);
                    await Tell("Error", $"Failed to uninstall pack: {ex.Message}");
                }
                return;
            }

            if (pack.IsExternal)
            {
                try
                {
                    var url = pack.ExternalUrl ?? await svc.GetExternalPackDownloadUrlAsync(pack.Id);
                    if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                        await Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), u.AbsoluteUri);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to get external pack URL for {PackId}", pack.Id);
                    await Tell("Error", $"Failed to get download link: {ex.Message}");
                }
                return;
            }

            var sizeMb = pack.SizeBytes > 0 ? $" ({pack.SizeBytes / (1024.0 * 1024):F0} MB)" : "";
            if (!await Ask("Install Content Pack",
                    $"Download and install '{pack.Name}'?{sizeMb}\n\nThis will download encrypted content to a secure folder on your computer.")) return;
            await InstallAndActivateAsync(card);
        }

        /// <summary>The install half without the confirm (tests drive it directly).</summary>
        internal async Task InstallAndActivateAsync(PackCardViewModel card)
        {
            var pack = card.Pack;
            var svc = PackService;
            var store = Packs;
            if (svc == null || store == null) return;
            pack.IsDownloading = true;
            try
            {
                var progress = new Progress<int>(p => Dispatcher.UIThread.Post(() => pack.DownloadProgress = p));
                await svc.InstallPackAsync(pack, progress);
                store.ActivatePack(pack.Id);
                pack.IsActive = true;
                RefreshAssetTree();
                var bytes = await Task.Run(() => svc.GetPackPreviewBytes(pack.Id, 10));
                card.SetPreviews(PackCardViewModel.Decode(bytes));
                await Tell(Loc.Get("title_success"), Loc.GetF("msg_0_installed_successfully", pack.Name));
            }
            catch (UnauthorizedAccessException) { Log.Debug("Pack install cancelled - authentication required"); }
            catch (PackRateLimitException) { Log.Debug("Pack install cancelled - rate limit exceeded"); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to install pack: {Name}", pack.Name);
                await Tell("Error", $"Installation failed: {ex.Message}");
            }
            finally
            {
                pack.IsDownloading = false;
                pack.DownloadProgress = 0;
            }
        }

        /// <summary>WPF BtnPackActivate_Click.</summary>
        internal void TogglePackActive(PackCardViewModel card)
        {
            var pack = card.Pack;
            var store = Packs;
            if (store == null || !pack.IsDownloaded) return;
            try
            {
                if (pack.IsActive) { store.DeactivatePack(pack.Id); pack.IsActive = false; }
                else { store.ActivatePack(pack.Id); pack.IsActive = true; }
                RefreshAssetTree();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to toggle pack activation: {Name}", pack.Name);
                _ = Tell("Error", $"Failed to update pack: {ex.Message}");
            }
        }

        /// <summary>WPF PacksScrollViewer_PreviewMouseWheel: the wheel pans the strip sideways.</summary>
        private void PacksScrollViewer_Wheel(object? sender, PointerWheelEventArgs e)
        {
            if (e.Delta.Y == 0) return;
            var sv = PacksScrollViewer;
            sv.Offset = sv.Offset.WithX(Math.Max(0, sv.Offset.X - e.Delta.Y * 120));
            e.Handled = true;
        }
    }

    /// <summary>One card in the content-packs strip (WPF ContentPack's view half): the Core
    /// <see cref="ContentPack"/> state plus the decoded previews that rotate once a second.</summary>
    public sealed class PackCardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public PackCardViewModel(ContentPack pack)
        {
            Pack = pack;
            pack.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != null) Raise(e.PropertyName);
                if (e.PropertyName == nameof(ContentPack.IsDownloaded)) Raise(nameof(ShowExternalButtons));
            };
        }

        public ContentPack Pack { get; }

        public string Name => Pack.Name;
        public string Description => Pack.Description;
        public string SizeDisplay => Pack.SizeDisplay;
        public int ImageCount => Pack.ImageCount;
        public int VideoCount => Pack.VideoCount;
        public bool IsDownloaded => Pack.IsDownloaded;
        public bool IsActive => Pack.IsActive;
        public bool IsExternal => Pack.IsExternal;
        public bool IsDownloading => Pack.IsDownloading;
        public bool IsNotDownloading => Pack.IsNotDownloading;
        public double DownloadProgress => Pack.DownloadProgress;
        public bool ShowExternalButtons => Pack.ShowExternalButtons;
        public string DownloadButtonText => Pack.DownloadButtonText;
        public string ActivateButtonText => Pack.ActivateButtonText;

        /// <summary>WPF: a MultiBinding "{0} images, {1} videos" (English there too).</summary>
        public string CountsDisplay => $"{ImageCount} images, {VideoCount} videos";

        private List<IImage> _previews = new();
        private int _index;

        public IImage? CurrentPreviewImage => _previews.Count > 0 ? _previews[_index % _previews.Count] : null;

        /// <summary>WPF's static PreviewImageUrl was a pack:// resource (pack1.png / pack2.png); this
        /// head ships no such image, so a pack without decoded previews shows "No Preview".</summary>
        public IImage? PreviewImage => null;
        public bool HasPreviewImages => _previews.Count > 0;
        public bool HasAnyPreview => _previews.Count > 0;

        public void SetPreviews(List<IImage> images)
        {
            _previews = images;
            _index = 0;
            Raise(nameof(CurrentPreviewImage));
            Raise(nameof(HasPreviewImages));
            Raise(nameof(HasAnyPreview));
        }

        public void AdvancePreviewImage()
        {
            if (_previews.Count <= 1) return;
            _index = (_index + 1) % _previews.Count;
            Raise(nameof(CurrentPreviewImage));
        }

        /// <summary>Decodes at 240 px wide (WPF 240x100 thumbnails); a picture that will not decode is skipped.</summary>
        internal static List<IImage> Decode(IEnumerable<byte[]> bytes)
        {
            var list = new List<IImage>();
            foreach (var b in bytes)
            {
                try { using var ms = new MemoryStream(b); list.Add(Bitmap.DecodeToWidth(ms, 240)); }
                catch (Exception ex) { Log.Debug("Pack preview did not decode: {Error}", ex.Message); }
            }
            return list;
        }
    }
}
