using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>main-sync 209659be8 (ccp-bugs #841): the tube shows the "listening" cue while a spoken
/// mantra's mic is open, for the first listen and the retry, and hides it after each.</summary>
public sealed class SpokenMantraListeningCueTests
{
    [Fact]
    public async Task EveryListenIsWrappedInTheListeningCue()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-mantra-cue-").FullName;
        var audio = Path.Combine(dir, "resources", "sounds", "companion_audio");
        Directory.CreateDirectory(audio);
        File.WriteAllText(Path.Combine(audio, "mantras.json"),
            "{\"mantras\":[{\"id\":\"m1\",\"phrase\":\"good girl\",\"promptText\":\"Say it\"}]}");
        var (pkg, id) = (CoreMods.ActiveModPackageProvider, CoreMods.ActiveModIdProvider);
        var modId = "cue-test-" + Guid.NewGuid().ToString("N");
        CoreMods.ActiveModIdProvider = () => modId;
        CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = modId }, dir, isBuiltIn: false);
        try
        {
            var log = new List<string>();
            var host = new SpokenMantraHost
            {
                Delay = (_, _) => Task.CompletedTask,
                ShowListening = p => log.Add("show:" + p),
                HideListening = () => log.Add("hide"),
                Recognize = (p, _) =>
                {
                    log.Add("listen");
                    return Task.FromResult(new PhraseResult { TimedOut = true });   // a miss: one retry
                },
            };
            await SpokenMantra.RunAsync(new MantraVoiceService(), host);
            Assert.Equal(new[] { "show:good girl", "listen", "hide", "show:good girl", "listen", "hide" }, log);
        }
        finally
        {
            (CoreMods.ActiveModPackageProvider, CoreMods.ActiveModIdProvider) = (pkg, id);
            Directory.Delete(dir, true);
        }
    }
}
