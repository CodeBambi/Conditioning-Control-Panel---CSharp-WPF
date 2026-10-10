using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Games.BackRoom;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>IB7: the race opened from the Back Room's door (WPF BackRoomHostService.OnRoomClosed /
/// ReturnToRoom). The room closes, the race opens, and the room comes back when the race ends by
/// itself; a panic or the title-bar X ends the trip where it is.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps the process-wide race door seams
public sealed class BackRoomRaceReturnTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class Door
    {
        public string? Refusal;
        public int Launches, Returns;
        public bool LaunchFails;
        public GameWindow? Race;
        public readonly List<GameWindow> Open = new();
    }

    private static Task Trip(Action<Door, Func<(GameWindow Room, List<JObject> Posted)>> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var door = new Door();
        GameWindow.RoomVoiceFactory = _ => NullBackRoomVoice.Instance;
        GameWindow.RoomRaceRefusal = () => door.Refusal;
        GameWindow.RoomRaceLaunch = () =>
        {
            door.Launches++;
            if (door.LaunchFails) return null;
            var race = new GameWindow(RaceWindow.Spec);
            race.MarkRaceFromRoom();
            race.Show();
            door.Open.Add(race);
            return door.Race = race;
        };
        GameWindow.RoomReturn = () => door.Returns++;
        try
        {
            body(door, () =>
            {
                var w = new GameWindow(GameWindow.Games["backroom"]);
                var posted = new List<JObject>();
                w.Posted += json => posted.Add(JObject.Parse(json));
                w.Show();
                door.Open.Add(w);
                w.HandleMessage("{\"type\":\"ready\"}");
                return (w, posted);
            });
        }
        finally
        {
            foreach (var w in door.Open) { try { w.Close(); } catch { } }
            GameWindow.RoomRaceRefusal = GameWindow.DefaultRoomRaceRefusal;
            GameWindow.RoomRaceLaunch = GameWindow.DefaultRoomRaceLaunch;
            GameWindow.RoomReturn = GameWindow.DefaultRoomReturn;
            GameWindow.RoomVoiceFactory = breakout => new BackRoomVoice(breakout);
        }
        return Task.CompletedTask;
    });

    private const string GameOpen = "{\"type\":\"game-open\",\"game\":\"race\"}";

    private static JObject Result(List<JObject> posted) => posted.Last(p => (string?)p["type"] == "game-open-result");

    [Fact]
    public Task AShutDoor_AnswersTheReason_AndTheRoomStays() => Trip((door, open) =>
    {
        door.Refusal = "locked";
        var (room, posted) = open();
        room.HandleMessage(GameOpen);
        Assert.False((bool)Result(posted)["ok"]!);
        Assert.Equal("locked", (string?)Result(posted)["reason"]);
        room.Close();
        Assert.Equal(0, door.Launches);
        Assert.Equal(0, door.Returns);
    });

    [Fact]
    public Task TheRaceOpensWhenTheRoomHasClosed_AndTheRoomComesBackWhenItEnds() => Trip((door, open) =>
    {
        var (room, posted) = open();
        room.HandleMessage(GameOpen);
        Assert.True((bool)Result(posted)["ok"]!);
        Assert.Equal(0, door.Launches);   // not before the room has wound down
        room.Close();
        Assert.Equal(1, door.Launches);
        Assert.True(door.Race!.RaceFromRoom);
        Assert.Equal(0, door.Returns);
        door.Race.Close();
        Assert.Equal(1, door.Returns);
    });

    [Fact]
    public Task ASecondKnock_IsOneTrip() => Trip((door, open) =>
    {
        var (room, _) = open();
        room.HandleMessage(GameOpen);
        room.HandleMessage(GameOpen);
        room.Close();
        Assert.Equal(1, door.Launches);
    });

    [Fact]
    public Task ADoorThatShutWhileTheRoomClosed_PutsTheRoomBack() => Trip((door, open) =>
    {
        var (room, _) = open();
        room.HandleMessage(GameOpen);
        door.Refusal = "busy";
        room.Close();
        Assert.Equal(0, door.Launches);
        Assert.Equal(1, door.Returns);
    });

    [Fact]
    public Task ARaceThatDidNotOpen_PutsTheRoomBack() => Trip((door, open) =>
    {
        door.LaunchFails = true;
        var (room, _) = open();
        room.HandleMessage(GameOpen);
        room.Close();
        Assert.Equal(1, door.Launches);
        Assert.Equal(1, door.Returns);
    });

    [Fact]
    public Task Panic_DuringTheRace_NeverBringsTheRoomBack() => Trip((door, open) =>
    {
        var (room, _) = open();
        room.HandleMessage(GameOpen);
        room.Close();
        Assert.NotNull(door.Race);
        GameWindow.CloseAllForPanic();
        Assert.Equal(0, door.Returns);
    });

    [Fact]
    public Task Panic_WhileTheRoomIsClosing_OpensNoRace() => Trip((door, open) =>
    {
        var (room, _) = open();
        room.HandleMessage(GameOpen);
        GameWindow.CloseAllForPanic();
        Assert.Equal(0, door.Launches);
        Assert.Equal(0, door.Returns);
    });

    [Fact]
    public Task ARoomClosedWithoutTheDoor_OpensNothing() => Trip((door, open) =>
    {
        var (room, _) = open();
        room.Close();
        Assert.Equal(0, door.Launches);
        Assert.Equal(0, door.Returns);
    });

    [Fact]
    public void TheReturnUrlIsThePagesOwn()
    {
        Assert.Equal("raceReturn=1", GameWindow.RaceReturnQuery);
        Assert.Equal("backroom/index.html", GameWindow.Games["backroom"].Page);
    }
}
