using System;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The seam for the app's ONE <see cref="ModerationLog"/>. The class itself already lives in
    /// Core; what could not cross is the single instance, which hangs off the head's App and is
    /// built over a per-launch <see cref="ModerationSession"/>. A second instance would hash a
    /// second session id and split the CCBill record file in two, so callers reach the app's
    /// through here rather than constructing their own.
    ///
    /// <para>The provider is re-read on every call, not cached: on Windows <c>App.ModerationLog</c>
    /// is null until well into startup, so a prompt editor opened - or a moderation hit fired -
    /// before that point must find null and shrug, not crash.</para>
    ///
    /// <para>Unseeded is a silent no-op: nothing is written, nothing throws. A head with no
    /// moderation log keeps no record, which is the honest outcome; refusing the user's save
    /// because the record file is absent would be the wrong trade.</para>
    /// </summary>
    public static class CoreModerationLog
    {
        public static volatile Func<ModerationLog?>? InstanceProvider;

        /// <summary>Records a moderation hit. <paramref name="source"/> is input/output/memory.</summary>
        public static void Record(ProhibitedCategory category, string source, string modelHint)
        {
            try { InstanceProvider?.Invoke()?.Record(category, source, modelHint); } catch { }
        }

        /// <summary>Records a PromptValidator flag from a prompt-editor surface. Counts only, never text.</summary>
        public static void RecordEdit(string fieldName, int patternCount, string surface)
        {
            try { InstanceProvider?.Invoke()?.RecordEdit(fieldName, patternCount, surface); } catch { }
        }
    }
}
