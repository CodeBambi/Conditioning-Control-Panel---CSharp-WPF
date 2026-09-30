using System.Reflection;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>BambiSprite moved to Core reads the mod through CoreMods.Service. With the Bambi mod
/// there, the media block takes the Bambi branch (HOW TO LINK + BambiCloud playlists) - the same
/// seam the WPF PromptCharBudgetTests / PersonaWireFidelityTests now set.</summary>
public sealed class BambiSpriteCoreModsTests
{
    private static readonly FieldInfo ServiceField = typeof(CoreMods).GetField("<Service>k__BackingField",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void BambiModOnCoreModsServiceBuildsTheHowToLinkBlock()
    {
        var prior = ServiceField.GetValue(null);
        var mods = (ModService)RuntimeHelpers.GetUninitializedObject(typeof(ModService));
        typeof(ModService).GetField("_activeMod", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(mods, new ModPackage(BuiltInMods.BambiSleep, null, isBuiltIn: true));
        ServiceField.SetValue(null, mods);
        BambiSprite.VideoPoolProvider = () => BuiltInMods.BambiSleep.Browser!.DefaultVideoLinks;
        try
        {
            var block = (string)typeof(BambiSprite).GetMethod("GetCoreMediaLinks", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(new BambiSprite(), null)!;
            Assert.Contains("HOW TO LINK", block);
            Assert.Contains("bambicloud.com/playlist/", block);
        }
        finally
        {
            BambiSprite.VideoPoolProvider = null;
            ServiceField.SetValue(null, prior);
        }
    }
}
