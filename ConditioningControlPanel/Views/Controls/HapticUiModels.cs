using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Views.Controls
{
    // ============================================================================
    // View models for the Phase E Haptics tab.
    //
    // The tab is data-driven now: the old 9 copy-pasted feature rows (and their 9
    // near-identical event handlers) are one ItemsControl over
    // HapticRoutingGroupVm -> HapticRoutingRowVm, and the toy cards are an
    // ItemsControl over HapticToyCardVm fed by HapticDeviceManager.
    //
    // Each VM writes STRAIGHT THROUGH to the settings object that the engine
    // actually reads, then calls App.Settings.Save() (SettingsService debounces
    // 500 ms internally, so a slider drag is still one disk write).
    //
    // WHICH property is authoritative differs per row and is NOT guessable — see
    // Models/HapticSettings.cs:
    //   * Event rows      -> HapticSettingsV2.Rule(HapticEventKind) is the truth;
    //                        HapticService.PostEvent reads it directly.
    //   * Video layer     -> the LEVEL still comes from the legacy
    //                        HapticSettings.VideoIntensity (HapticService
    //                        .StartVideoBackgroundVibeAsync multiplies it by 0.1),
    //                        while the layer rule only gates/scales it. So this row
    //                        writes the legacy property, which OnPropertyChanged
    //                        mirrors into the layer rule for us.
    //   * AudioSync layer -> HapticService.SetSyncIntensityAsync early-outs on
    //                        Settings.AudioSync.Enabled, and the mixer additionally
    //                        honours the layer rule, so BOTH have to be written.
    // ============================================================================

    public abstract class HapticVmBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected static void SaveSettings()
        {
            try { App.Settings?.Save(); } catch { }
        }
    }

    /// <summary>Which legacy property (if any) is the real source of truth for a row.</summary>
    public enum HapticRowLegacyBinding
    {
        None,
        /// <summary>Video background vibe: level lives on HapticSettings.VideoIntensity.</summary>
        VideoLevel,
        /// <summary>Audio sync: HapticService gates on HapticSettings.AudioSync.Enabled.</summary>
        AudioSync,
        /// <summary>
        /// DtRH accents: the v2 rule IS the truth (DtrhHapticDirector reads it live), but the
        /// legacy DtrhEnabled / DtrhIntensity pair is still mirrored so a downgrade to v6.6.3 —
        /// where those two properties were the only control — keeps the user's intent. Writing
        /// DtrhEnabled additionally carries the toggle to the Dtrh LAYER rule (the ambient floor)
        /// via HapticSettings.SyncLegacyToRouting.
        /// </summary>
        Dtrh
    }

    /// <summary>
    /// Keeps at most ONE routing row open at a time. Twenty rows that can all be open at
    /// once is the wall of sliders again with extra steps; one open row is a detail pane.
    /// </summary>
    public sealed class HapticRowExpansionScope
    {
        private HapticRoutingRowVm? _current;

        internal void NotifyExpanded(HapticRoutingRowVm row)
        {
            if (ReferenceEquals(_current, row)) return;
            var previous = _current;
            _current = row;
            previous?.CollapseSilently();
        }

        internal void NotifyCollapsed(HapticRoutingRowVm row)
        {
            if (ReferenceEquals(_current, row)) _current = null;
        }
    }

    /// <summary>One row of the routing matrix: enable, intensity, pattern, target role.</summary>
    public sealed class HapticRoutingRowVm : HapticVmBase
    {
        private readonly HapticSettings _settings;
        private readonly HapticEventKind? _kind;
        private readonly HapticLayer? _layer;
        private readonly HapticRowLegacyBinding _legacy;
        private bool _isExpanded;

        /// <summary>Loc keys for the six <see cref="VibrationMode"/>s, in enum order.</summary>
        private static readonly string[] ModeKeys =
        {
            "btn_constant", "btn_pulse", "btn_wave", "btn_heartbeat", "btn_escalate", "btn_earthquake"
        };

        /// <summary>Loc keys for the four <see cref="ToyRole"/>s, in enum order.</summary>
        private static readonly string[] RoleKeys =
        {
            "haptics_role_all", "haptics_role_reward", "haptics_role_punish", "haptics_role_ambient"
        };

        /// <summary>Raised after any write, so the tab can refresh dependent chrome
        /// (e.g. showing the audio-sync tuning card when that row is switched on).</summary>
        public event EventHandler? Changed;

        private HapticRoutingRowVm(HapticSettings settings, string icon, string label, string hint,
                                   HapticEventKind? kind, HapticLayer? layer,
                                   HapticRowLegacyBinding legacy)
        {
            _settings = settings;
            _kind = kind;
            _layer = layer;
            _legacy = legacy;
            Icon = icon;
            Label = label;
            Hint = hint;
        }

        /// <summary>
        /// Event row. <paramref name="legacy"/> is only needed for the handful of kinds that still
        /// have a legacy twin somebody reads (DtRH); everything else routes purely through the v2
        /// rule, which is what HapticService.PostEvent consults.
        /// </summary>
        public static HapticRoutingRowVm ForEvent(HapticSettings settings, HapticEventKind kind,
                                                  string icon, string labelKey, string hintKey,
                                                  HapticRowLegacyBinding legacy = HapticRowLegacyBinding.None)
            => new(settings, icon, Loc.Get(labelKey), Loc.Get(hintKey), kind, null, legacy);

        public static HapticRoutingRowVm ForLayer(HapticSettings settings, HapticLayer layer,
                                                  string icon, string labelKey, string hintKey,
                                                  HapticRowLegacyBinding legacy)
            => new(settings, icon, Loc.Get(labelKey), Loc.Get(hintKey), null, layer, legacy);

        public string Icon { get; }
        public string Label { get; }
        public string Hint { get; }

        /// <summary>Set once at build time so the rows share a single "one open at a time" rule.</summary>
        public HapticRowExpansionScope? Scope { get; set; }

        /// <summary>
        /// Design pass: at rest a row is a toggle, a name and a value pill. Its slider,
        /// pattern and target only exist while it is the open row.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                if (value) Scope?.NotifyExpanded(this);
                else Scope?.NotifyCollapsed(this);
                Raise();
            }
        }

        /// <summary>Closed by the scope because another row opened — no scope callback back.</summary>
        internal void CollapseSilently()
        {
            if (!_isExpanded) return;
            _isExpanded = false;
            Raise(nameof(IsExpanded));
        }

        /// <summary>Continuous layers have no pattern — a "vibration mode" only means
        /// something for a transient envelope. The combo is collapsed for them.</summary>
        public bool ShowMode => _kind.HasValue;
        public Visibility ModeVisibility => ShowMode ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// The whole row in one pill: "50% · Pulse · All", or "Off". This is what makes a
        /// collapsed row readable — you never have to open one just to see what it does.
        /// </summary>
        public string ValueSummary
        {
            get
            {
                if (!RowEnabled) return Loc.Get("haptics_row_off");
                var parts = new List<string>(3) { IntensityText };
                if (ShowMode) parts.Add(Loc.Get(ModeKeys[Math.Clamp(ModeIndex, 0, ModeKeys.Length - 1)]));
                parts.Add(Loc.Get(RoleKeys[Math.Clamp(RoleIndex, 0, RoleKeys.Length - 1)]));
                return string.Join(" · ", parts);
            }
        }

        private HapticEventRule? EventRule => _kind.HasValue ? _settings.V2.Rule(_kind.Value) : null;
        private HapticLayerRule? LayerRule => _layer.HasValue ? _settings.V2.Rule(_layer.Value) : null;

        public bool RowEnabled
        {
            get => _legacy switch
            {
                HapticRowLegacyBinding.VideoLevel => _settings.VideoEnabled,
                HapticRowLegacyBinding.AudioSync => _settings.AudioSync.Enabled,
                _ => EventRule?.Enabled ?? LayerRule?.Enabled ?? false
            };
            set
            {
                if (RowEnabled == value) return;
                switch (_legacy)
                {
                    case HapticRowLegacyBinding.VideoLevel:
                        // Mirrors itself into the layer rule via HapticSettings.OnPropertyChanged.
                        _settings.VideoEnabled = value;
                        break;
                    case HapticRowLegacyBinding.AudioSync:
                        _settings.AudioSync.Enabled = value;
                        if (LayerRule != null) LayerRule.Enabled = value;
                        break;
                    case HapticRowLegacyBinding.Dtrh:
                        if (EventRule != null) EventRule.Enabled = value;
                        // Mirrors into the Dtrh LAYER rule (ambient floor) AND tells the director,
                        // which listens for DtrhEnabled to kill anything already playing.
                        _settings.DtrhEnabled = value;
                        break;
                    default:
                        if (EventRule != null) EventRule.Enabled = value;
                        if (LayerRule != null) LayerRule.Enabled = value;
                        break;
                }
                SaveSettings();
                NotifyEngine();
                Raise();
                Raise(nameof(ValueSummary));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>0-100 for the slider.</summary>
        public double IntensityPercent
        {
            get => Math.Round(RawIntensity * 100);
            set
            {
                var clamped = Math.Clamp(value, 0, 100) / 100.0;
                if (Math.Abs(clamped - RawIntensity) < 0.0005) return;
                switch (_legacy)
                {
                    case HapticRowLegacyBinding.VideoLevel:
                        _settings.VideoIntensity = clamped;
                        break;
                    case HapticRowLegacyBinding.Dtrh:
                        if (EventRule != null) EventRule.Intensity = clamped;
                        _settings.DtrhIntensity = clamped;   // legacy twin, for a downgrade
                        break;
                    default:
                        if (EventRule != null) EventRule.Intensity = clamped;
                        else if (LayerRule != null) LayerRule.Intensity = clamped;
                        break;
                }
                SaveSettings();
                NotifyEngine();
                Raise();
                Raise(nameof(IntensityText));
                Raise(nameof(ValueSummary));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private double RawIntensity => _legacy switch
        {
            HapticRowLegacyBinding.VideoLevel => _settings.VideoIntensity,
            _ => EventRule?.Intensity ?? LayerRule?.Intensity ?? 0
        };

        public string IntensityText => ((int)Math.Round(IntensityPercent)).ToString(CultureInfo.InvariantCulture) + "%";

        /// <summary>Index into the six <see cref="VibrationMode"/>s (combo order matches the enum).</summary>
        public int ModeIndex
        {
            get => EventRule == null ? 0 : (int)EventRule.Mode;
            set
            {
                if (EventRule == null || value < 0) return;
                var mode = (VibrationMode)Math.Clamp(value, 0, 5);
                if (EventRule.Mode == mode) return;
                EventRule.Mode = mode;
                SaveSettings();
                NotifyEngine();
                Raise();
                Raise(nameof(ValueSummary));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Index into <see cref="ToyRole"/> (All / Reward / Punish / Ambient).</summary>
        public int RoleIndex
        {
            get => (int)(EventRule?.Target ?? LayerRule?.Target ?? ToyRole.All);
            set
            {
                if (value < 0) return;
                var role = (ToyRole)Math.Clamp(value, 0, 3);
                if (RoleIndex == (int)role) return;
                if (EventRule != null) EventRule.Target = role;
                if (LayerRule != null) LayerRule.Target = role;
                SaveSettings();
                Raise();
                Raise(nameof(ValueSummary));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// The v2 rules are plain POCOs with no change notification, so writing one is invisible to
        /// the engine: a pattern already in flight would keep playing after its row was switched
        /// off. HapticService.NotifyRuleChanged is the engine's re-read hook (it cancels in-flight
        /// patterns of that kind when the rule now forbids them). Layer rows have no event kind and
        /// are already level-set, so they have nothing to cancel.
        /// </summary>
        private void NotifyEngine()
        {
            if (!_kind.HasValue) return;
            try { App.Haptics?.NotifyRuleChanged(_kind.Value); } catch { }
        }

        /// <summary>Re-read everything from settings (after a load or an external change).</summary>
        public void Refresh()
        {
            Raise(nameof(RowEnabled));
            Raise(nameof(IntensityPercent));
            Raise(nameof(IntensityText));
            Raise(nameof(ModeIndex));
            Raise(nameof(RoleIndex));
            Raise(nameof(ValueSummary));
        }
    }

    /// <summary>A titled band of routing rows (Core / Rewards / Media / Games).</summary>
    public sealed class HapticRoutingGroupVm
    {
        public HapticRoutingGroupVm(string icon, string title, IEnumerable<HapticRoutingRowVm> rows)
        {
            Icon = icon;
            Title = title;
            Rows = new ObservableCollection<HapticRoutingRowVm>(rows);
        }

        public string Icon { get; }
        public string Title { get; }
        public ObservableCollection<HapticRoutingRowVm> Rows { get; }
    }

    /// <summary>One connected toy: identity, battery, capability chips and per-toy config.</summary>
    public sealed class HapticToyCardVm : HapticVmBase
    {
        private readonly HapticDeviceManager _manager;
        private string _nickname;
        private double _trimPercent;
        private bool _toyEnabled;
        private int _roleIndex;

        public HapticToyCardVm(HapticDeviceManager manager, HapticDevice device)
        {
            _manager = manager;
            DeviceKey = device.DeviceKey;
            Name = string.IsNullOrWhiteSpace(device.Name) ? DeviceKey : device.Name;
            ProviderLabel = ProviderDisplay(device.ProviderKey);
            _nickname = device.Nickname ?? "";
            _trimPercent = Math.Round(Math.Clamp(device.IntensityTrim, 0, 1) * 100);
            _toyEnabled = device.Enabled;
            _roleIndex = (int)device.Role;
            BatteryPercent = device.BatteryPercent;
            Capabilities = new ObservableCollection<string>(DescribeActuators(device));
        }

        public string DeviceKey { get; }
        public string Name { get; }
        public string ProviderLabel { get; }
        public int? BatteryPercent { get; }

        public string BatteryText => BatteryPercent.HasValue
            ? BatteryPercent.Value.ToString(CultureInfo.InvariantCulture) + "%"
            : "";

        /// <summary>Providers that never report a battery must not show an empty pill.</summary>
        public Visibility BatteryVisibility => BatteryPercent.HasValue ? Visibility.Visible : Visibility.Collapsed;

        public ObservableCollection<string> Capabilities { get; }

        public string Nickname
        {
            get => _nickname;
            set
            {
                var v = value ?? "";
                if (_nickname == v) return;
                _nickname = v;
                _manager.SetNickname(DeviceKey, v);   // persists + saves + rebuilds
                Raise();
            }
        }

        public int RoleIndex
        {
            get => _roleIndex;
            set
            {
                var v = Math.Clamp(value, 0, 3);
                if (_roleIndex == v || value < 0) return;
                _roleIndex = v;
                _manager.SetRole(DeviceKey, (ToyRole)v);
                Raise();
            }
        }

        public double TrimPercent
        {
            get => _trimPercent;
            set
            {
                var v = Math.Clamp(value, 0, 100);
                if (Math.Abs(_trimPercent - v) < 0.5) return;
                _trimPercent = v;
                _manager.SetTrim(DeviceKey, v / 100.0);
                Raise();
                Raise(nameof(TrimText));
            }
        }

        public string TrimText => ((int)Math.Round(_trimPercent)).ToString(CultureInfo.InvariantCulture) + "%";

        public bool ToyEnabled
        {
            get => _toyEnabled;
            set
            {
                if (_toyEnabled == value) return;
                _toyEnabled = value;
                _manager.SetEnabled(DeviceKey, value);
                Raise();
            }
        }

        private static string ProviderDisplay(string key) => key?.ToLowerInvariant() switch
        {
            "lovense" => "Lovense",
            "buttplug" => "Intiface",
            "mock" => Loc.Get("haptics_provider_mock"),
            _ => key ?? ""
        };

        /// <summary>Capability chips: one per actuator TYPE, with a x2/x3 multiplier when a toy
        /// has several motors of the same kind (Edge = 2 vibes, Lapis = 3).</summary>
        private static IEnumerable<string> DescribeActuators(HapticDevice device)
        {
            var counts = new Dictionary<ActuatorType, int>();
            foreach (var a in device.Actuators)
            {
                // Stroke is the range partner of Thrust, not a chip of its own.
                if (a.Type == ActuatorType.Stroke) continue;
                counts[a.Type] = counts.TryGetValue(a.Type, out var n) ? n + 1 : 1;
            }

            foreach (var kv in counts)
            {
                var label = kv.Key switch
                {
                    ActuatorType.Vibrate => "VIBE",
                    ActuatorType.Rotate => "ROTATE",
                    ActuatorType.Thrust => "THRUST",
                    ActuatorType.Finger => "FINGER",
                    ActuatorType.Suction => "SUCTION",
                    ActuatorType.Oscillate => "OSCILLATE",
                    ActuatorType.Pump => "PUMP",
                    ActuatorType.Depth => "DEPTH",
                    ActuatorType.Position => "POSITION",
                    ActuatorType.Constrict => "CONSTRICT",
                    _ => kv.Key.ToString().ToUpperInvariant()
                };
                yield return kv.Value > 1 ? label + " x" + kv.Value : label;
            }
        }
    }

    /// <summary>Status chip for one provider in the top strip.</summary>
    public sealed class HapticProviderChipVm : HapticVmBase
    {
        private bool _connected;
        private bool _enabled;

        public HapticProviderChipVm(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }
        public string Label { get; }

        public bool IsConnected
        {
            get => _connected;
            set { if (_connected == value) return; _connected = value; Raise(); }
        }

        public bool IsEnabledForConnect
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; Raise(); }
        }
    }

    // ------------------------------------------------------------------------
    // Converters. They live here (and are instantiated in the UserControl's own
    // Resources root) because a converter declared inside a nested Grid.Resources
    // is invisible to a DataTemplate — a trap this codebase has hit before.
    // ------------------------------------------------------------------------

    /// <summary>true -> the connected colour, false -> the muted colour. Parameter-free so it can
    /// be reused for dots and chips alike.</summary>
    public sealed class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var on = value is bool b && b;
            var key = on ? "SuccessGreenBrush" : "TextDimBrush";
            try
            {
                if (Application.Current?.Resources[key] is System.Windows.Media.Brush brush) return brush;
            }
            catch { }
            return on ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>Empty / whitespace string -> Collapsed.</summary>
    public sealed class EmptyStringToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
