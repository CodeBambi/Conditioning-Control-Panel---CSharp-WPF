using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ProgramsBrowseTests
{
    [Fact]
    public async Task BrowseUsesCoreCatalogueSelectionAndReadOnlyDetails()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureAvalonia();
            Window? host = null;
            try
            {
                var definitions = BuiltInPrograms.All();
                var view = new ProgramsTabView { Width = 1200, Height = 900 };
                host = new Window { Width = 1200, Height = 900, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var list = view.FindControl<ListBox>("ProgramLibraryList")!;
                var rows = list.Items.Cast<ProgramBrowseItem>().ToArray();
                Assert.Equal(definitions.Count, list.ItemCount);
                Assert.Equal(definitions.Select(program => program.Id), rows.Select(row => row.ProgramId));
                Assert.Equal(definitions.Select(program => program.Title), rows.Select(row => row.Title));
                Assert.All(rows, row => Assert.False(row.IsActionEnabled));
                Assert.True(view.FindControl<Border>("ProgramDetailsPanel")!.IsVisible);
                Assert.Equal(definitions[0].Title,
                    view.FindControl<TextBlock>("TxtProgramDetailsTitle")!.Text);
                Assert.False(view.FindControl<StackPanel>("ProgramsRunPanel")!.IsVisible);
                Assert.False(view.FindControl<StackPanel>("ProgramsLapsedPanel")!.IsVisible);
                Assert.False(view.FindControl<StackPanel>("ProgramsGraduatedPanel")!.IsVisible);

                list.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                var selected = definitions[1];
                Assert.Equal(selected.Id, ((ProgramBrowseItem)list.SelectedItem!).ProgramId);
                Assert.Equal(selected.Title,
                    view.FindControl<TextBlock>("TxtProgramDetailsTitle")!.Text);
                Assert.Equal(selected.Chapters.Count,
                    view.FindControl<ItemsControl>("ProgramDetailsChapterList")!.ItemCount);
            }
            finally
            {
                host?.Close();
                Dispatcher.UIThread.RunJobs();
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EmptyCatalogueShowsLocalizedEmptyStateAndNoSelectionDetails()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureAvalonia();
            Window? host = null;
            try
            {
                var view = new ProgramsTabView { Width = 1000, Height = 700 };
                view.UseProgramLibrary(Array.Empty<ProgramDefinition>());
                host = new Window { Width = 1000, Height = 700, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(0, view.FindControl<ListBox>("ProgramLibraryList")!.ItemCount);
                Assert.True(view.FindControl<TextBlock>("TxtProgramsBrowseEmpty")!.IsVisible);
                Assert.False(view.FindControl<Border>("ProgramDetailsPanel")!.IsVisible);
                Assert.Null(view.FindControl<ListBox>("ProgramLibraryList")!.SelectedItem);
                Assert.False(view.FindControl<StackPanel>("ProgramsRunPanel")!.IsVisible);
            }
            finally
            {
                host?.Close();
                Dispatcher.UIThread.RunJobs();
            }

            return Task.CompletedTask;
        });
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
