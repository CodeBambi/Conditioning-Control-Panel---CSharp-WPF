using System;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SessionFileServicePersistenceTests
{
    [Fact]
    public void ExportAndImportDefinition_RoundTripsFileContractAndTimelineSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-session-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "known.session.json");
            var definition = new SessionDefinition
            {
                Id = "roundtrip-session",
                Name = "Roundtrip Session",
                Icon = "🌸",
                VibeSummary = "A known vibe",
                Description = "A known description",
                ImagePath = "sessions/roundtrip.png",
                DurationMinutes = 45,
                Difficulty = SessionDifficulty.Hard,
                BonusXP = 1200,
                IsAvailable = false,
                HasCornerGifOption = true,
                CornerGifDescription = "Known corner option",
                Source = SessionSource.Custom,
                SourceFilePath = "not-written-to-json",
                Settings = new SessionSettings
                {
                    FlashEnabled = true,
                    FlashPerHour = 37,
                    FlashOpacity = 73,
                    FlashAudioEnabled = false,
                    BubblesClickable = false,
                    RampCurve = RampCurve.EaseOut
                },
                Phases = new()
                {
                    new SessionPhase { StartMinute = 0, Name = "Settle", Description = "Start phase" },
                    new SessionPhase { StartMinute = 20, Name = "Deepen", Description = "Second phase" }
                },
                TimelineEvents = new()
                {
                    new TimelineEvent
                    {
                        Id = "flash-start",
                        FeatureId = "flash",
                        Minute = 3,
                        EventType = TimelineEventType.Start,
                        PairedEventId = "flash-stop",
                        StartValue = 10,
                        EndValue = 80,
                        Settings = new()
                        {
                            ["perHour"] = 37,
                            ["clickable"] = false
                        }
                    },
                    new TimelineEvent
                    {
                        Id = "flash-stop",
                        FeatureId = "flash",
                        Minute = 18,
                        EventType = TimelineEventType.Stop,
                        PairedEventId = "flash-start"
                    }
                }
            };

            var service = new SessionFileService();
            service.ExportSession(definition, path);

            Assert.True(File.Exists(path));
            using (var document = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var root = document.RootElement;
                Assert.Equal("roundtrip-session", root.GetProperty("id").GetString());
                Assert.Equal("hard", root.GetProperty("difficulty").GetString());
                Assert.Equal(37, root.GetProperty("settings").GetProperty("flashPerHour").GetInt32());
                Assert.Equal("easeOut", root.GetProperty("settings").GetProperty("rampCurve").GetString());
                Assert.Equal("start", root.GetProperty("timelineEvents")[0].GetProperty("eventType").GetString());
                Assert.False(root.TryGetProperty("Id", out _));
                Assert.False(root.TryGetProperty("source", out _));
                Assert.False(root.TryGetProperty("sourceFilePath", out _));
                Assert.Equal("flash-stop", root.GetProperty("timelineEvents")[0].GetProperty("pairedEventId").GetString());
            }

            Assert.True(service.ValidateSessionFile(path, out var validationError), validationError);
            var imported = service.ImportSession(path);

            Assert.NotNull(imported);
            Assert.Equal(SessionSource.Imported, imported!.Source);
            Assert.Equal(path, imported.SourceFilePath);
            Assert.Equal(definition.Id, imported.Id);
            Assert.Equal(definition.Name, imported.Name);
            Assert.Equal(definition.Icon, imported.Icon);
            Assert.Equal(definition.VibeSummary, imported.VibeSummary);
            Assert.Equal(definition.Description, imported.Description);
            Assert.Equal(definition.ImagePath, imported.ImagePath);
            Assert.Equal(definition.DurationMinutes, imported.DurationMinutes);
            Assert.Equal(definition.Difficulty, imported.Difficulty);
            Assert.Equal(definition.BonusXP, imported.BonusXP);
            Assert.Equal(definition.IsAvailable, imported.IsAvailable);
            Assert.Equal(definition.HasCornerGifOption, imported.HasCornerGifOption);
            Assert.Equal(definition.CornerGifDescription, imported.CornerGifDescription);

            Assert.True(imported.Settings.FlashEnabled);
            Assert.Equal(37, imported.Settings.FlashPerHour);
            Assert.Equal(73, imported.Settings.FlashOpacity);
            Assert.False(imported.Settings.FlashAudioEnabled);
            Assert.False(imported.Settings.BubblesClickable);
            Assert.Equal(RampCurve.EaseOut, imported.Settings.RampCurve);

            Assert.Equal(2, imported.Phases.Count);
            Assert.Equal("Settle", imported.Phases[0].Name);
            Assert.Equal(20, imported.Phases[1].StartMinute);
            Assert.Equal("Second phase", imported.Phases[1].Description);

            Assert.Equal(2, imported.TimelineEvents.Count);
            var start = imported.TimelineEvents[0];
            Assert.Equal("flash-start", start.Id);
            Assert.Equal("flash", start.FeatureId);
            Assert.Equal(3, start.Minute);
            Assert.Equal(TimelineEventType.Start, start.EventType);
            Assert.Equal("flash-stop", start.PairedEventId);
            Assert.Equal(10, start.StartValue);
            Assert.Equal(80, start.EndValue);
            Assert.IsType<JsonElement>(start.Settings["perHour"]);
            Assert.Equal(37, start.GetSetting("perHour", -1));
            Assert.False(start.GetSetting("clickable", true));

            var stop = imported.TimelineEvents[1];
            Assert.Equal(TimelineEventType.Stop, stop.EventType);
            Assert.Equal("flash-start", stop.PairedEventId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ValidationAndImport_HandleMalformedWrongExtensionAndMissingFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ccp-session-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new SessionFileService();
            var malformedPath = Path.Combine(directory, "malformed.session.json");
            File.WriteAllText(malformedPath, "{ not valid json");

            Assert.Null(service.ImportSession(malformedPath));
            Assert.False(service.ValidateSessionFile(malformedPath, out var malformedError));
            Assert.StartsWith("Invalid JSON:", malformedError);

            var wrongExtensionPath = Path.Combine(directory, "valid.json");
            File.WriteAllText(wrongExtensionPath, "{\"id\":\"valid\",\"name\":\"Valid\",\"durationMinutes\":1}");
            Assert.False(service.ValidateSessionFile(wrongExtensionPath, out var extensionError));
            Assert.Equal("File must be a .session.json file", extensionError);

            var missingPath = Path.Combine(directory, "missing.session.json");
            Assert.False(File.Exists(missingPath));
            Assert.Null(service.ImportSession(missingPath));
            Assert.False(service.ValidateSessionFile(missingPath, out var missingError));
            Assert.Equal("File not found", missingError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
