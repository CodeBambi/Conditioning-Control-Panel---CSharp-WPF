using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    public enum ValidationSeverity
    {
        Warning,
        Error
    }

    public class ValidationError
    {
        public string Path { get; set; } = "";
        public string Message { get; set; } = "";
        public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;

        public override string ToString() => $"[{Severity}] {Path}: {Message}";
    }

    public static class EnhancementValidator
    {
        public static List<ValidationError> Validate(Enhancement e)
        {
            var errors = new List<ValidationError>();

            // Newtonsoft accepts non-standard "NaN"/"Infinity" JSON literals by
            // default, and a NaN double makes every range comparison silently
            // false (NaN < 0 == false, NaN > 1 == false), bypassing every
            // bounds check below and producing nondeterministic OrderBy on
            // overlap detection. Reject non-finite doubles up-front so the
            // regular range checks operate on real numbers only.
            ValidateAllDoublesFinite(e, errors);

            ValidateMediaType(e, errors);
            ValidateTimelineItems(e, errors);

            // Legacy rule validation always runs. The editor adds band-style
            // rules directly to _enhancement.Rules + _enhancement.Regions and
            // only back-projects into TimelineItems on save, so a session
            // that hasn't saved has user state living only in the legacy
            // collections. Skip rules already represented in TimelineItems so
            // Load-projected files don't fire duplicate errors.
            var representedTriggers = new HashSet<EnhancementTrigger>();
            foreach (var item in e.TimelineItems)
            {
                if (item?.Trigger != null) representedTriggers.Add(item.Trigger);
            }
            ValidateRules(e, errors, skipTrigger: representedTriggers);

            // Region/haptic legacy validation only when TimelineItems is empty
            // — projected files have intentional overlaps from band-stacking
            // that the legacy region overlap check would flag as errors.
            if (e.TimelineItems.Count == 0)
            {
                ValidateRegions(e, errors);
                ValidateHapticTracks(e, errors);
            }

            return errors;
        }

        private static void ValidateTimelineItems(Enhancement e, List<ValidationError> errors)
        {
            bool isAudio = e.MediaType == MediaTypes.Audio;
            var itemIds = new HashSet<string>();
            // TimelineItem ids form the namespace that region_entered/region_exited/seek/loop
            // triggers and actions reference, so we collect them up-front.
            var bandIds = new HashSet<string>();
            foreach (var item in e.TimelineItems)
            {
                if (item == null) continue;
                if (item.Kind == TimelineItemKind.Rule && item.Duration > 0 && item.Duration < double.MaxValue)
                    bandIds.Add(item.Id);
            }

            for (int i = 0; i < e.TimelineItems.Count; i++)
            {
                var item = e.TimelineItems[i];
                if (item == null) continue;
                var path = $"timeline_items[{i}]";

                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    errors.Add(new ValidationError { Path = path, Message = "Item id cannot be empty." });
                }
                else if (!itemIds.Add(item.Id))
                {
                    errors.Add(new ValidationError { Path = path, Message = $"Duplicate timeline item id \"{item.Id}\"." });
                }

                if (item.Start < 0)
                    errors.Add(new ValidationError { Path = path, Message = $"start must be >= 0 (got {item.Start})." });

                if (item.Duration < 0)
                    errors.Add(new ValidationError { Path = path, Message = $"duration must be >= 0 (got {item.Duration})." });

                if (item.CooldownMs < 0)
                    errors.Add(new ValidationError { Path = path, Message = $"cooldown_ms must be >= 0 (got {item.CooldownMs})." });

                if (item.Kind == TimelineItemKind.Effect)
                {
                    ValidateEffectPayload(item, path, errors);
                }
                else // Rule
                {
                    if (item.Trigger == null)
                    {
                        if (item.Action != null)
                            errors.Add(new ValidationError { Path = $"{path}.trigger", Message = "Rule has an action but no trigger." });
                        // No trigger + no action = decorative band, allowed.
                    }
                    else if (isAudio && item.Trigger.IsVideoOnly)
                    {
                        errors.Add(new ValidationError
                        {
                            Path = $"{path}.trigger",
                            Message = $"Trigger \"{item.Trigger.Type}\" is video-only and cannot be used on an audio enhancement."
                        });
                    }
                    else if (item.Trigger is NeverFiringTrigger nft && nft.OriginalType != TriggerTypes.Never)
                    {
                        errors.Add(new ValidationError
                        {
                            Path = $"{path}.trigger",
                            Message = $"Unknown trigger type \"{nft.OriginalType}\" — rule will never fire.",
                            Severity = ValidationSeverity.Warning
                        });
                    }
                    else
                    {
                        ValidateTriggerSpecific(item.Trigger, $"{path}.trigger", bandIds, errors);
                    }

                    if (item.Action != null)
                    {
                        if (item.Enabled
                            && item.Trigger != null
                            && item.Action is NoOpEnhancementAction nopa
                            && nopa.OriginalType == ActionTypes.NoOp)
                        {
                            errors.Add(new ValidationError
                            {
                                Path = $"{path}.action",
                                Message = "Rule is enabled but has no action — it will fire silently. Pick an action or disable the rule.",
                                Severity = ValidationSeverity.Warning
                            });
                        }
                        ValidateActionSpecific(item.Action, $"{path}.action", bandIds, errors);
                    }
                }
            }
        }

        private static void ValidateEffectPayload(TimelineItem item, string path, List<ValidationError> errors)
        {
            if (string.IsNullOrEmpty(item.EffectType))
            {
                errors.Add(new ValidationError { Path = path, Message = "Effect items must set effect_type." });
                return;
            }

            if (item.EffectIntensity < 0 || item.EffectIntensity > 1)
                errors.Add(new ValidationError { Path = path, Message = $"effect_intensity must be in [0, 1] (got {item.EffectIntensity})." });
            if (item.EffectDurationMs <= 0)
                errors.Add(new ValidationError { Path = path, Message = $"effect_duration_ms must be > 0 (got {item.EffectDurationMs})." });

            switch (item.EffectType)
            {
                case EffectTypes.Haptic:
                    bool hasNamed = !string.IsNullOrEmpty(item.EffectPatternName);
                    bool hasCustom = item.EffectCustomPattern != null && item.EffectCustomPattern.Count > 0;
                    if (hasNamed && hasCustom)
                        errors.Add(new ValidationError { Path = path, Message = "Set exactly one of pattern_name or custom_pattern, not both." });
                    else if (!hasNamed && !hasCustom)
                        errors.Add(new ValidationError { Path = path, Message = "Haptic effect must set pattern_name or custom_pattern." });
                    if (hasCustom) ValidateCustomPattern(item.EffectCustomPattern!, path, errors);
                    break;

                case EffectTypes.Subliminal:
                    ValidateSubliminalText(item.EffectText, $"{path}.effect_text", errors);
                    break;

                case EffectTypes.Overlay:
                    if (item.EffectOverlayKind != OverlayKinds.PinkFilter
                        && item.EffectOverlayKind != OverlayKinds.Spiral
                        && item.EffectOverlayKind != OverlayKinds.BrainDrain)
                    {
                        errors.Add(new ValidationError { Path = path, Message = $"Unknown overlay kind \"{item.EffectOverlayKind}\"." });
                    }
                    if (item.EffectOpacity < 0 || item.EffectOpacity > 1)
                        errors.Add(new ValidationError { Path = path, Message = $"effect_opacity must be in [0, 1] (got {item.EffectOpacity})." });
                    break;

                case EffectTypes.Bubble:
                    if (item.EffectMaxBubbles < 1 || item.EffectMaxBubbles > 50)
                        errors.Add(new ValidationError { Path = path, Message = $"effect_max_bubbles must be in [1, 50] (got {item.EffectMaxBubbles})." });
                    break;

                case EffectTypes.Flash:
                    // image_path is optional (null = random); play_sound is bool.
                    break;

                default:
                    errors.Add(new ValidationError
                    {
                        Path = path,
                        Message = $"Unknown effect_type \"{item.EffectType}\" — effect will be skipped at runtime.",
                        Severity = ValidationSeverity.Warning
                    });
                    break;
            }
        }

        private static void ValidateAllDoublesFinite(Enhancement e, List<ValidationError> errors)
        {
            void Reject(string path, string field, double v)
            {
                if (!double.IsFinite(v))
                    errors.Add(new ValidationError
                    {
                        Path = path,
                        Message = $"{field} must be a finite number (got {v}).",
                        Severity = ValidationSeverity.Error
                    });
            }

            void RejectPattern(List<double[]>? pattern, string path)
            {
                if (pattern == null) return;
                for (int i = 0; i < pattern.Count; i++)
                {
                    var kf = pattern[i];
                    if (kf == null) continue;
                    for (int j = 0; j < kf.Length; j++)
                        Reject($"{path}.custom_pattern[{i}][{j}]", "value", kf[j]);
                }
            }

            for (int i = 0; i < e.TimelineItems.Count; i++)
            {
                var item = e.TimelineItems[i];
                if (item == null) continue;
                var path = $"timeline_items[{i}]";
                Reject(path, "start", item.Start);
                Reject(path, "duration", item.Duration);
                Reject(path, "effect_intensity", item.EffectIntensity);
                Reject(path, "effect_opacity", item.EffectOpacity);
                RejectPattern(item.EffectCustomPattern, path);
                RejectTriggerDoubles(item.Trigger, $"{path}.trigger", Reject);
                RejectActionDoubles(item.Action, $"{path}.action", Reject, RejectPattern);
            }

            for (int i = 0; i < e.Regions.Count; i++)
            {
                var r = e.Regions[i];
                var path = $"regions[{i}]";
                Reject(path, "start", r.Start);
                Reject(path, "end", r.End);
            }

            for (int i = 0; i < e.Rules.Count; i++)
            {
                var rule = e.Rules[i];
                var path = $"rules[{i}]";
                RejectTriggerDoubles(rule.Trigger, $"{path}.trigger", Reject);
                RejectActionDoubles(rule.Action, $"{path}.action", Reject, RejectPattern);
            }

            for (int t = 0; t < e.HapticTracks.Count; t++)
            {
                var track = e.HapticTracks[t];
                if (track?.Events == null) continue;
                for (int j = 0; j < track.Events.Count; j++)
                {
                    var ev = track.Events[j];
                    if (ev == null) continue;
                    var path = $"haptic_tracks[{t}].events[{j}]";
                    Reject(path, "start", ev.Start);
                    Reject(path, "duration", ev.Duration);
                    Reject(path, "intensity", ev.Intensity);
                    RejectPattern(ev.CustomPattern, path);
                }
            }
        }

        private static void RejectTriggerDoubles(EnhancementTrigger? t, string path, System.Action<string, string, double> reject)
        {
            switch (t)
            {
                case GazeTargetTrigger g when g.Rect != null:
                    for (int i = 0; i < g.Rect.Length; i++) reject($"{path}.rect[{i}]", "value", g.Rect[i]);
                    break;
                case GazeAvoidTrigger g when g.Rect != null:
                    for (int i = 0; i < g.Rect.Length; i++) reject($"{path}.rect[{i}]", "value", g.Rect[i]);
                    break;
                case TimeReachedTrigger tr:
                    reject(path, "time", tr.Time);
                    break;
            }
        }

        private static void RejectActionDoubles(
            EnhancementAction? a,
            string path,
            System.Action<string, string, double> reject,
            System.Action<List<double[]>?, string> rejectPattern)
        {
            switch (a)
            {
                case SeekAction s when s.Time.HasValue:
                    reject(path, "time", s.Time.Value);
                    break;
                case TriggerHapticAction h:
                    reject(path, "intensity", h.Intensity);
                    rejectPattern(h.CustomPattern, path);
                    break;
                case TriggerEffectAction te:
                    reject(path, "intensity", te.Intensity);
                    reject(path, "opacity", te.Opacity);
                    rejectPattern(te.CustomPattern, path);
                    break;
                case ScreenShakeAction ss:
                    reject(path, "intensity", ss.Intensity);
                    break;
                case SetIntensityAction si:
                    reject(path, "value", si.Value);
                    break;
            }
        }

        private static void ValidateMediaType(Enhancement e, List<ValidationError> errors)
        {
            if (e.MediaType != MediaTypes.Video && e.MediaType != MediaTypes.Audio)
            {
                errors.Add(new ValidationError
                {
                    Path = "media_type",
                    Message = $"Must be \"{MediaTypes.Video}\" or \"{MediaTypes.Audio}\" (got \"{e.MediaType}\")."
                });
            }

            if (string.IsNullOrWhiteSpace(e.MediaSource))
            {
                errors.Add(new ValidationError
                {
                    Path = "media_source",
                    Message = "Cannot be empty. Use \"*\" to match any media of this type."
                });
            }
            else if (IsUncOrExtendedPath(e.MediaSource))
            {
                // UNC paths in shared files would leak the user's NTLM hash on
                // first File.Exists / open. No legitimate authoring case needs
                // these, so reject hard.
                errors.Add(new ValidationError
                {
                    Path = "media_source",
                    Message = "UNC and extended-length paths are not allowed. Use a local drive path or a https URL.",
                    Severity = ValidationSeverity.Error
                });
            }

            // Walk effect items for image paths from shared files; same rule.
            for (int i = 0; i < e.TimelineItems.Count; i++)
            {
                var item = e.TimelineItems[i];
                if (item == null) continue;
                if (!string.IsNullOrEmpty(item.EffectImagePath) && IsUnsafeAssetPath(item.EffectImagePath))
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"timeline_items[{i}].effect_image_path",
                        Message = AssetPathRejectMessage,
                        Severity = ValidationSeverity.Error
                    });
                }
            }
        }

        private static bool IsUncOrExtendedPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.StartsWith("\\\\", System.StringComparison.Ordinal)) return true;
            if (path.StartsWith("//", System.StringComparison.Ordinal)) return true;
            return false;
        }

        // Asset paths in actions (play_audio.path, trigger_effect.image_path,
        // timeline_items[].effect_image_path) must resolve relative to the
        // user's assets folder. Absolute paths in shared files would let a
        // creator point at an arbitrary local file on the recipient's disk
        // (private recordings, system files); UNC paths additionally leak the
        // user's NTLM hash on first access. Reject both at validation.
        private static bool IsUnsafeAssetPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (IsUncOrExtendedPath(path)) return true;
            try { if (System.IO.Path.IsPathRooted(path)) return true; }
            catch { return true; }
            return false;
        }

        private const string AssetPathRejectMessage =
            "Path must be relative to the assets folder. Absolute and UNC paths are not allowed.";

        private static void ValidateRegions(Enhancement e, List<ValidationError> errors)
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < e.Regions.Count; i++)
            {
                var r = e.Regions[i];
                var path = $"regions[{i}]";

                if (string.IsNullOrWhiteSpace(r.Id))
                {
                    errors.Add(new ValidationError { Path = path, Message = "Region id cannot be empty." });
                }
                else if (!ids.Add(r.Id))
                {
                    errors.Add(new ValidationError { Path = path, Message = $"Duplicate region id \"{r.Id}\"." });
                }

                if (r.Start < 0)
                    errors.Add(new ValidationError { Path = path, Message = $"start must be >= 0 (got {r.Start})." });

                if (r.End <= r.Start)
                    errors.Add(new ValidationError { Path = path, Message = $"end ({r.End}) must be > start ({r.Start})." });
            }

            // Overlap check on the same timeline.
            var sorted = e.Regions
                .Select((r, i) => (r, i))
                .OrderBy(t => t.r.Start)
                .ToList();
            for (int j = 1; j < sorted.Count; j++)
            {
                var prev = sorted[j - 1].r;
                var curr = sorted[j].r;
                if (curr.Start < prev.End)
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"regions[{sorted[j].i}]",
                        Message = $"Region \"{curr.Id}\" overlaps region \"{prev.Id}\" ({curr.Start} < {prev.End})."
                    });
                }
            }
        }

        private static void ValidateHapticTracks(Enhancement e, List<ValidationError> errors)
        {
            var trackIds = new HashSet<string>();
            for (int t = 0; t < e.HapticTracks.Count; t++)
            {
                var track = e.HapticTracks[t];
                var trackPath = $"haptic_tracks[{t}]";

                if (string.IsNullOrWhiteSpace(track.Id))
                {
                    errors.Add(new ValidationError { Path = trackPath, Message = "Track id cannot be empty." });
                }
                else if (!trackIds.Add(track.Id))
                {
                    errors.Add(new ValidationError { Path = trackPath, Message = $"Duplicate track id \"{track.Id}\"." });
                }

                for (int i = 0; i < track.Events.Count; i++)
                {
                    var ev = track.Events[i];
                    var path = $"{trackPath}.events[{i}]";

                    if (ev.Start < 0)
                        errors.Add(new ValidationError { Path = path, Message = $"start must be >= 0 (got {ev.Start})." });

                    if (ev.Duration <= 0)
                        errors.Add(new ValidationError { Path = path, Message = $"duration must be > 0 (got {ev.Duration})." });

                    if (ev.Intensity < 0 || ev.Intensity > 1)
                        errors.Add(new ValidationError { Path = path, Message = $"intensity must be in [0, 1] (got {ev.Intensity})." });

                    bool hasNamed = !string.IsNullOrEmpty(ev.PatternName);
                    bool hasCustom = ev.CustomPattern != null && ev.CustomPattern.Count > 0;

                    if (hasNamed && hasCustom)
                        errors.Add(new ValidationError { Path = path, Message = "Set exactly one of pattern_name or custom_pattern, not both." });
                    else if (!hasNamed && !hasCustom)
                        errors.Add(new ValidationError { Path = path, Message = "Must set pattern_name or custom_pattern." });

                    if (hasCustom)
                        ValidateCustomPattern(ev.CustomPattern!, path, errors);
                }

                // Overlap check within the track.
                var sorted = track.Events
                    .Select((ev, i) => (ev, i))
                    .OrderBy(t => t.ev.Start)
                    .ToList();
                for (int j = 1; j < sorted.Count; j++)
                {
                    var prev = sorted[j - 1].ev;
                    var curr = sorted[j].ev;
                    if (curr.Start < prev.Start + prev.Duration)
                    {
                        errors.Add(new ValidationError
                        {
                            Path = $"{trackPath}.events[{sorted[j].i}]",
                            Message = $"Event at {curr.Start} overlaps previous event ending at {prev.Start + prev.Duration}."
                        });
                    }
                }
            }
        }

        private static void ValidateCustomPattern(List<double[]> pattern, string path, List<ValidationError> errors)
        {
            double prevT = -1;
            for (int k = 0; k < pattern.Count; k++)
            {
                var kf = pattern[k];
                var kfPath = $"{path}.custom_pattern[{k}]";

                if (kf == null || kf.Length != 2)
                {
                    errors.Add(new ValidationError { Path = kfPath, Message = "Each keyframe must be [t_frac, intensity]." });
                    continue;
                }

                double t = kf[0];
                double intensity = kf[1];

                if (t < 0 || t > 1)
                    errors.Add(new ValidationError { Path = kfPath, Message = $"t_frac must be in [0, 1] (got {t})." });

                if (t < prevT)
                    errors.Add(new ValidationError { Path = kfPath, Message = $"t_frac must be monotonically non-decreasing (got {t} after {prevT})." });

                if (intensity < 0 || intensity > 1)
                    errors.Add(new ValidationError { Path = kfPath, Message = $"intensity must be in [0, 1] (got {intensity})." });

                prevT = t;
            }
        }

        private static void ValidateRules(Enhancement e, List<ValidationError> errors, HashSet<EnhancementTrigger>? skipTrigger = null)
        {
            bool isAudio = e.MediaType == MediaTypes.Audio;
            // Resolve band ids from BOTH the legacy Regions list AND the
            // unified TimelineItems (Rule kind, finite duration). The engine
            // resolves either at runtime; without this, rules referencing a
            // band id during unsaved editor sessions (where TimelineItems
            // lead and Regions haven't been back-projected yet) get false
            // "unknown region" errors that the engine wouldn't actually hit.
            var regionIds = e.Regions.Select(r => r.Id).ToHashSet();
            foreach (var ti in e.TimelineItems)
            {
                if (ti?.Kind == TimelineItemKind.Rule
                    && ti.Duration > 0 && ti.Duration < double.MaxValue
                    && !string.IsNullOrEmpty(ti.Id))
                {
                    regionIds.Add(ti.Id);
                }
            }

            for (int i = 0; i < e.Rules.Count; i++)
            {
                var rule = e.Rules[i];
                if (skipTrigger != null && rule.Trigger != null && skipTrigger.Contains(rule.Trigger))
                    continue;
                var path = $"rules[{i}]";

                // The Newtonsoft converter returns null for explicit "trigger": null
                // even though the model default is non-null. Reject the file here
                // rather than NRE-ing further down and aborting the whole validation.
                if (rule.Trigger == null)
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"{path}.trigger",
                        Message = "Trigger cannot be null. Use \"type\": \"never\" if the rule should not fire.",
                        Severity = ValidationSeverity.Error
                    });
                    if (rule.Action != null)
                        ValidateActionSpecific(rule.Action, $"{path}.action", regionIds, errors);
                    continue;
                }

                if (rule.Trigger is NeverFiringTrigger nft && nft.OriginalType != TriggerTypes.Never)
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"{path}.trigger",
                        Message = $"Unknown trigger type \"{nft.OriginalType}\" — rule will never fire.",
                        Severity = ValidationSeverity.Warning
                    });
                }
                else if (isAudio && rule.Trigger.IsVideoOnly)
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"{path}.trigger",
                        Message = $"Trigger \"{rule.Trigger.Type}\" is video-only and cannot be used on an audio enhancement."
                    });
                }

                ValidateTriggerSpecific(rule.Trigger, $"{path}.trigger", regionIds, errors);

                if (rule.Enabled
                    && rule.Action is NoOpEnhancementAction nopa
                    && nopa.OriginalType == ActionTypes.NoOp)
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"{path}.action",
                        Message = "Rule is enabled but has no action — it will fire silently. Pick an action or disable the rule.",
                        Severity = ValidationSeverity.Warning
                    });
                }

                ValidateActionSpecific(rule.Action, $"{path}.action", regionIds, errors);

                if (!string.IsNullOrEmpty(rule.RegionConstraint) && !regionIds.Contains(rule.RegionConstraint))
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"{path}.region_constraint",
                        Message = $"Referenced region \"{rule.RegionConstraint}\" does not exist."
                    });
                }

                if (rule.CooldownMs < 0)
                {
                    errors.Add(new ValidationError
                    {
                        Path = $"{path}.cooldown_ms",
                        Message = $"cooldown_ms must be >= 0 (got {rule.CooldownMs})."
                    });
                }
            }
        }

        private static void ValidateTriggerSpecific(EnhancementTrigger t, string path, HashSet<string> regionIds, List<ValidationError> errors)
        {
            switch (t)
            {
                case GazeTargetTrigger g:
                    ValidateRect(g.Rect, path, errors);
                    if (g.MinDwellMs < 0) errors.Add(new ValidationError { Path = path, Message = $"min_dwell_ms must be >= 0 (got {g.MinDwellMs})." });
                    break;
                case GazeAvoidTrigger g:
                    ValidateRect(g.Rect, path, errors);
                    if (g.MinDwellMs < 0) errors.Add(new ValidationError { Path = path, Message = $"min_dwell_ms must be >= 0 (got {g.MinDwellMs})." });
                    break;
                case AttentionLostTrigger a:
                    if (a.MinDurationMs < 0) errors.Add(new ValidationError { Path = path, Message = $"min_duration_ms must be >= 0 (got {a.MinDurationMs})." });
                    break;
                case TimeReachedTrigger tr:
                    if (tr.Time < 0) errors.Add(new ValidationError { Path = path, Message = $"time must be >= 0 (got {tr.Time})." });
                    break;
                case RegionEnteredTrigger re:
                    if (string.IsNullOrEmpty(re.RegionId) || !regionIds.Contains(re.RegionId))
                        errors.Add(new ValidationError { Path = path, Message = $"Referenced region \"{re.RegionId}\" does not exist." });
                    break;
                case RegionExitedTrigger rx:
                    if (string.IsNullOrEmpty(rx.RegionId) || !regionIds.Contains(rx.RegionId))
                        errors.Add(new ValidationError { Path = path, Message = $"Referenced region \"{rx.RegionId}\" does not exist." });
                    break;
            }
        }

        private static void ValidateActionSpecific(EnhancementAction a, string path, HashSet<string> regionIds, List<ValidationError> errors)
        {
            switch (a)
            {
                case NoOpEnhancementAction noop when noop.OriginalType != ActionTypes.NoOp:
                    errors.Add(new ValidationError
                    {
                        Path = path,
                        Message = $"Unknown action type \"{noop.OriginalType}\" — action will be skipped at runtime.",
                        Severity = ValidationSeverity.Warning
                    });
                    break;

                case SeekAction seek:
                    if (seek.Target == SeekTargets.Time)
                    {
                        if (seek.Time == null) errors.Add(new ValidationError { Path = path, Message = "seek with target=\"time\" requires a time field." });
                        else if (seek.Time < 0) errors.Add(new ValidationError { Path = path, Message = $"time must be >= 0 (got {seek.Time})." });
                    }
                    else if (seek.Target == SeekTargets.RegionStart || seek.Target == SeekTargets.RegionEnd)
                    {
                        if (string.IsNullOrEmpty(seek.RegionId) || !regionIds.Contains(seek.RegionId))
                            errors.Add(new ValidationError { Path = path, Message = $"seek references unknown region \"{seek.RegionId}\"." });
                    }
                    else
                    {
                        errors.Add(new ValidationError { Path = path, Message = $"seek target must be \"time\", \"region_start\", or \"region_end\" (got \"{seek.Target}\")." });
                    }
                    break;

                case LoopRegionAction loop:
                    if (!string.IsNullOrEmpty(loop.RegionId) && !regionIds.Contains(loop.RegionId))
                        errors.Add(new ValidationError { Path = path, Message = $"loop_region references unknown region \"{loop.RegionId}\"." });
                    break;

                case PlayAudioAction pa:
                    if (string.IsNullOrEmpty(pa.Path))
                        errors.Add(new ValidationError { Path = path, Message = "play_audio requires a path." });
                    else if (IsUnsafeAssetPath(pa.Path))
                        errors.Add(new ValidationError { Path = $"{path}.path", Message = AssetPathRejectMessage, Severity = ValidationSeverity.Error });
                    if (pa.Volume < 0 || pa.Volume > 100)
                        errors.Add(new ValidationError { Path = path, Message = $"volume must be in [0, 100] (got {pa.Volume})." });
                    break;

                case TriggerHapticAction h:
                    bool hasNamed = !string.IsNullOrEmpty(h.PatternName);
                    bool hasCustom = h.CustomPattern != null && h.CustomPattern.Count > 0;
                    if (hasNamed && hasCustom)
                        errors.Add(new ValidationError { Path = path, Message = "Set exactly one of pattern_name or custom_pattern, not both." });
                    else if (!hasNamed && !hasCustom)
                        errors.Add(new ValidationError { Path = path, Message = "Must set pattern_name or custom_pattern." });
                    if (hasCustom)
                        ValidateCustomPattern(h.CustomPattern!, path, errors);
                    if (h.Intensity < 0 || h.Intensity > 1)
                        errors.Add(new ValidationError { Path = path, Message = $"intensity must be in [0, 1] (got {h.Intensity})." });
                    if (h.DurationMs <= 0)
                        errors.Add(new ValidationError { Path = path, Message = $"duration_ms must be > 0 (got {h.DurationMs})." });
                    break;

                case TriggerEffectAction te:
                    if (string.IsNullOrEmpty(te.EffectType))
                        errors.Add(new ValidationError { Path = path, Message = "trigger_effect requires effect_type." });
                    if (te.Intensity < 0 || te.Intensity > 1)
                        errors.Add(new ValidationError { Path = path, Message = $"intensity must be in [0, 1] (got {te.Intensity})." });
                    if (te.DurationMs <= 0)
                        errors.Add(new ValidationError { Path = path, Message = $"duration_ms must be > 0 (got {te.DurationMs})." });
                    if (te.EffectType == EffectTypes.Haptic)
                    {
                        bool hn = !string.IsNullOrEmpty(te.PatternName);
                        bool hc = te.CustomPattern != null && te.CustomPattern.Count > 0;
                        if (hn && hc)
                            errors.Add(new ValidationError { Path = path, Message = "Set exactly one of pattern_name or custom_pattern, not both." });
                        else if (!hn && !hc)
                            errors.Add(new ValidationError { Path = path, Message = "Haptic trigger_effect must set pattern_name or custom_pattern." });
                        if (hc) ValidateCustomPattern(te.CustomPattern!, path, errors);
                    }
                    else if (te.EffectType == EffectTypes.Flash)
                    {
                        if (!string.IsNullOrEmpty(te.ImagePath) && IsUnsafeAssetPath(te.ImagePath))
                            errors.Add(new ValidationError { Path = $"{path}.image_path", Message = AssetPathRejectMessage, Severity = ValidationSeverity.Error });
                    }
                    else if (te.EffectType == EffectTypes.Subliminal)
                    {
                        ValidateSubliminalText(te.Text, $"{path}.text", errors);
                    }
                    else if (te.EffectType == EffectTypes.Overlay)
                    {
                        if (te.OverlayKind != OverlayKinds.PinkFilter
                            && te.OverlayKind != OverlayKinds.Spiral
                            && te.OverlayKind != OverlayKinds.BrainDrain)
                        {
                            errors.Add(new ValidationError { Path = path, Message = $"Unknown overlay kind \"{te.OverlayKind}\"." });
                        }
                        if (te.Opacity < 0 || te.Opacity > 1)
                            errors.Add(new ValidationError { Path = path, Message = $"opacity must be in [0, 1] (got {te.Opacity})." });
                    }
                    else if (te.EffectType == EffectTypes.Bubble)
                    {
                        if (te.MaxBubbles < 1 || te.MaxBubbles > 50)
                            errors.Add(new ValidationError { Path = path, Message = $"max_bubbles must be in [1, 50] (got {te.MaxBubbles})." });
                    }
                    break;

                case ScreenShakeAction ss:
                    if (ss.Intensity < 0 || ss.Intensity > 1)
                        errors.Add(new ValidationError { Path = path, Message = $"intensity must be in [0, 1] (got {ss.Intensity})." });
                    if (ss.DurationMs <= 0)
                        errors.Add(new ValidationError { Path = path, Message = $"duration_ms must be > 0 (got {ss.DurationMs})." });
                    break;

                case SetIntensityAction si:
                    if (si.Value < 0 || si.Value > 1)
                        errors.Add(new ValidationError { Path = path, Message = $"value must be in [0, 1] (got {si.Value})." });
                    break;
            }
        }

        // Subliminal text appears flashed onscreen and is sourced from shared
        // .ccpenh.json files. Cap length so an oversized payload can't blow up
        // a flash render, and reject bidi-override / control codepoints that
        // would let a malicious file mask its actual rendered content.
        private const int MaxSubliminalTextLength = 256;
        private static void ValidateSubliminalText(string? text, string path, List<ValidationError> errors)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                errors.Add(new ValidationError { Path = path, Message = "Subliminal text cannot be empty." });
                return;
            }
            if (text.Length > MaxSubliminalTextLength)
            {
                errors.Add(new ValidationError
                {
                    Path = path,
                    Message = $"Subliminal text is too long ({text.Length} chars, max {MaxSubliminalTextLength}).",
                    Severity = ValidationSeverity.Error
                });
            }
            foreach (char c in text)
            {
                // Control chars (excluding tab, LF, CR which are harmless here).
                if ((c < 0x20 && c != '\t' && c != '\n' && c != '\r') || (c >= 0x7F && c < 0xA0))
                {
                    errors.Add(new ValidationError { Path = path, Message = "Subliminal text contains a control character.", Severity = ValidationSeverity.Error });
                    return;
                }
                // Bidi override / embedding controls. These can re-order the
                // displayed text against what's stored, masking actual content.
                if (c >= 0x202A && c <= 0x202E) goto bidi;
                if (c >= 0x2066 && c <= 0x2069) goto bidi;
            }
            return;
        bidi:
            errors.Add(new ValidationError { Path = path, Message = "Subliminal text contains bidirectional override characters.", Severity = ValidationSeverity.Error });
        }

        private static void ValidateRect(double[] rect, string path, List<ValidationError> errors)
        {
            if (rect == null || rect.Length != 4)
            {
                errors.Add(new ValidationError { Path = path, Message = "rect must be [x, y, width, height]." });
                return;
            }
            for (int i = 0; i < 4; i++)
            {
                if (rect[i] < 0 || rect[i] > 1)
                {
                    errors.Add(new ValidationError
                    {
                        Path = path,
                        Message = $"rect[{i}] must be in [0, 1] (got {rect[i]})."
                    });
                }
            }
            if (rect.Length == 4 && rect[2] <= 0)
                errors.Add(new ValidationError { Path = path, Message = "rect width must be > 0." });
            if (rect.Length == 4 && rect[3] <= 0)
                errors.Add(new ValidationError { Path = path, Message = "rect height must be > 0." });
        }
    }
}
