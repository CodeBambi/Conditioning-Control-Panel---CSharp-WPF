using System.Windows.Input;
using Xunit;
using static ConditioningControlPanel.LockCardWindow;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #734 — lock card copy/paste + undo cheats. The window itself needs WPF and can't be realized
/// headlessly, but the two decisions that make the hardening work are pure and covered here:
/// which keyboard gestures are refused, and whether a full-phrase match was actually typed.
///
/// The original block used <c>Keyboard.Modifiers == ModifierKeys.Control</c> in the window's
/// BUBBLING KeyDown, which (a) never ran at all — the TextBox class handler executed Paste and
/// marked the event handled first — and (b) would have missed Ctrl+Shift+V even if it had.
/// </summary>
public class LockCardAntiCheatTests
{
    // ── blocked gestures ───────────────────────────────────────────────────

    [Theory]
    [InlineData(Key.C)]   // copy
    [InlineData(Key.V)]   // paste
    [InlineData(Key.X)]   // cut
    [InlineData(Key.A)]   // select all
    [InlineData(Key.Z)]   // undo — restored the whole phrase after RegisterSuccessfulRepeat cleared it
    [InlineData(Key.Y)]   // redo — the other half of the ~1s Ctrl+Z/Ctrl+Y completion loop
    public void CtrlCombosAreBlocked(Key key)
        => Assert.True(IsBlockedInputGesture(key, ModifierKeys.Control));

    [Fact]
    public void CtrlShiftVIsBlocked()
        // Paste-as-plain-text. An exact `== ModifierKeys.Control` check waves this straight through.
        => Assert.True(IsBlockedInputGesture(Key.V, ModifierKeys.Control | ModifierKeys.Shift));

    [Fact]
    public void CtrlAltVIsBlocked()
        // Any extra modifier, same reasoning as Ctrl+Shift+V.
        => Assert.True(IsBlockedInputGesture(Key.V, ModifierKeys.Control | ModifierKeys.Alt));

    [Theory]
    [InlineData(Key.Insert, ModifierKeys.Shift)]     // legacy paste
    [InlineData(Key.Delete, ModifierKeys.Shift)]     // legacy cut
    [InlineData(Key.Insert, ModifierKeys.Control)]   // legacy copy
    public void LegacyClipboardGesturesAreBlocked(Key key, ModifierKeys mods)
        => Assert.True(IsBlockedInputGesture(key, mods));

    // ── gestures that must keep working ────────────────────────────────────

    [Theory]
    [InlineData(Key.V)]
    [InlineData(Key.Z)]
    [InlineData(Key.A)]
    public void UnmodifiedLettersAreNotBlocked(Key key)
        // The phrase has to be typeable — including the letters that happen to be shortcut keys.
        => Assert.False(IsBlockedInputGesture(key, ModifierKeys.None));

    [Fact]
    public void EscapeIsNeverBlocked()
        // Esc is the deliberate always-available exit the dead-man's switch depends on.
        => Assert.False(IsBlockedInputGesture(Key.Escape, ModifierKeys.None));

    [Theory]
    [InlineData(Key.Back, ModifierKeys.None)]
    [InlineData(Key.Back, ModifierKeys.Control)]     // delete previous word — corrects a typo, cheats nothing
    [InlineData(Key.Delete, ModifierKeys.None)]
    [InlineData(Key.Insert, ModifierKeys.None)]      // plain overtype toggle
    [InlineData(Key.Left, ModifierKeys.Shift)]       // keyboard selection
    public void EditingAndNavigationKeysAreNotBlocked(Key key, ModifierKeys mods)
        => Assert.False(IsBlockedInputGesture(key, mods));

    // ── the keystroke gate ─────────────────────────────────────────────────

    [Fact]
    public void FullyTypedPhraseIsAccepted()
        => Assert.True(HasTypedEnough(keystrokes: 15, phraseLength: 15));

    [Fact]
    public void OverTypingIsAccepted()
        // Backspacing and retyping only ever inflates the credit, so corrections never lock anyone out.
        => Assert.True(HasTypedEnough(keystrokes: 40, phraseLength: 15));

    [Fact]
    public void BulkInsertedPhraseIsRefused()
        // A paste/undo that fills the box in one shot leaves the credit at (near) zero.
        => Assert.False(HasTypedEnough(keystrokes: 0, phraseLength: 15));

    [Fact]
    public void PartiallyTypedThenPastedIsRefused()
        // Type a few characters, then paste the rest: still short of the phrase length.
        => Assert.False(HasTypedEnough(keystrokes: 3, phraseLength: 15));

    [Fact]
    public void EmptyPhraseCannotDeadlock()
        // Degenerate config: a zero-length phrase must not gate itself into an unsolvable card.
        => Assert.True(HasTypedEnough(keystrokes: 0, phraseLength: 0));
}
