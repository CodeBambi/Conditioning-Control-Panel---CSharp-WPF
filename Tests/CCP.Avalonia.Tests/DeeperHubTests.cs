using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Rows shell-deeper-hub / views-tab-deeper / shell-deeper-submissions / shell-deeper-fx:
/// the library list driven from the shell's Deeper tab - keyboard selection, open in the editor
/// (one per file), play (one player), two-step delete with Undo and the trash, the submission
/// badge, the welcome card and the row hover lift. No server is touched.</summary>
public sealed class DeeperHubTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Step(TimeSpan by) => Now += by.Ticks;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static string WriteEnhancement(string name)
    {
        Directory.CreateDirectory(DeeperLocalLibrary.DefaultFolder);
        var path = Path.Combine(DeeperLocalLibrary.DefaultFolder, $"hubtest-{name}-{Guid.NewGuid():N}.ccpenh.json");
        var e = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "/tmp/none.mp3" };
        e.Metadata.Name = "HubTest " + name;
        File.WriteAllText(path, EnhancementSerializer.Save(e));
        return path;
    }

    private static void Key(Control target, Key key, KeyModifiers mods = KeyModifiers.None)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target });
        Dispatcher.UIThread.RunJobs();
    }

    private static Border RowBorder(DeeperTabView view, string path) =>
        view.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("deeper-row") && b.DataContext is DeeperLibraryRowVm r
                        && DeeperTabViewModel.PathsEqual(r.Entry.FilePath, path));

    private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public Task LibraryRowsSelectOpenPlayAndDeleteWithUndo() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var a = WriteEnhancement("a");
        var b = WriteEnhancement("b");
        var trash = Directory.CreateTempSubdirectory("ccp-trash-").FullName;
        var clock = new SteppedClock();
        var oldConfirm = MainShellWindow.DeeperConfirm;
        MainShellWindow? w = null;
        try
        {
            ConditioningControlPanel.Avalonia.Platform.TrashBin.RootOverride = trash;
            MainShellWindow.DeeperDeleteTime = clock;
            MainShellWindow.DeeperConfirm = (_, _, _) => Task.FromResult(true);
            var toasts = new StackPanel();
            global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(toasts);

            w = new MainShellWindow();
            w.Show();
            w.ShowTab("deeper");                     // every door rescans (WPF TabNavigation.cs:358)
            Dispatcher.UIThread.RunJobs();
            var view = w.Named<DeeperTabView>("DeeperTab")!;
            var vm = (DeeperTabViewModel)view.DataContext!;
            view.FindControl<TextBox>("TxtDeeperSearch")!.Text = "HubTest";   // only this test's rows
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, vm.FilteredEntries.Count);

            // Ctrl+F focuses the search box; Down selects the first row and paints it.
            var list = view.FindControl<ItemsControl>("DeeperLibraryList")!;
            Key(view, global::Avalonia.Input.Key.F, KeyModifiers.Control);
            Assert.True(view.FindControl<TextBox>("TxtDeeperSearch")!.IsFocused, "Ctrl+F did not focus search");
            Key(list, global::Avalonia.Input.Key.Down);
            var first = vm.FilteredEntries[0].Entry.FilePath;
            Assert.True(vm.FilteredEntries[0].IsSelected);
            Assert.Contains("selected", RowBorder(view, first).Classes);

            // Enter opens the editor; a second Enter re-activates the same one.
            Key(list, global::Avalonia.Input.Key.Enter);
            Key(list, global::Avalonia.Input.Key.Enter);
            var editors = w.OwnedWindows.OfType<DeeperEditorWindow>()
                .Where(e => DeeperTabViewModel.PathsEqual(e.LoadedFilePath, first)).ToList();
            Assert.Single(editors);
            editors[0].Close();
            Dispatcher.UIThread.RunJobs();

            // The row's play button opens one player on that file; again only brings it forward.
            var row = RowBorder(view, first);
            var play = row.GetVisualDescendants().OfType<Button>().First(x => Equals(x.Content, "▶"));
            Click(play);
            Click(play);
            var players = w.OwnedWindows.OfType<EnhancementPlayerWindow>().ToList();
            Assert.Single(players);
            Assert.True(DeeperTabViewModel.PathsEqual(players[0].LoadedFilePath, first));
            players[0].Close();

            // Delete: the row goes at once, the file waits out the grace; Undo brings it back.
            var delete = row.GetVisualDescendants().OfType<Button>().First(x => Equals(x.Content, "🗑"));
            Click(delete);
            Assert.DoesNotContain(vm.FilteredEntries, r => DeeperTabViewModel.PathsEqual(r.Entry.FilePath, first));
            Assert.True(File.Exists(first));
            var undo = toasts.GetLogicalDescendants().OfType<Button>().Last(x => x.Name == "ToastAction");
            Click(undo);
            Assert.Contains(vm.FilteredEntries, r => DeeperTabViewModel.PathsEqual(r.Entry.FilePath, first));

            // Delete again and let the grace run out on the stepped clock: the file is trashed.
            Click(RowBorder(view, first).GetVisualDescendants().OfType<Button>().First(x => Equals(x.Content, "🗑")));
            clock.Step(TimeSpan.FromSeconds(5));
            w.CommitDueDeeperDeletes();
            Assert.True(File.Exists(first), "committed before the 6 s grace");
            clock.Step(TimeSpan.FromSeconds(2));
            w.CommitDueDeeperDeletes();
            Assert.False(File.Exists(first));
            Assert.True(File.Exists(Path.Combine(trash, "files", Path.GetFileName(first))));
            Assert.True(File.Exists(Path.Combine(trash, "info", Path.GetFileName(first) + ".trashinfo")));
            Assert.Single(vm.FilteredEntries);
        }
        finally
        {
            w?.Close();
            MainShellWindow.DeeperConfirm = oldConfirm;
            MainShellWindow.DeeperDeleteTime = TimeProvider.System;
            ConditioningControlPanel.Avalonia.Platform.TrashBin.RootOverride = null;
            File.Delete(a);
            File.Delete(b);
            Directory.Delete(trash, true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SubmissionBadgeWelcomeCardAndRowLift() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var path = WriteEnhancement("badge");
        var s = CoreSettings.Current;
        var seenWelcome = s.HasSeenDeeperWelcome;
        var motion = s.MotionLevel;
        var key = MainShellWindow.CanonicalCataloguePathKey(path);
        MainShellWindow? w = null;
        try
        {
            s.HasSeenDeeperWelcome = false;
            s.MotionLevel = MotionLevel.Off;   // the lift snaps, so no transition clock is needed
            s.DeeperSubmissions[key] = new DeeperSubmissionRecord { CatalogueId = "x1", Status = "published" };

            w = new MainShellWindow();
            w.Show();
            w.ShowTab("deeper");
            Dispatcher.UIThread.RunJobs();
            var view = w.Named<DeeperTabView>("DeeperTab")!;
            var vm = (DeeperTabViewModel)view.DataContext!;
            var row = vm.FilteredEntries.Single(r => DeeperTabViewModel.PathsEqual(r.Entry.FilePath, path));
            Assert.True(row.ShowSubmissionBadge);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("deeper_submission_badge_published"), row.SubmissionBadgeText);

            // Hover lift: the row rises 2px on enter and settles on leave.
            var border = RowBorder(view, path);
            border.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, border,
                new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), w, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.Equal(-2, ((TranslateTransform)border.RenderTransform!).Y);
            border.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, border,
                new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), w, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.Equal(0, ((TranslateTransform)border.RenderTransform!).Y);

            // The welcome card's dismiss folds it and remembers it.
            var card = view.FindControl<Border>("DeeperWelcomeCard")!;
            Assert.True(card.IsVisible);
            Click(view.FindControl<Button>("BtnDeeperWelcomeDismiss")!);
            Assert.False(card.IsVisible);
            Assert.True(s.HasSeenDeeperWelcome);
        }
        finally
        {
            w?.Close();
            s.HasSeenDeeperWelcome = seenWelcome;
            s.MotionLevel = motion;
            s.DeeperSubmissions.Remove(key);
            File.Delete(path);
        }
        return Task.CompletedTask;
    });
}
