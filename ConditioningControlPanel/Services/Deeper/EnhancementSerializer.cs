using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models.Deeper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Deeper
{
    public class EnhancementLoadException : Exception
    {
        public EnhancementLoadException(string message) : base(message) { }
        public EnhancementLoadException(string message, Exception inner) : base(message, inner) { }
    }

    public static class EnhancementSerializer
    {
        private static readonly JsonSerializerSettings ReadSettings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include
        };

        private static readonly JsonSerializerSettings WriteSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include
        };

        /// <summary>
        /// Loads an Enhancement from JSON. Throws <see cref="EnhancementLoadException"/>
        /// on schema mismatch, version-too-new, or malformed JSON.
        /// Unknown fields are preserved via <c>[JsonExtensionData]</c> for round-trip safety.
        /// </summary>
        public static Enhancement Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new EnhancementLoadException("File is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonReaderException ex)
            {
                throw new EnhancementLoadException($"Malformed JSON: {ex.Message}", ex);
            }

            var schema = root["$schema"]?.ToString();
            if (string.IsNullOrEmpty(schema) || schema != Enhancement.SchemaTag)
            {
                throw new EnhancementLoadException(
                    $"Not a Deeper enhancement file (expected $schema=\"{Enhancement.SchemaTag}\", got \"{schema ?? "<missing>"}\").");
            }

            var versionToken = root["version"];
            if (versionToken == null)
                throw new EnhancementLoadException("Missing version field.");

            int version;
            try { version = versionToken.Value<int>(); }
            catch
            {
                throw new EnhancementLoadException($"version must be an integer (got \"{versionToken}\").");
            }

            if (version > Enhancement.CurrentVersion)
            {
                throw new EnhancementLoadException(
                    $"This enhancement was authored in a newer version of Deeper (file version {version}, this build supports up to {Enhancement.CurrentVersion}). Update CCP to open it.");
            }

            try
            {
                var enhancement = root.ToObject<Enhancement>(JsonSerializer.Create(ReadSettings))
                                  ?? throw new EnhancementLoadException("Deserialization returned null.");
                return enhancement;
            }
            catch (EnhancementLoadException) { throw; }
            catch (Exception ex)
            {
                throw new EnhancementLoadException($"Failed to parse enhancement: {ex.Message}", ex);
            }
        }

        public static string Save(Enhancement e)
        {
            // Ensure the schema tag is current on save (the in-memory value may have been
            // tampered with).
            e.Schema = Enhancement.SchemaTag;
            if (e.Version <= 0) e.Version = Enhancement.CurrentVersion;
            return JsonConvert.SerializeObject(e, WriteSettings);
        }

        /// <summary>
        /// Diagnostic round-trip self-check. Builds a fixture covering every trigger
        /// and action type, serializes, deserializes, and asserts shape preservation.
        /// Throws on any divergence. Not invoked by normal app flow — call from a
        /// debug menu or unit test harness.
        /// </summary>
        public static void SelfTest()
        {
            var fixture = BuildFixture();
            var json = Save(fixture);
            var roundTripped = Load(json);

            var errors = new List<string>();

            if (roundTripped.MediaType != fixture.MediaType)
                errors.Add($"media_type mismatch: {fixture.MediaType} vs {roundTripped.MediaType}");
            if (roundTripped.Regions.Count != fixture.Regions.Count)
                errors.Add($"region count mismatch: {fixture.Regions.Count} vs {roundTripped.Regions.Count}");
            if (roundTripped.HapticTracks.Count != fixture.HapticTracks.Count)
                errors.Add($"haptic_track count mismatch");
            if (roundTripped.Rules.Count != fixture.Rules.Count)
                errors.Add($"rule count mismatch: {fixture.Rules.Count} vs {roundTripped.Rules.Count}");

            for (int i = 0; i < System.Math.Min(fixture.Rules.Count, roundTripped.Rules.Count); i++)
            {
                if (fixture.Rules[i].Trigger.Type != roundTripped.Rules[i].Trigger.Type)
                    errors.Add($"rules[{i}].trigger.type mismatch: {fixture.Rules[i].Trigger.Type} vs {roundTripped.Rules[i].Trigger.Type}");
                if (fixture.Rules[i].Action.Type != roundTripped.Rules[i].Action.Type)
                    errors.Add($"rules[{i}].action.type mismatch: {fixture.Rules[i].Action.Type} vs {roundTripped.Rules[i].Action.Type}");
            }

            // Unknown-type round-trip: inject a hand-crafted action with an unknown type
            // and verify it survives a load → save cycle.
            var craftedJson = json.Replace("\"type\": \"pause\"", "\"type\": \"future_unknown_action\", \"future_field\": 42");
            var crafted = Load(craftedJson);
            var craftedRoundTrip = Save(crafted);
            if (!craftedRoundTrip.Contains("future_unknown_action"))
                errors.Add("Unknown action type was lost on round-trip.");
            if (!craftedRoundTrip.Contains("future_field"))
                errors.Add("Unknown action field was lost on round-trip.");

            // Validator catches expected violations.
            var bad = BuildInvalidFixture();
            var validationErrors = EnhancementValidator.Validate(bad);
            if (validationErrors.Count == 0)
                errors.Add("Validator failed to catch any errors on intentionally-invalid fixture.");

            // Version-too-new is rejected.
            try
            {
                Load(json.Replace("\"version\": 1", "\"version\": 999"));
                errors.Add("Loader did not reject version=999.");
            }
            catch (EnhancementLoadException) { /* expected */ }

            if (errors.Count > 0)
                throw new System.Exception("EnhancementSerializer self-test failed:\n  " + string.Join("\n  ", errors));
        }

        private static Enhancement BuildFixture()
        {
            return new Enhancement
            {
                MediaType = MediaTypes.Video,
                MediaSource = "https://hypnotube.com/video/*",
                Metadata = new EnhancementMetadata
                {
                    Name = "Self-Test Fixture",
                    Creator = "CCP",
                    Description = "Round-trip fixture covering every trigger and action type.",
                    Tags = new List<string> { "test", "fixture" }
                },
                Regions = new List<Region>
                {
                    new() { Id = "intro", Start = 0, End = 10, Label = "Intro", Color = "#7B5CFF" },
                    new() { Id = "main",  Start = 10, End = 60, Label = "Main",  Color = "#FF69B4" }
                },
                HapticTracks = new List<HapticTrack>
                {
                    new()
                    {
                        Id = "primary",
                        Events = new List<HapticEvent>
                        {
                            new() { Start = 1, Duration = 2, Intensity = 0.6, PatternName = "Pulse" },
                            new() { Start = 5, Duration = 3, Intensity = 1.0, CustomPattern = new List<double[]>
                                {
                                    new[] { 0.0, 0.0 },
                                    new[] { 0.5, 1.0 },
                                    new[] { 1.0, 0.3 }
                                } }
                        }
                    }
                },
                Rules = new List<EnhancementRule>
                {
                    new() { Trigger = new GazeTargetTrigger { Rect = new[] { 0.4, 0.4, 0.2, 0.2 }, MinDwellMs = 500 },
                            Action  = new TriggerHapticAction { PatternName = "Throb", Intensity = 0.8, DurationMs = 1500 } },
                    new() { Trigger = new GazeAvoidTrigger    { Rect = new[] { 0.0, 0.0, 1.0, 1.0 }, MinDwellMs = 300 },
                            Action  = new ScreenShakeAction    { Intensity = 0.3, DurationMs = 400 } },
                    new() { Trigger = new AttentionLostTrigger { MinDurationMs = 2000 },
                            Action  = new SetIntensityAction    { Value = 0.4 } },
                    new() { Trigger = new BlinkDetectedTrigger(),
                            Action  = new PlayAudioAction { Path = "stock:gentle_chime", Volume = 70, DuckOtherAudio = true } },
                    new() { Trigger = new MouthOpenTrigger(),
                            Action  = new PauseAction() },
                    new() { Trigger = new TimeReachedTrigger { Time = 30 },
                            Action  = new SeekAction { Target = SeekTargets.RegionStart, RegionId = "main" } },
                    new() { Trigger = new RegionEnteredTrigger { RegionId = "main" },
                            Action  = new LoopRegionAction { RegionId = "main" } },
                    new() { Trigger = new RegionExitedTrigger { RegionId = "main" },
                            Action  = new SeekAction { Target = SeekTargets.Time, Time = 0 } }
                }
            };
        }

        private static Enhancement BuildInvalidFixture()
        {
            return new Enhancement
            {
                MediaType = MediaTypes.Audio,
                Regions = new List<Region>
                {
                    new() { Id = "a", Start = 0,  End = 10 },
                    new() { Id = "a", Start = 5,  End = 15 } // duplicate id + overlap
                },
                Rules = new List<EnhancementRule>
                {
                    new()
                    {
                        // Video-only trigger on an audio enhancement — should be rejected.
                        Trigger = new GazeTargetTrigger { Rect = new[] { 2.0, 0.0, 1.0, 1.0 } }, // also: rect[0] out of range
                        Action  = new TriggerHapticAction
                        {
                            PatternName = "Pulse",
                            CustomPattern = new List<double[]> { new[] { 0.0, 0.5 } }, // both set
                            Intensity = 1.5 // out of range
                        }
                    }
                }
            };
        }
    }
}
