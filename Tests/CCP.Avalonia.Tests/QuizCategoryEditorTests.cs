using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>QuizCategoryEditorWindow (reachable only from the hidden quiz, as in WPF): WPF's
/// validation messages, the built-in template copy, and create/edit/delete through QuizStore into
/// custom_quiz_categories.json in the Core golden shape.</summary>
public sealed class QuizCategoryEditorTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static async Task<MessageDialog> Dialog(Window owner)
    {
        for (var i = 0; i < 40 && !owner.OwnedWindows.OfType<MessageDialog>().Any(); i++)
        {
            await Task.Delay(25);
            Dispatcher.UIThread.RunJobs();
        }
        return Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
    }

    private static async Task ExpectWarning(QuizCategoryEditorWindow e, string key)
    {
        e.BtnSave_Click(null!);
        var d = await Dialog(e);
        Assert.Contains(d.GetVisualDescendantTexts(), t => t == Loc.Get(key));
        d.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(e.IsVisible);
    }

    private static async Task<QuizCategoryDefinition> Save(QuizCategoryEditorWindow e)
    {
        e.BtnSave_Click(null!);
        for (var i = 0; i < 40 && e.IsVisible; i++) await Task.Delay(25);
        Assert.False(e.IsVisible);
        QuizStore.SaveCustomCategory(e.Result!);   // what QuizWindow does on a true result
        return e.Result!;
    }

    [Fact]
    public Task CreateEditDeleteRoundTrip() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var file = Path.Combine(CorePaths.UserData, "custom_quiz_categories.json");
        File.Delete(file);

        var e = new QuizCategoryEditorWindow(null);
        e.Show();
        await ExpectWarning(e, "msg_please_enter_a_category_name");
        e.FindControl<TextBox>("TxtName")!.Text = QuizStore.FindCategory("bambi")!.Name.ToUpperInvariant();
        await ExpectWarning(e, "msg_please_enter_a_system_prompt_for_the_ai");

        // Template copy fills the prompt and the built-in archetypes (WPF xaml.cs:220).
        e.FindControl<ComboBox>("CboTemplate")!.SelectedIndex = 2;   // bambi
        Assert.Contains("RESULT ARCHETYPES", e.FindControl<TextBox>("TxtPrompt")!.Text);
        await ExpectWarning(e, "msg_this_name_conflicts_with_a_built_in_category");

        e.FindControl<TextBox>("TxtName")!.Text = "Velvet";
        var created = await Save(e);
        Assert.Equal(QuizStore.FindCategory("bambi")!.Archetypes.Select(a => a.Name), created.Archetypes.Select(a => a.Name));

        var written = (JObject)JArray.Parse(File.ReadAllText(file)).Single();
        var golden = (JObject)JArray.Parse(File.ReadAllText(Golden()))[0];
        Assert.Equal(Keys(golden), Keys(written));
        Assert.Equal("Velvet", (string?)written["Name"]);

        var edit = new QuizCategoryEditorWindow(created);
        edit.Show();
        edit.FindControl<TextBox>("TxtName")!.Text = "Silk";
        var edited = await Save(edit);
        Assert.Equal(created.Id, edited.Id);
        Assert.Equal("Silk", QuizStore.LoadCustomCategories().Single().Name);

        var del = new QuizCategoryEditorWindow(edited);
        del.Show();
        del.BtnDelete_Click(null!);
        (await Dialog(del)).Close(true);
        for (var i = 0; i < 40 && del.IsVisible; i++) await Task.Delay(25);
        Assert.Null(del.Result);
        Assert.Empty(QuizStore.LoadCustomCategories());
    });

    private static string[] Keys(JObject o) =>
        o.Properties().Select(p => p.Name)
            .Concat(((JArray)o["Archetypes"]!).OfType<JObject>().Take(1).SelectMany(a => a.Properties().Select(p => "Archetypes." + p.Name)))
            .ToArray();

    private static string Golden([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "CCP.Core.Tests", "Fixtures", "Quiz", "custom_quiz_categories_golden.json");
}
