using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;

namespace CcpClient.Desktop.Views;

/// <summary>
/// <b>Session Editor</b> — upstream's <c>Windows/SessionEditorWindow.xaml.cs</c>, opened from the
/// rack's edit action (<c>MainWindow/MainWindow.SessionIO.cs:538</c>, handled at
/// <c>:1819-1868</c>).
///
/// <para><b>It edits a COPY and never the rack's own instance.</b>
/// <see cref="SessionEditorRules.Apply"/> builds a detached session out of the file format, so a
/// cancelled edit — or a save the store refuses — leaves the rack exactly as it found it. Upstream
/// gets the same property from a different mechanism (it converts into a <c>TimelineSession</c> and
/// back, <c>:63</c> and <c>:1150</c>); the outcome is what matters and it is the same one.</para>
///
/// <para><b>The window never touches the disk.</b> It hands the edited session to the
/// <c>commit</c> delegate it was constructed with and reads a yes or no back: on yes it closes, on
/// no it stays open with the refusal on screen and the user's typing still in the boxes. That
/// split is deliberate — upstream's editor holds its own <c>SessionFileService</c> (<c>:55</c>) and
/// writes from inside the window, which is how its Import button ends up owning a second copy of
/// the load path. Here the ONE writer is <see cref="CustomSessionStore"/>, reached through the page
/// that owns it, and this window is only a set of fields.</para>
/// </summary>
public partial class SessionEditorWindow : Window
{
    private readonly Func<ScriptedSession, bool> _commit;
    private readonly Func<ScriptedSession, bool> _delete;
    private bool _deleteArmed;

    /// <param name="session">The session the user picked in the rack. Never mutated.</param>
    /// <param name="commit">Persist the edited session; true when it really landed. Injected so the
    /// window has no store, no path and no file system — see the class remark.</param>
    /// <param name="delete">Remove the session the user opened; true when it really went. Only ever
    /// called for a session whose <see cref="ScriptedSession.Origin"/> is
    /// <see cref="ScriptedSessionOrigin.Custom"/>, because the button is not on screen otherwise
    /// (upstream's own rule, <c>MainWindow/MainWindow.SessionIO.cs:540-544</c>).</param>
    public SessionEditorWindow(
        ScriptedSession session,
        Func<ScriptedSession, bool> commit,
        Func<ScriptedSession, bool> delete)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(delete);
        InitializeComponent();
        Original = session;
        _commit = commit;
        _delete = delete;

        Title = SessionRackNotices.EditorTitle;
        Header.Text = SessionRackNotices.EditorTitle;
        ProvenanceLine.Text = SessionRackNotices.EditorProvenance(session.Origin);
        AbsenceLine.Text = SessionRackNotices.EditorAbsences;

        NameBox.Text = session.Name;
        DescriptionBox.Text = session.Description;

        // The bounds come off the rules rather than the markup, so the control and the clamp that
        // guards it cannot drift apart. Order matters: the range has to exist before a value can be
        // placed inside it, or the slider clamps the value against its default 0..100.
        DurationSlider.Minimum = SessionEditorRules.MinDurationMinutes;
        DurationSlider.Maximum = SessionEditorRules.MaxDurationMinutes;
        DurationSlider.TickFrequency = SessionEditorRules.DurationStepMinutes;
        DurationSlider.SmallChange = SessionEditorRules.DurationStepMinutes;
        DurationSlider.LargeChange = SessionEditorRules.DurationStepMinutes * 2;
        DurationSlider.Value = SessionEditorRules.ClampDuration(session.DurationMinutes);
        RenderDuration();
        DurationSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                RenderDuration();
            }
        };

        // Upstream offers delete only where it could ever succeed (:540-544), and its store refuses
        // a built-in outright anyway (Services/Session/SessionManager.cs:203-205). Both guards are
        // kept: the button is absent for a built-in AND CustomSessionStore.Delete would refuse one.
        DeleteButton.IsVisible = session.Origin != ScriptedSessionOrigin.BuiltIn;
        DeleteButton.Content = SessionRackNotices.EditorDelete;

        SaveButton.Click += (_, _) => OnSave();
        CancelButton.Click += (_, _) => Close();
        DeleteButton.Click += (_, _) => OnDelete();
    }

    /// <summary>The session this window was opened on — the rack's own instance, untouched.</summary>
    public ScriptedSession Original { get; }

    /// <summary>The session the last save built, or null when nothing has been saved. Upstream's
    /// <c>ResultSession</c> (<c>Windows/SessionEditorWindow.xaml.cs:47</c>), kept public for the
    /// same reason: it is what a caller reads to find out what the user made.</summary>
    public ScriptedSession? ResultSession { get; private set; }

    /// <summary>Whether the delete button is holding its second-gesture question. Public so a fact
    /// can pin that ONE click deletes nothing.</summary>
    public bool DeleteArmed => _deleteArmed;

    /// <summary>The refusal on screen, or empty. Public so a fact reads the sentence the user
    /// reads rather than a copy of it.</summary>
    public string Refusal => RefusalLine.IsVisible ? RefusalLine.Text ?? string.Empty : string.Empty;

    /// <summary>Escape closes without saving, as it does on the recap and the history window
    /// (<c>SessionCompleteWindow.xaml.cs:44-51</c>). Upstream's editor closes on its own ✕ with
    /// <c>DialogResult = false</c> (<c>:113-117</c>), which is the same outcome: nothing is
    /// written.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// Upstream's <c>BtnSave_Click</c> (<c>Windows/SessionEditorWindow.xaml.cs:1138-1153</c>): read
    /// the three boxes, refuse an empty name and write NOTHING when it is empty, otherwise build
    /// the result and hand it on.
    ///
    /// <para>Upstream closes the moment it has a result, because its caller does the writing after
    /// the dialog returns (<c>MainWindow/MainWindow.SessionIO.cs:1828-1865</c>) and a write that
    /// fails there has nowhere to put the user's typing back. This one asks first and only closes
    /// on a yes, so a save that cannot happen leaves the window, the boxes and the reason on
    /// screen.</para>
    /// </summary>
    private void OnSave()
    {
        var refusal = SessionEditorRules.Validate(NameBox.Text);
        if (refusal is not null)
        {
            ShowRefusal(refusal);
            return;
        }

        var edited = SessionEditorRules.Apply(
            Original, NameBox.Text, DescriptionBox.Text, (int)DurationSlider.Value);

        if (!_commit(edited))
        {
            ShowRefusal(SessionRackNotices.EditorSaveFailed);
            return;
        }

        ResultSession = edited;
        Close();
    }

    /// <summary>
    /// Upstream confirms a delete before it happens (<c>MainWindow/MainWindow.SessionIO.cs:1953-1959</c>,
    /// a styled dialog naming the session). The second gesture here is the button's own caption: the
    /// first click asks, the second acts, and a window closed in between has deleted nothing.
    /// </summary>
    private void OnDelete()
    {
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            DeleteButton.Content = SessionRackNotices.EditorDeleteConfirm;
            ShowRefusal(SessionRackNotices.EditorDeleteQuestion);
            return;
        }

        if (!_delete(Original))
        {
            _deleteArmed = false;
            DeleteButton.Content = SessionRackNotices.EditorDelete;
            ShowRefusal(SessionRackNotices.EditorDeleteFailed);
            return;
        }

        Close();
    }

    private void ShowRefusal(string text)
    {
        RefusalLine.Text = text;
        RefusalLine.IsVisible = true;
    }

    private void RenderDuration() => DurationValue.Text = string.Create(
        CultureInfo.InvariantCulture, $"{(int)DurationSlider.Value} min");
}
