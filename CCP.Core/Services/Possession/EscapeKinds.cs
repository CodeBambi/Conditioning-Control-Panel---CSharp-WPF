using System;

namespace ConditioningControlPanel.Services.Possession;

/// <summary>Escape-attempt kinds raised through LockdownService.NotifyEscapeAttempt (tripwires).</summary>
public static class EscapeKinds
{
    public const string Close = "close";              // Alt+F4 / window X / OnClosing
    public const string Minimize = "minimize";        // minimize button (ALLOWED - still a tripwire)
    public const string SystemKey = "syskey";         // a suppressed Win / Alt+Tab / Ctrl+Esc combo
    public const string Stop = "stop";                // Stop button, tube quick-menu Stop, voice stop
    public const string WrongPhrase = "wrong_phrase"; // a wrong secret phrase typed into the timer box
    public const string Settings = "settings";        // trying to flip a greyed safety toggle
    public const string EmergencyExit = "emergency_exit"; // pressed the big Emergency Exit button (minigame launched)
    public const string Starve = "starve";                // switched the LAST running feature off mid-lockdown (the dose went empty)

    /// <summary>Which tripwires are "trying to leave" and cost on the Chaster tab. Close, Stop and a
    /// wrong phrase are. The emergency exit NEVER is. Minimize is allowed, a system key is usually a
    /// reflex, a greyed toggle is curiosity and a starved dose is not an exit at all.</summary>
    public static bool CostsChaster(string? kind) => kind is Close or Stop or WrongPhrase;
}

/// <summary>One escape attempt. Repeat = how many times THIS kind fired during this lockdown (1-based);
/// Total = every kind combined. The director scales the scare by both.</summary>
public readonly record struct EscapeAttempt(string Kind, int Repeat, int Total, DateTime At);
