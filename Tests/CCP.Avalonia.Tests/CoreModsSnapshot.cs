using System;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel;

namespace CCP.Avalonia.Tests;

/// <summary>App.StartMods seeds CoreMods' static providers from the test's own ModService. Left seeded,
/// every later test (xunit v3 shuffles class order) sees that test's last mod. Take one before StartMods,
/// dispose it in the test's finally.</summary>
internal sealed class CoreModsSnapshot : IDisposable
{
    private static readonly FieldInfo[] Providers = typeof(CoreMods)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.Name.EndsWith("Provider", StringComparison.Ordinal)).ToArray();

    private readonly object?[] _saved = Providers.Select(f => f.GetValue(null)).ToArray();
    private readonly Func<System.Collections.Generic.IReadOnlyDictionary<string, string>?>? _links =
        CoreModsHooks.KnownVideoLinksProvider;

    public void Dispose()
    {
        for (var i = 0; i < Providers.Length; i++) Providers[i].SetValue(null, _saved[i]);
        CoreModsHooks.KnownVideoLinksProvider = _links;
    }
}
