using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// The one place that turns a dial stop into the sentence describing it, shared by the runtime VM
    /// and the mock so the state gallery cannot drift from the live card.
    ///
    /// <para><b>What the dial actually moves.</b> Exactly one thing: whether any app's page or tab
    /// title is allowed to leave this PC. Off closes her eyes entirely; the middle stop sends the
    /// category, the app name and a rounded duration; the third adds titles, and only for apps named
    /// by hand. It never widened "what she can see" in the sense the old "Everything" label
    /// suggested - the deny list and the consent flag govern that, and neither is on this strip.</para>
    /// </summary>
    internal static class AwarenessDialCopy
    {
        /// <summary>
        /// The hint shown under the strip for the selected stop.
        ///
        /// <para><b>Mind the name collision.</b> There are two enums called
        /// <c>AwarenessIntensity</c>: this one (Off / BroadStrokes / Everything, in
        /// <c>CompanionVmPrimitives</c>, the privacy dial) and
        /// <c>Services.Awareness.AwarenessIntensity</c> (Off / Subtle / Chatty / Unhinged, how
        /// talkative she is). They share a member name - <c>Off</c> - so a file that imports the
        /// wrong one still compiles and every other stop silently falls into the default arm. This
        /// file deliberately does NOT import the Services namespace; the parameter is fully
        /// qualified by its own namespace instead.</para>
        /// </summary>
        public static string HintFor(AwarenessIntensity intensity) => intensity switch
        {
            AwarenessIntensity.Off => Loc.Get("companion_awareness_dial_hint_off"),
            AwarenessIntensity.BroadStrokes => Loc.Get("companion_awareness_dial_hint_broad"),
            _ => Loc.Get("companion_awareness_dial_hint_everything")
        };
    }
}
