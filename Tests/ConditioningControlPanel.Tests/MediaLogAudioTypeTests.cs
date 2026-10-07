using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1098: the media log gained an Audio type. The logs store MediaType by ordinal, so the
/// old members must keep their numbers or every saved entry would change type on load.
/// </summary>
public class MediaLogAudioTypeTests
{
    [Fact]
    public void Ordinals_are_stable()
    {
        Assert.Equal(0, (int)MediaType.Video);
        Assert.Equal(1, (int)MediaType.Image);
        Assert.Equal(2, (int)MediaType.Audio);
    }

    [Fact]
    public void Old_entries_still_load_and_audio_round_trips()
    {
        var old = JsonConvert.DeserializeObject<MediaLogEntry>("{\"type\":1,\"file_path\":\"C:\\\\a.png\"}");
        Assert.Equal(MediaType.Image, old!.Type);

        var json = JsonConvert.SerializeObject(new MediaLogEntry { Type = MediaType.Audio, FilePath = "C:\\b.mp3" });
        Assert.Equal(MediaType.Audio, JsonConvert.DeserializeObject<MediaLogEntry>(json)!.Type);
    }
}
