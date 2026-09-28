using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>
    /// One drawn help animation (a "loop") for a dashboard ? popover. A scene is a pure function of
    /// time: <see cref="Draw"/> paints the frame at <c>t</c> into a <see cref="LoopFrame"/>, in the
    /// 480x270 stage space of the approved mockup. Any per-loop state (counters, phrase index) lives
    /// in fields and is cleared in <see cref="Reset"/>, which the view calls whenever t wraps.
    /// </summary>
    public abstract class HelpLoopScene
    {
        /// <summary>Equals <c>HelpContent.SectionId</c>, e.g. "LockCard".</summary>
        public abstract string Id { get; }

        /// <summary>Loop length in ms.</summary>
        public abstract double DurationMs { get; }

        /// <summary>The frame shown when motion is Off.</summary>
        public abstract double StillMs { get; }

        public abstract IReadOnlyList<HelpLoopStep> Steps { get; }

        /// <summary>Draws the frame at <paramref name="t"/> in [0, DurationMs).</summary>
        public abstract void Draw(LoopFrame f, double t);

        /// <summary>The loop wrapped (t went backwards): clear per-loop state.</summary>
        public virtual void Reset() { }
    }

    /// <summary>A step label under the loop, lit while t is in [StartMs, EndMs).</summary>
    public sealed record HelpLoopStep(string LocKey, double StartMs, double EndMs);

    /// <summary>
    /// Finds the loop for a help topic. Scenes register themselves just by existing: every
    /// non-abstract <see cref="HelpLoopScene"/> in this assembly is found once by reflection and
    /// keyed by its <see cref="HelpLoopScene.Id"/>. No list to edit when a scene is added.
    /// </summary>
    public static class HelpLoopRegistry
    {
        private static readonly Lazy<IReadOnlyDictionary<string, Type>> Types = new(Discover);

        /// <summary>Every registered section id.</summary>
        public static IReadOnlyCollection<string> Ids => Types.Value.Keys.ToList();

        /// <summary>A NEW scene instance for <paramref name="sectionId"/>, or false.</summary>
        public static bool TryGet(string? sectionId, out HelpLoopScene scene)
        {
            scene = null!;
            if (string.IsNullOrEmpty(sectionId)) return false;
            if (!Types.Value.TryGetValue(sectionId, out var type)) return false;
            scene = Create(type)!;
            return scene != null;
        }

        /// <summary>True when a loop exists for the id (no instance built).</summary>
        public static bool Has(string? sectionId) =>
            !string.IsNullOrEmpty(sectionId) && Types.Value.ContainsKey(sectionId);

        private static IReadOnlyDictionary<string, Type> Discover()
        {
            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            Type[] all;
            try { all = typeof(HelpLoopScene).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(t => t != null).ToArray()!; }

            foreach (var type in all)
            {
                if (type.IsAbstract || !typeof(HelpLoopScene).IsAssignableFrom(type)) continue;
                var probe = Create(type);
                if (probe == null || string.IsNullOrEmpty(probe.Id)) continue;
                map[probe.Id] = type;
            }
            return map;
        }

        private static HelpLoopScene? Create(Type type)
        {
            try { return (HelpLoopScene?)Activator.CreateInstance(type, nonPublic: true); }
            catch { return null; }
        }
    }
}
