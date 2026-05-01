using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Settings the editor's clipboard / undo system uses for JSON-clone
        /// round-trips on individual TimelineItems / Regions / HapticEvents
        /// and full Enhancements. Matches the loader settings so every shape
        /// the on-disk format permits round-trips identically (including the
        /// custom polymorphic converters wired up via attributes on the
        /// EnhancementTrigger / EnhancementAction base classes).
        /// </summary>
        public static JsonSerializerSettings JsonReadSettingsForClone() => ReadSettings;

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

                // Additive-schema bridge: if the file pre-dates timeline_items but
                // has legacy regions/rules/haptic_tracks, project them so the
                // editor + engine can treat TimelineItems as authoritative.
                if (enhancement.TimelineItems.Count == 0 && HasLegacyContent(enhancement))
                    ProjectLegacyToTimeline(enhancement);

                // Effects-as-segments migration: items authored before effects had
                // a visible duration on the timeline have Duration=0 + a positive
                // EffectDurationMs. Project the latter into Duration so the editor
                // can render them as segments and users can drag-resize.
                foreach (var item in enhancement.TimelineItems)
                {
                    if (item != null
                        && item.Kind == TimelineItemKind.Effect
                        && item.Duration <= 0
                        && item.EffectDurationMs > 0)
                    {
                        item.Duration = item.EffectDurationMs / 1000.0;
                    }
                }

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

            // When the new editor is the source of truth, rebuild the legacy
            // collections from TimelineItems so files saved by this build still
            // load on older CCP versions that only know about regions/rules/haptic_tracks.
            if (e.TimelineItems.Count > 0)
                BackProjectTimelineToLegacy(e);

            return JsonConvert.SerializeObject(e, WriteSettings);
        }

        // -- Projection: legacy regions/rules/haptic_tracks → TimelineItems -------

        private static bool HasLegacyContent(Enhancement e)
        {
            if (e.Regions.Count > 0) return true;
            if (e.Rules.Count > 0) return true;
            foreach (var t in e.HapticTracks)
                if (t?.Events != null && t.Events.Count > 0) return true;
            return false;
        }

        /// <summary>
        /// Loader projection. Folds legacy regions+rules+haptic_tracks into the
        /// unified TimelineItems collection so the new editor and engine have a
        /// single authoritative source. The legacy collections are left in place
        /// (additive schema); save will rebuild them from TimelineItems.
        /// </summary>
        public static void ProjectLegacyToTimeline(Enhancement e)
        {
            var items = new List<TimelineItem>();
            var rulesByConstraint = e.Rules
                .GroupBy(r => r.RegionConstraint ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            // Regions become Rule TimelineItems. The first rule constrained to that
            // region folds into the band; extra rules with the same constraint stack
            // as same-span Rule items with fresh ids.
            var consumedRules = new HashSet<EnhancementRule>();
            foreach (var r in e.Regions)
            {
                rulesByConstraint.TryGetValue(r.Id, out var attached);
                attached ??= new List<EnhancementRule>();

                EnhancementRule? primary = attached.FirstOrDefault();
                items.Add(BuildRuleItemFromRegion(r, primary));
                if (primary != null) consumedRules.Add(primary);

                for (int k = 1; k < attached.Count; k++)
                {
                    var stacked = attached[k];
                    items.Add(BuildRuleItemFromRegion(r, stacked, freshId: true));
                    consumedRules.Add(stacked);
                }
            }

            // Rules with no region_constraint that didn't fold into a region.
            // time_reached → point-style (Duration = 0); other triggers → spans
            // the whole track so the rule fires anywhere it would have v1.
            foreach (var rule in e.Rules)
            {
                if (consumedRules.Contains(rule)) continue;

                if (rule.Trigger is TimeReachedTrigger tr)
                {
                    items.Add(new TimelineItem
                    {
                        Id = TimelineItem.NewId(),
                        Kind = TimelineItemKind.Rule,
                        Start = Math.Max(0, tr.Time),
                        Duration = 0,
                        Trigger = rule.Trigger,
                        Action = rule.Action,
                        CooldownMs = rule.CooldownMs,
                        Enabled = rule.Enabled
                    });
                }
                else
                {
                    items.Add(new TimelineItem
                    {
                        Id = TimelineItem.NewId(),
                        Kind = TimelineItemKind.Rule,
                        Start = 0,
                        Duration = double.MaxValue,
                        Trigger = rule.Trigger,
                        Action = rule.Action,
                        CooldownMs = rule.CooldownMs,
                        Enabled = rule.Enabled
                    });
                }
            }

            // Haptic events become Effect TimelineItems with effect_type=haptic.
            foreach (var track in e.HapticTracks)
            {
                if (track?.Events == null) continue;
                foreach (var ev in track.Events)
                {
                    if (ev == null) continue;
                    items.Add(new TimelineItem
                    {
                        Id = TimelineItem.NewId(),
                        Kind = TimelineItemKind.Effect,
                        Start = ev.Start,
                        Duration = ev.Duration,
                        EffectType = EffectTypes.Haptic,
                        EffectIntensity = ev.Intensity,
                        EffectDurationMs = (int)Math.Max(50, ev.Duration * 1000),
                        EffectPatternName = ev.PatternName,
                        EffectCustomPattern = ev.CustomPattern
                    });
                }
            }

            e.TimelineItems = items;
        }

        private static TimelineItem BuildRuleItemFromRegion(Region r, EnhancementRule? rule, bool freshId = false)
        {
            return new TimelineItem
            {
                Id = freshId ? TimelineItem.NewId() : (string.IsNullOrEmpty(r.Id) ? TimelineItem.NewId() : r.Id),
                Kind = TimelineItemKind.Rule,
                Start = r.Start,
                Duration = Math.Max(0, r.End - r.Start),
                Label = string.IsNullOrEmpty(r.Label) ? null : r.Label,
                Color = string.IsNullOrEmpty(r.Color) ? null : r.Color,
                Trigger = rule?.Trigger,
                Action = rule?.Action,
                CooldownMs = rule?.CooldownMs ?? 1000,
                Enabled = rule?.Enabled ?? false
            };
        }

        // -- Back-projection: TimelineItems → legacy collections ------------------

        /// <summary>
        /// Save back-projection. Rebuilds the v1 regions/rules/haptic_tracks
        /// collections from TimelineItems so files saved by this build still load
        /// on older CCP clients that only understand the legacy schema.
        /// Non-haptic Effects (flash/bubble/subliminal/overlay) have no v1
        /// equivalent — they only appear in <c>timeline_items</c>; older clients
        /// silently skip them.
        /// </summary>
        public static void BackProjectTimelineToLegacy(Enhancement e)
        {
            var regions = new List<Region>();
            var rules = new List<EnhancementRule>();
            var hapticEvents = new List<HapticEvent>();
            var seenRegionIds = new HashSet<string>();

            foreach (var ti in e.TimelineItems)
            {
                if (ti == null) continue;

                if (ti.Kind == TimelineItemKind.Rule)
                {
                    if (ti.Duration > 0 && ti.Duration < double.MaxValue)
                    {
                        // Band-style rule: emit a Region + (optional) Rule pointing at it.
                        var regionId = ti.Id;
                        if (string.IsNullOrEmpty(regionId) || !seenRegionIds.Add(regionId))
                            regionId = TimelineItem.NewId();

                        regions.Add(new Region
                        {
                            Id = regionId,
                            Start = ti.Start,
                            End = ti.Start + ti.Duration,
                            Label = ti.Label ?? "",
                            Color = ti.Color ?? "#7B5CFF"
                        });

                        if (ti.Trigger != null && ti.Action != null)
                        {
                            rules.Add(new EnhancementRule
                            {
                                Trigger = ti.Trigger,
                                Action = ti.Action,
                                RegionConstraint = regionId,
                                CooldownMs = ti.CooldownMs,
                                Enabled = ti.Enabled
                            });
                        }
                    }
                    else if (ti.Trigger != null && ti.Action != null)
                    {
                        // Point-style or unbounded rule: emit a free-standing Rule.
                        rules.Add(new EnhancementRule
                        {
                            Trigger = ti.Trigger,
                            Action = ti.Action,
                            RegionConstraint = null,
                            CooldownMs = ti.CooldownMs,
                            Enabled = ti.Enabled
                        });
                    }
                }
                else if (ti.Kind == TimelineItemKind.Effect && ti.EffectType == EffectTypes.Haptic)
                {
                    hapticEvents.Add(new HapticEvent
                    {
                        Start = ti.Start,
                        Duration = ti.Duration > 0 ? ti.Duration : Math.Max(0.05, ti.EffectDurationMs / 1000.0),
                        Intensity = ti.EffectIntensity,
                        PatternName = ti.EffectPatternName,
                        CustomPattern = ti.EffectCustomPattern
                    });
                }
                // Non-haptic Effects: no legacy shape — only timeline_items carries them.
            }

            e.Regions = regions;
            e.Rules = rules;
            if (hapticEvents.Count > 0)
            {
                e.HapticTracks = new List<HapticTrack>
                {
                    new() { Id = "primary", Events = hapticEvents }
                };
            }
            else
            {
                e.HapticTracks = new List<HapticTrack>();
            }
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

            // Additive-schema check: a v1-shaped fixture (legacy regions/rules/haptic_tracks
            // populated, timeline_items empty) must come back from Load with TimelineItems
            // populated by the projection pass.
            if (roundTripped.TimelineItems.Count == 0)
                errors.Add("TimelineItems was not populated by loader projection.");

            // Effect TimelineItem round-trip: build a fixture using the new model only,
            // round-trip it, and assert non-haptic effect fields survive.
            var v2Fixture = BuildV2Fixture();
            var v2Json = Save(v2Fixture);
            var v2Loaded = Load(v2Json);
            if (v2Loaded.TimelineItems.Count != v2Fixture.TimelineItems.Count)
                errors.Add($"v2 timeline_items count mismatch: {v2Fixture.TimelineItems.Count} vs {v2Loaded.TimelineItems.Count}");
            var flashItem = v2Loaded.TimelineItems.FirstOrDefault(t => t.EffectType == EffectTypes.Flash);
            if (flashItem == null) errors.Add("v2 flash effect lost on round-trip.");
            else if (flashItem.EffectImagePath != "stock:image1") errors.Add($"v2 flash image_path lost: {flashItem.EffectImagePath}");

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

        private static Enhancement BuildV2Fixture()
        {
            return new Enhancement
            {
                MediaType = MediaTypes.Video,
                MediaSource = "https://hypnotube.com/video/example.html",
                Metadata = new EnhancementMetadata
                {
                    Name = "v2 Self-Test",
                    Creator = "HT-uploader",
                    Remixer = "ccp-remixer",
                    Description = "Round-trip fixture covering TimelineItem effects."
                },
                TimelineItems = new List<TimelineItem>
                {
                    new()
                    {
                        Id = "intro-band", Kind = TimelineItemKind.Rule,
                        Start = 0, Duration = 10, Color = "#7B5CFF", Label = "Intro",
                        Trigger = new BlinkDetectedTrigger(),
                        Action = new TriggerEffectAction { EffectType = EffectTypes.Flash, ImagePath = "stock:image1", DurationMs = 800 }
                    },
                    new()
                    {
                        Id = "haptic-dot", Kind = TimelineItemKind.Effect,
                        Start = 5, Duration = 2,
                        EffectType = EffectTypes.Haptic, EffectIntensity = 0.7,
                        EffectDurationMs = 2000, EffectPatternName = "Pulse"
                    },
                    new()
                    {
                        Id = "flash-dot", Kind = TimelineItemKind.Effect,
                        Start = 7, Duration = 0,
                        EffectType = EffectTypes.Flash, EffectImagePath = "stock:image1",
                        EffectDurationMs = 600, EffectPlaySound = true
                    },
                    new()
                    {
                        Id = "subliminal-dot", Kind = TimelineItemKind.Effect,
                        Start = 12, Duration = 0,
                        EffectType = EffectTypes.Subliminal, EffectText = "obey",
                        EffectDurationMs = 200
                    },
                    new()
                    {
                        Id = "overlay-dot", Kind = TimelineItemKind.Effect,
                        Start = 20, Duration = 0,
                        EffectType = EffectTypes.Overlay, EffectOverlayKind = OverlayKinds.PinkFilter,
                        EffectDurationMs = 4000, EffectOpacity = 0.4
                    },
                    new()
                    {
                        Id = "bubble-dot", Kind = TimelineItemKind.Effect,
                        Start = 28, Duration = 0,
                        EffectType = EffectTypes.Bubble, EffectMaxBubbles = 5,
                        EffectDurationMs = 5000, EffectIntensity = 0.8
                    }
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
