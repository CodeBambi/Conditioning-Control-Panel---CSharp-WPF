using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops
{
    /// <summary>One drawn help loop, a pure function of time in the mockup's 480x270 stage space
    /// (WPF Controls/HelpLoops/HelpLoopScene.cs). Per-loop state is cleared in <see cref="Reset"/>,
    /// which the view calls whenever t wraps.</summary>
    public abstract class HelpLoopScene
    {
        /// <summary>Equals <c>HelpContent.SectionId</c>, e.g. "PinkFilter".</summary>
        public abstract string Id { get; }
        public abstract double DurationMs { get; }
        /// <summary>The frame shown when motion is Off.</summary>
        public abstract double StillMs { get; }
        public abstract IReadOnlyList<HelpLoopStep> Steps { get; }
        public abstract void Draw(LoopFrame f, double t);
        public virtual void Reset() { }
    }

    /// <summary>A step label under the loop, lit while t is in [StartMs, EndMs).</summary>
    public sealed record HelpLoopStep(string LocKey, double StartMs, double EndMs);

    /// <summary>Every non-abstract scene in this assembly, keyed by Id, found once by reflection
    /// exactly like WPF's registry - adding a scene is adding a file.</summary>
    public static class HelpLoopRegistry
    {
        private static readonly Lazy<IReadOnlyDictionary<string, Type>> Types = new(() =>
            typeof(HelpLoopScene).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(HelpLoopScene).IsAssignableFrom(t))
                .Select(t => (Type: t, Probe: Create(t)))
                .Where(p => !string.IsNullOrEmpty(p.Probe?.Id))
                .ToDictionary(p => p.Probe!.Id, p => p.Type, StringComparer.Ordinal));

        public static IReadOnlyCollection<string> Ids => Types.Value.Keys.ToList();

        /// <summary>True when a loop exists for the id (no instance built), as WPF's Has.</summary>
        public static bool Has(string? sectionId) =>
            !string.IsNullOrEmpty(sectionId) && Types.Value.ContainsKey(sectionId);

        /// <summary>A NEW scene instance for <paramref name="sectionId"/>, or false.</summary>
        public static bool TryGet(string? sectionId, out HelpLoopScene scene)
        {
            scene = null!;
            if (string.IsNullOrEmpty(sectionId) || !Types.Value.TryGetValue(sectionId, out var type)) return false;
            scene = Create(type)!;
            return scene != null;
        }

        private static HelpLoopScene? Create(Type type)
        {
            try { return (HelpLoopScene?)Activator.CreateInstance(type, nonPublic: true); }
            catch { return null; }
        }
    }
}
