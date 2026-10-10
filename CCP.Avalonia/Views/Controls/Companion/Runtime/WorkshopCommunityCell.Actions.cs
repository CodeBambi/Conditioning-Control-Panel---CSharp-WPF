using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// The Community cell's four buttons and its installed list (WPF 7.1.5 MainWindow.Patreon.cs
    /// :2346-2493 and MainWindow.CompanionTab.cs UpdateCommunityPromptsUI / CreatePromptRow), over
    /// Core <see cref="CommunityPromptLibrary"/>. Activation keeps WPF's explicit-content gate: the
    /// acknowledgement dialog first, and the library refuses without it.
    /// </summary>
    public partial class WorkshopCommunityCell
    {
        /// <summary>The app's library (seeded by App). Null = the buttons stay hidden.</summary>
        internal static Func<CommunityPromptLibrary?> Library = () => null;

        /// <summary>Raised after an install, import, activate, deactivate or remove, so a page that
        /// shows the active personality can re-read it (WPF PersonalityVm.Sync).</summary>
        internal static event Action? PromptsChanged;

        // Seams (tests): the dialogs and the pickers.
        internal static Func<Window, string, string, Task> Notice = async (o, t, m) => { await MessageDialog.ShowAsync(o, t, m); };
        internal static Func<Window, string, string, string, string, Task<bool>> Ask = (o, t, m, ok, cancel) => MessageDialog.ConfirmAsync(o, t, m, okText: ok, cancelText: cancel);
        internal static Func<Window, Task<bool>> Acknowledge = o => new ExplicitContentAcknowledgementDialog().ShowDialogSafe<bool>(o);
        internal static Func<Window, Task<string?>> PickImport = DefaultPickImport;
        internal static Func<Window, string, Task<string?>> PickExport = DefaultPickExport;

        private Window? Owner => TopLevel.GetTopLevel(this) as Window;

        private void InitializeCommunityActions()
        {
            var live = false;
            try { live = Library() != null; } catch { }
            foreach (var name in new[] { "BtnBrowsePrompts", "BtnImportPrompt", "BtnExportPrompt", "BtnRefreshPrompts" })
                this.FindControl<Button>(name)!.IsVisible = live;
            if (!live) return;

            BrowsePromptsRequested += async (_, _) => await BrowseAsync();
            ImportPromptRequested += async (_, _) => await ImportAsync();
            ExportPromptRequested += async (_, _) => await ExportAsync();
            RefreshPromptsRequested += async (_, _) => await RefreshAsync();
            AttachedToVisualTree += (_, _) => UpdateInstalledList();
            UpdateInstalledList();
        }

        /// <summary>WPF BtnRefreshPrompts_Click.</summary>
        internal async Task RefreshAsync()
        {
            var button = this.FindControl<Button>("BtnRefreshPrompts")!;
            try
            {
                button.IsEnabled = false;
                if (Library() is { } lib) await lib.GetAvailablePromptsAsync(forceRefresh: true);
                UpdateInstalledList();
            }
            catch (Exception ex) { Log.Warning("Failed to refresh prompts: {Error}", ex.Message); }
            finally { button.IsEnabled = true; }
        }

        /// <summary>WPF BtnBrowsePrompts_Click: the first five not yet installed, and Install takes the first.</summary>
        internal async Task BrowseAsync()
        {
            if (Owner is not { } owner || Library() is not { } lib) return;
            try
            {
                var available = await lib.GetAvailablePromptsAsync();
                if (available == null || available.Count == 0)
                {
                    await Notice(owner, Loc.Get("title_community_prompts"), Loc.Get("msg_no_community_prompts"));
                    return;
                }

                var installed = CoreSettings.Current.InstalledCommunityPromptIds ?? new List<string>();
                var notInstalled = available.Where(p => !installed.Contains(p.Id)).ToList();
                if (notInstalled.Count == 0)
                {
                    await Notice(owner, Loc.Get("title_community_prompts"), Loc.Get("msg_all_prompts_installed"));
                    return;
                }

                var message = Loc.Get("label_available_prompts");
                for (var i = 0; i < Math.Min(5, notInstalled.Count); i++)
                {
                    var p = notInstalled[i];
                    message += $"• {p.Name} by {p.Author}\n  {p.Description}\n\n";
                }
                if (notInstalled.Count > 5) message += Loc.GetF("label_and_more_prompts", notInstalled.Count - 5);
                message += Loc.Get("label_install_first_one");

                if (!await Ask(owner, Loc.Get("title_browse_community_prompts"), message, Loc.Get("btn_install"), Loc.Get("btn_cancel"))) return;
                var prompt = await lib.InstallPromptAsync(notInstalled[0].Id);
                if (prompt == null) return;
                await Notice(owner, Loc.Get("title_installed"), Loc.GetF("msg_prompt_installed", prompt.Name));
                Changed();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error browsing prompts");
                await Notice(owner, Loc.Get("title_error"), Loc.GetF("msg_failed_to_browse_prompts", ex.Message));
            }
        }

        /// <summary>WPF BtnImportPrompt_Click.</summary>
        internal async Task ImportAsync()
        {
            if (Owner is not { } owner || Library() is not { } lib) return;
            try
            {
                if (await PickImport(owner) is not { } path) return;
                var prompt = lib.ImportFromFile(path);
                if (prompt != null)
                {
                    await Notice(owner, Loc.Get("title_imported"), Loc.GetF("msg_prompt_imported", prompt.Name, prompt.Author));
                    Changed();
                }
                else
                {
                    await Notice(owner, Loc.Get("title_error"), Loc.Get("msg_failed_to_import_prompt_invalid"));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error importing prompt");
                await Notice(owner, Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt_error", ex.Message));
            }
        }

        /// <summary>WPF BtnExportPrompt_Click.</summary>
        internal async Task ExportAsync()
        {
            if (Owner is not { } owner || Library() is not { } lib) return;
            try
            {
                const string name = "My Custom Personality";
                var author = CoreAccount.DisplayName is { Length: > 0 } n ? n : "Anonymous";
                var prompt = lib.ExportCurrentSettings(name, author, "A custom AI personality.");
                if (await PickExport(owner, name.Replace(" ", "_") + ".json") is not { } path) return;
                await lib.SavePromptToFileAsync(prompt, path);
                await Notice(owner, Loc.Get("title_exported"), Loc.GetF("msg_prompt_exported", path));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error exporting prompt");
                await Notice(owner, Loc.Get("title_error"), Loc.GetF("msg_failed_to_export_prompt", ex.Message));
            }
        }

        /// <summary>WPF CreatePromptRow's Activate click: the explicit-content gate, then the library.</summary>
        internal async Task ActivateAsync(string promptId)
        {
            if (Library() is not { } lib) return;
            try
            {
                var s = CoreSettings.Current;
                var probe = new PersonalityPreset { PromptSettings = lib.GetInstalledPrompt(promptId)?.PromptSettings };
                if (ExplicitContentGate.RequiresAcknowledgement(probe, s.SlutModeEnabled))
                {
                    var prev = s.CompanionPrompt;
                    if (!ExplicitContentGate.IsAlreadyAcknowledged(prev))
                    {
                        if (Owner is not { } owner || !await Acknowledge(owner)) return;
                        if (prev != null)
                        {
                            ExplicitContentGate.MarkAcknowledged(prev);
                            CoreSettings.Save();
                        }
                    }
                }
                lib.ActivatePrompt(promptId);
                Changed();
            }
            catch (Exception ex) { Log.Warning("Activate prompt failed: {Error}", ex.Message); }
        }

        internal void Remove(string promptId)
        {
            try { Library()?.RemovePrompt(promptId); } catch (Exception ex) { Log.Warning("Remove prompt failed: {Error}", ex.Message); }
            Changed();
        }

        private void Changed()
        {
            UpdateInstalledList();
            try { PromptsChanged?.Invoke(); } catch (Exception ex) { Log.Debug("PromptsChanged: {E}", ex.Message); }
        }

        /// <summary>WPF UpdateCommunityPromptsUI: the rows are rebuilt, the localized empty line is
        /// shown and hidden, never destroyed.</summary>
        internal void UpdateInstalledList()
        {
            var panel = this.FindControl<StackPanel>("InstalledPromptsPanel");
            var placeholder = this.FindControl<TextBlock>("TxtNoInstalledPrompts");
            if (panel == null) return;
            for (var i = panel.Children.Count - 1; i >= 0; i--)
                if (!ReferenceEquals(panel.Children[i], placeholder)) panel.Children.RemoveAt(i);

            var shown = 0;
            var lib = Library();
            var s = CoreSettings.Current;
            foreach (var id in (s.InstalledCommunityPromptIds ?? new List<string>()).ToArray())
            {
                var prompt = lib?.GetInstalledPrompt(id);
                if (prompt == null) continue;
                panel.Children.Add(CreatePromptRow(prompt, id == s.ActiveCommunityPromptId));
                shown++;
            }
            if (placeholder != null) placeholder.IsVisible = shown == 0;
        }

        private Control CreatePromptRow(CommunityPrompt prompt, bool isActive)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (isActive)
                namePanel.Children.Add(new TextBlock { Text = "● ", Foreground = new SolidColorBrush(Color.FromRgb(147, 112, 219)), FontSize = 10 });
            namePanel.Children.Add(new TextBlock
            {
                Text = prompt.Name, Foreground = Brushes.White, FontSize = 10,
                FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Normal,
            });
            namePanel.Children.Add(new TextBlock
            {
                Text = " " + Loc.GetF("label_by_author", prompt.Author),
                Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 96)), FontSize = 9,
            });
            grid.Children.Add(namePanel);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var id = prompt.Id;
            if (!isActive)
            {
                var activate = new Button
                {
                    Name = "BtnActivatePrompt",
                    Content = new TextBlock { Text = Loc.Get("btn_activate") },
                    Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(147, 112, 219)),
                    BorderThickness = new Thickness(0), FontSize = 9, Padding = new Thickness(6, 2, 6, 2),
                    Cursor = new Cursor(StandardCursorType.Hand), Tag = id,
                };
                activate.Click += async (_, _) => await ActivateAsync(id);
                buttons.Children.Add(activate);
            }
            var remove = new Button
            {
                Name = "BtnRemovePrompt",
                Content = new TextBlock { Text = "×" },
                Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                BorderThickness = new Thickness(0), FontSize = 12, Padding = new Thickness(4, 0, 4, 0),
                Cursor = new Cursor(StandardCursorType.Hand), Tag = id,
            };
            ToolTip.SetTip(remove, Loc.Get("btn_uninstall"));
            remove.Click += (_, _) => Remove(id);
            buttons.Children.Add(remove);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
            return grid;
        }

        private static async Task<string?> DefaultPickImport(Window owner)
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Get("title_import_community_prompt"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType(Loc.Get("filetype_all_files")) { Patterns = new[] { "*" } },
                },
            });
            return files.Count == 1 ? files[0].TryGetLocalPath() : null;
        }

        private static async Task<string?> DefaultPickExport(Window owner, string suggestedName)
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get("title_export_community_prompt"),
                SuggestedFileName = suggestedName,
                DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
            });
            return file?.TryGetLocalPath();
        }
    }
}
