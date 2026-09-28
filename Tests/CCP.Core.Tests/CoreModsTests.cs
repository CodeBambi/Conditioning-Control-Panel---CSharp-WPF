using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CoreModsTests
{
    [Fact]
    public void ActiveModPackageProvider_IsLazyAndFaultSafe()
    {
        var previous = CoreMods.ActiveModPackageProvider;
        try
        {
            CoreMods.ActiveModPackageProvider = null;
            Assert.Null(CoreMods.ActiveModPackage);

            var package = new ModPackage(new ModManifest { Id = "test-mod" }, null, isBuiltIn: false);
            CoreMods.ActiveModPackageProvider = () => package;
            Assert.Same(package, CoreMods.ActiveModPackage);

            CoreMods.ActiveModPackageProvider = () => throw new InvalidOperationException("test");
            Assert.Null(CoreMods.ActiveModPackage);
        }
        finally
        {
            CoreMods.ActiveModPackageProvider = previous;
        }
    }

    /// <summary>ModCompanionContent moved to Core and reads the active mod through CoreMods, not
    /// App.Mods: a seeded package's packaged content must win rung 1.</summary>
    [Fact]
    public void ModCompanionContent_ResolveActive_ReadsTheSeededMod()
    {
        var prevId = CoreMods.ActiveModIdProvider;
        var prevPkg = CoreMods.ActiveModPackageProvider;
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-mcc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = System.IO.Path.Combine(root, "resources", "sounds", "companion_audio", "bark_rules.json");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            System.IO.File.WriteAllText(file, "{}");
            var package = new ModPackage(new ModManifest { Id = "test-mod" }, root, isBuiltIn: false);
            CoreMods.ActiveModIdProvider = () => "test-mod";
            CoreMods.ActiveModPackageProvider = () => package;

            var pick = ConditioningControlPanel.Services.Companion.ModCompanionContent.ResolveActive(
                ConditioningControlPanel.Services.Companion.CompanionChannel.BarkRules);

            Assert.Equal(ConditioningControlPanel.Services.Companion.CompanionContentSource.PackagedMod, pick.Source);
            Assert.Equal(file, pick.Path);
        }
        finally
        {
            CoreMods.ActiveModIdProvider = prevId;
            CoreMods.ActiveModPackageProvider = prevPkg;
            try { System.IO.Directory.Delete(root, true); } catch { }
        }
    }
}
