using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Services.FirstShow;

/// <summary>Native Emi bleepese and short, disposable show cues.</summary>
internal sealed class FirstShowAudio : IDisposable
{
    private readonly MediaPlayer _voice = new(), _cue = new();
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "ccp-first-show-" + Guid.NewGuid().ToString("N"));
    private readonly bool _preview;
    private bool _disposed;
    public FirstShowAudio(bool preview) { _preview = preview; }
    private double Volume => App.Audio?.IsOutputSuppressed == true ? 0 : Math.Clamp((App.Settings?.Current?.MasterVolume ?? (_preview ? 65 : 0)) / 100.0, 0, 1);
    public void Speak(string text, string mood = "idle")
    {
        if (_disposed || Volume <= 0) return;
        try
        {
            Directory.CreateDirectory(_cache);
            var path = Path.Combine(_cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + mood))) + ".wav");
            if (!File.Exists(path)) File.WriteAllBytes(path, EmiVox.WriteWav(EmiVox.RenderBurst(EmiVox.MakeScore(text, mood))));
            _voice.Stop(); _voice.Open(new Uri(path)); _voice.Volume = Volume; _voice.Play();
        }
        catch (Exception ex) { App.Logger?.Debug(ex, "First show speech unavailable"); }
    }
    public void Cue(string name)
    {
        if (_disposed || Volume <= 0) return;
        var asset = name switch
        {
            "gather" => "chaos/sink.mp3", "reveal" => "chaos/reveal_chime.mp3",
            "pop" => "bubbles/Pop3.mp3", "pink" => "chaos/ripple_cast.mp3",
            "drain" => "chaos/time_slow_in.mp3", "flash" => "chaos/chip_pop.mp3",
            _ => "chaos/ui_equip.mp3"
        };
        try
        {
            var path = ModResourceResolver.ResolveAudioPath(asset);
            if (!File.Exists(path)) return;
            _cue.Stop(); _cue.Open(new Uri(path)); _cue.Volume = Volume * .32; _cue.Play();
        }
        catch (Exception ex) { App.Logger?.Debug(ex, "First show cue unavailable"); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _voice.Close(); _cue.Close();
        try { if (Directory.Exists(_cache)) Directory.Delete(_cache, true); }
        catch (IOException) { }
    }
}
