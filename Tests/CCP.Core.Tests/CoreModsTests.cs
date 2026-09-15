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
}
