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

            ValidateMediaType(e, errors);
            ValidateRegions(e, errors);
            ValidateHapticTracks(e, errors);
            ValidateRules(e, errors);

            return errors;
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
        }

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

        private static void ValidateRules(Enhancement e, List<ValidationError> errors)
        {
            bool isAudio = e.MediaType == MediaTypes.Audio;
            var regionIds = e.Regions.Select(r => r.Id).ToHashSet();

            for (int i = 0; i < e.Rules.Count; i++)
            {
                var rule = e.Rules[i];
                var path = $"rules[{i}]";

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
