using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: CCP.Avalonia.Tests.IsolateProcessState]
[assembly: TestCaseOrderer(typeof(CCP.Avalonia.Tests.SeededOrder))]
[assembly: TestCollectionOrderer(typeof(CCP.Avalonia.Tests.SeededOrder))]

namespace CCP.Avalonia.Tests;

/// <summary>xUnit v3's default order is fixed, so order leaks hide. <c>CCP_TEST_ORDER_SEED=n</c>
/// shuffles classes and their tests reproducibly; unset, the default order is kept.</summary>
internal sealed class SeededOrder : ITestCaseOrderer, ITestCollectionOrderer
{
    private static readonly int? Seed = int.TryParse(Environment.GetEnvironmentVariable("CCP_TEST_ORDER_SEED"), out var s) ? s : null;

    public IReadOnlyCollection<T> OrderTestCases<T>(IReadOnlyCollection<T> testCases) where T : notnull, ITestCase =>
        Seed is { } seed ? Shuffle(testCases, seed, t => t.UniqueID) : DefaultTestCaseOrderer.Instance.OrderTestCases(testCases);

    public IReadOnlyCollection<T> OrderTestCollections<T>(IReadOnlyCollection<T> collections) where T : ITestCollection =>
        Seed is { } seed ? Shuffle(collections, seed, c => c.UniqueID) : DefaultTestCollectionOrderer.Instance.OrderTestCollections(collections);

    private static IReadOnlyCollection<T> Shuffle<T>(IEnumerable<T> items, int seed, Func<T, string> id)
    {
        var sorted = items.OrderBy(id, StringComparer.Ordinal).ToArray();
        new Random(seed).Shuffle(sorted);
        return sorted;
    }
}

/// <summary>PLAYBOOK P02, made structural: before EVERY test in this assembly the process-wide
/// state tests poke is snapshotted, and after it restored to the exact previous value/reference.
/// Covered: every static field/property any test file assigns (found by scanning this project's
/// sources, so a new seam is covered the moment a test assigns it), every settable static of the
/// Avalonia <c>App</c> service locator and of Core's <c>Core*</c> seam classes, the in-memory
/// settings object <see cref="CoreSettings.Current"/> plus Core's unseeded fallback, and the
/// top-level *.json files in the sandbox profile (settings.json, ...).</summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class IsolateProcessStateAttribute : BeforeAfterTestAttribute
{
    internal static ProcessStateSnapshot? Current { get; private set; }
    // On the shared Avalonia UI thread: reading a head static can create Dispatcher.UIThread, which
    // must be the thread the tests' headless platform lives on, and settings change handlers are UI code.
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() => Current = ProcessStateSnapshot.Take());
    public override void After(MethodInfo methodUnderTest, IXunitTest test) =>
        CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() => { Current?.Restore(); Current = null; });
}

internal sealed class ProcessStateSnapshot
{
    private readonly List<(MemberInfo Member, object? Value)> _statics = new();
    private readonly List<(AppSettings Settings, string Json)> _settings = new();
    private readonly Dictionary<string, byte[]> _files = new();

    private static readonly JsonSerializerSettings Json = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Error = (_, e) => e.ErrorContext.Handled = true,
    };

    internal static ProcessStateSnapshot Take()
    {
        var s = new ProcessStateSnapshot();
        foreach (var m in Members.Value) s._statics.Add((m, Get(m)));
        foreach (var settings in new[] { CoreSettings.Current, FallbackSettings() }.Distinct())
            s._settings.Add((settings, JsonConvert.SerializeObject(settings, Json)));
        foreach (var f in Directory.EnumerateFiles(TestUserDataProfile.Root, "*.json"))
            s._files[f] = File.ReadAllBytes(f);
        return s;
    }

    internal void Restore()
    {
        foreach (var (m, value) in _statics)
        {
            var now = Get(m);
            if (!ReferenceEquals(now, value) && !Equals(now, value)) Set(m, value);
        }
        foreach (var (settings, json) in _settings)
            if (JsonConvert.SerializeObject(settings, Json) != json) JsonConvert.PopulateObject(json, settings, Json);
        foreach (var f in Directory.EnumerateFiles(TestUserDataProfile.Root, "*.json"))
            if (!_files.ContainsKey(f)) try { File.Delete(f); } catch (IOException) { }
        foreach (var (f, bytes) in _files)
            if (!File.Exists(f) || !File.ReadAllBytes(f).AsSpan().SequenceEqual(bytes))
                try { File.WriteAllBytes(f, bytes); } catch (IOException) { }
    }

    private static AppSettings FallbackSettings() =>
        ((Lazy<AppSettings>)typeof(CoreSettings).GetField("Fallback", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!).Value;

    private static object? Get(MemberInfo m) => m is FieldInfo f ? f.GetValue(null) : ((PropertyInfo)m).GetValue(null);
    private static void Set(MemberInfo m, object? v)
    {
        if (m is FieldInfo f) f.SetValue(null, v); else ((PropertyInfo)m).SetValue(null, v);
    }

    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static bool Settable(MemberInfo m) => m switch
    {
        FieldInfo f => !f.IsPrivate && !f.IsInitOnly && !f.IsLiteral,
        PropertyInfo p => p.SetMethod is { IsStatic: true } && p.GetMethod is not null && p.GetIndexParameters().Length == 0,
        _ => false,
    };

    internal static readonly Lazy<IReadOnlyList<MemberInfo>> Members = new(FindMembers);

    private static IReadOnlyList<MemberInfo> FindMembers()
    {
        var core = typeof(CoreSettings).Assembly;
        var head = typeof(ConditioningControlPanel.Avalonia.App).Assembly;
        var types = new[] { core, head }
            .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>().ToArray(); } })
            .Where(t => !t.ContainsGenericParameters).ToList();
        var byName = types.ToLookup(t => t.Name);
        var found = new HashSet<MemberInfo>();

        // Whole seam classes: the head's service locator and Core's static Core* seams.
        foreach (var t in types.Where(t => t == typeof(ConditioningControlPanel.Avalonia.App)
                     || (t.Assembly == core && t.IsAbstract && t.IsSealed && t.Name.StartsWith("Core", StringComparison.Ordinal))))
            foreach (var m in t.GetMembers(AnyStatic).Where(Settable)) found.Add(m);

        // Every `Type.Member =` a test writes, through `using Alias = ...;` too.
        var alias = new Regex(@"^using\s+(\w+)\s*=\s*(?:global::)?([\w.]+)\s*;", RegexOptions.Multiline);
        var assign = new Regex(@"(?<![\w.])(?:global::)?((?:\w+\.)*\w+)\.(\w+)\s*=(?![=>])");
        foreach (var file in Directory.EnumerateFiles(SourceDir(), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            var aliases = alias.Matches(text).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            foreach (Match m in assign.Matches(text))
            {
                var qualifier = m.Groups[1].Value;
                var last = aliases.TryGetValue(qualifier, out var full) ? full.Split('.')[^1] : qualifier.Split('.')[^1];
                foreach (var t in byName[last])
                    foreach (var member in t.GetMember(m.Groups[2].Value, AnyStatic).Where(Settable))
                        found.Add(member);
            }
        }
        return found.ToList();
    }

    private static string SourceDir([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;
}
