using System;
using System.IO;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>"Open with CCP": WPF App.xaml.cs:90-173 (--play / --edit and the handoff file).</summary>
public sealed class FileOpenHandoffTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-fileopen-" + Guid.NewGuid().ToString("N"));
    private readonly string _media;

    public FileOpenHandoffTests()
    {
        Directory.CreateDirectory(_dir);
        _media = Path.Combine(_dir, "clip.mp4");
        File.WriteAllText(_media, "x");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void PlayAndEditNameAnExistingMediaFile()
    {
        Assert.Equal(("play", _media), FileOpenHandoff.ParseArgs(new[] { "--play", _media }));
        Assert.Equal(("edit", _media), FileOpenHandoff.ParseArgs(new[] { "--panel", "--edit", _media }));
    }

    [Fact]
    public void AnythingElseIsRefused()
    {
        var text = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(text, "x");
        Assert.Equal((null, null), FileOpenHandoff.ParseArgs(new[] { "--play", text }));                         // not media
        Assert.Equal((null, null), FileOpenHandoff.ParseArgs(new[] { "--play", Path.Combine(_dir, "no.mp4") })); // missing
        Assert.Equal((null, null), FileOpenHandoff.ParseArgs(new[] { "--play", "//server/share/clip.mp4".Replace('/', Path.DirectorySeparatorChar) }));
        Assert.Equal((null, null), FileOpenHandoff.ParseArgs(new[] { "--play" }));                               // no file
        Assert.Equal((null, null), FileOpenHandoff.ParseArgs(new[] { "--panel" }));
        Assert.Equal((null, null), FileOpenHandoff.ParseArgs(null));
    }

    [Fact]
    public void TheHandoffFileIsWpfsTwoLines()
    {
        FileOpenHandoff.Write(_dir, "play", _media);
        Assert.Equal("play\n" + _media, File.ReadAllText(Path.Combine(_dir, "fileopen.pending")));
        Assert.Equal(("play", _media), FileOpenHandoff.Consume(_dir));
        Assert.False(File.Exists(FileOpenHandoff.PathIn(_dir)));          // read once
        Assert.Equal((null, null), FileOpenHandoff.Consume(_dir));
    }

    [Fact]
    public void ASurfaceRidesTheSameFile()
    {
        FileOpenHandoff.Write(_dir, LauncherHandoff.Action, "game:race");
        Assert.Equal((LauncherHandoff.Action, "game:race"), FileOpenHandoff.Consume(_dir));
    }

    [Fact]
    public void ABadHandoffReadsAsNone()
    {
        File.WriteAllText(FileOpenHandoff.PathIn(_dir), "delete\n" + _media);
        Assert.Equal((null, null), FileOpenHandoff.Consume(_dir));
        File.WriteAllText(FileOpenHandoff.PathIn(_dir), "play\n" + Path.Combine(_dir, "gone.mp4"));
        Assert.Equal((null, null), FileOpenHandoff.Consume(_dir));
        File.WriteAllText(FileOpenHandoff.PathIn(_dir), "play");
        Assert.Equal((null, null), FileOpenHandoff.Consume(_dir));
    }

    [Fact]
    public void VideoOrAudioDecidesTheEditorsBlank()
    {
        Assert.True(FileOpenHandoff.IsVideo("a.MKV"));
        Assert.False(FileOpenHandoff.IsVideo("a.flac"));
    }
}
