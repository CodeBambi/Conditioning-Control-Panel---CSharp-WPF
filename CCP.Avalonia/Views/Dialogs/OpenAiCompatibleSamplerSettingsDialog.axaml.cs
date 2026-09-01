using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Modal editor for the OpenAI-compatible provider's sampler parameters.
    /// Only populated values are persisted; empty fields become null and are
    /// omitted from the outbound request so strict OpenAI endpoints don't see
    /// unsupported keys like top_k/repetition_penalty/min_p.
    ///
    /// Ported from the WPF head. WPF's <c>DialogResult</c> has no Avalonia equivalent, so the
    /// dialog closes with <c>Close(bool)</c> and the caller awaits <c>ShowDialog&lt;bool&gt;</c>;
    /// dismissing the window with the X yields <c>default(bool)</c> = false, which is what the
    /// WPF caller's <c>ShowDialog() == true</c> did too.
    /// </summary>
    public partial class OpenAiCompatibleSamplerSettingsDialog : Window
    {
        private readonly CompanionPromptSettings _settings;

        private readonly CheckBox _chkUseCustomSamplers;
        private readonly Grid _inputsPanel;
        private readonly TextBox _txtTemperature;
        private readonly TextBox _txtTopP;
        private readonly TextBox _txtTopK;
        private readonly TextBox _txtMinP;
        private readonly TextBox _txtFrequencyPenalty;
        private readonly TextBox _txtPresencePenalty;
        private readonly TextBox _txtRepetitionPenalty;
        private readonly TextBlock _txtError;

        /// <summary>Render/design constructor: default settings so --render-view can draw the dialog.</summary>
        public OpenAiCompatibleSamplerSettingsDialog() : this(new CompanionPromptSettings()) { }

        public OpenAiCompatibleSamplerSettingsDialog(CompanionPromptSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            AvaloniaXamlLoader.Load(this);
            DataContext = new OpenAiCompatibleSamplerSettingsViewModel();

            _chkUseCustomSamplers = this.FindControl<CheckBox>("ChkUseCustomSamplers")!;
            _inputsPanel = this.FindControl<Grid>("InputsPanel")!;
            _txtTemperature = this.FindControl<TextBox>("TxtTemperature")!;
            _txtTopP = this.FindControl<TextBox>("TxtTopP")!;
            _txtTopK = this.FindControl<TextBox>("TxtTopK")!;
            _txtMinP = this.FindControl<TextBox>("TxtMinP")!;
            _txtFrequencyPenalty = this.FindControl<TextBox>("TxtFrequencyPenalty")!;
            _txtPresencePenalty = this.FindControl<TextBox>("TxtPresencePenalty")!;
            _txtRepetitionPenalty = this.FindControl<TextBox>("TxtRepetitionPenalty")!;
            _txtError = this.FindControl<TextBlock>("TxtError")!;

            // WPF wired Checked and Unchecked to one handler; Avalonia raises both as
            // IsCheckedChanged.
            _chkUseCustomSamplers.IsCheckedChanged += (_, _) => UpdateInputState();
            this.FindControl<Button>("BtnReset")!.Click += (_, _) => Reset();
            // Avalonia's IsCancel only raises Click - unlike WPF it does not set a result - so
            // the false is explicit here, as it was in BtnCancel_Click.
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnOk")!.Click += (_, _) => Accept();

            Loaded += (_, _) => LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _chkUseCustomSamplers.IsChecked = _settings.OpenAiCompatibleUseCustomSamplerSettings;
            UpdateInputState();

            _txtTemperature.Text = FormatNullable(_settings.OpenAiCompatibleTemperature);
            _txtTopP.Text = FormatNullable(_settings.OpenAiCompatibleTopP);
            _txtTopK.Text = FormatNullable(_settings.OpenAiCompatibleTopK);
            _txtFrequencyPenalty.Text = FormatNullable(_settings.OpenAiCompatibleFrequencyPenalty);
            _txtPresencePenalty.Text = FormatNullable(_settings.OpenAiCompatiblePresencePenalty);
            _txtRepetitionPenalty.Text = FormatNullable(_settings.OpenAiCompatibleRepetitionPenalty);
            _txtMinP.Text = FormatNullable(_settings.OpenAiCompatibleMinP);
        }

        private static string FormatNullable(double? value)
            => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

        private static string FormatNullable(int? value)
            => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

        private void UpdateInputState()
        {
            _inputsPanel.IsEnabled = _chkUseCustomSamplers.IsChecked == true;
        }

        private void Reset()
        {
            _chkUseCustomSamplers.IsChecked = false;
            _txtTemperature.Text = string.Empty;
            _txtTopP.Text = string.Empty;
            _txtTopK.Text = string.Empty;
            _txtFrequencyPenalty.Text = string.Empty;
            _txtPresencePenalty.Text = string.Empty;
            _txtRepetitionPenalty.Text = string.Empty;
            _txtMinP.Text = string.Empty;
            UpdateInputState();
            _txtError.IsVisible = false;
        }

        private void Accept()
        {
            _txtError.IsVisible = false;

            var useCustom = _chkUseCustomSamplers.IsChecked == true;

            if (!useCustom)
            {
                _settings.OpenAiCompatibleUseCustomSamplerSettings = false;
                Close(true);
                return;
            }

            // Permissive sane-guards: block only mathematically-impossible / clearly
            // fat-fingered values, NOT cloud limits. This provider targets local
            // backends (Ollama / vLLM / text-generation-webui) that allow far wider
            // ranges than OpenAI cloud (e.g. temperature up to ~5+). top_p and min_p
            // are cumulative probabilities so they are hard-bounded to 0..1; top_k
            // accepts -1 as the "disabled" sentinel used by vLLM and others.
            if (!TryParseDouble(_txtTemperature.Text, "temperature", 0d, 10d, out var temperature)
                || !TryParseDouble(_txtTopP.Text, "top_p", 0d, 1d, out var topP)
                || !TryParseInt(_txtTopK.Text, "top_k", -1, int.MaxValue, out var topK)
                || !TryParseDouble(_txtFrequencyPenalty.Text, "frequency_penalty", -2d, 2d, out var frequencyPenalty)
                || !TryParseDouble(_txtPresencePenalty.Text, "presence_penalty", -2d, 2d, out var presencePenalty)
                || !TryParseDouble(_txtRepetitionPenalty.Text, "repetition_penalty", 0d, 10d, out var repetitionPenalty)
                || !TryParseDouble(_txtMinP.Text, "min_p", 0d, 1d, out var minP))
            {
                return;
            }

            _settings.OpenAiCompatibleUseCustomSamplerSettings = true;
            _settings.OpenAiCompatibleTemperature = temperature;
            _settings.OpenAiCompatibleTopP = topP;
            _settings.OpenAiCompatibleTopK = topK;
            _settings.OpenAiCompatibleFrequencyPenalty = frequencyPenalty;
            _settings.OpenAiCompatiblePresencePenalty = presencePenalty;
            _settings.OpenAiCompatibleRepetitionPenalty = repetitionPenalty;
            _settings.OpenAiCompatibleMinP = minP;

            Close(true);
        }

        private bool TryParseDouble(string? text, string fieldName, double min, double max, out double? value)
        {
            value = null;
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) return true;

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                ShowError($"'{fieldName}' must be a number.");
                return false;
            }

            if (parsed < min || parsed > max)
            {
                ShowError($"'{fieldName}' must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.");
                return false;
            }

            value = parsed;
            return true;
        }

        private bool TryParseInt(string? text, string fieldName, int min, int max, out int? value)
        {
            value = null;
            var trimmed = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed)) return true;

            if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                ShowError($"'{fieldName}' must be a whole number.");
                return false;
            }

            if (parsed < min || parsed > max)
            {
                ShowError($"'{fieldName}' must be between {min} and {max}.");
                return false;
            }

            value = parsed;
            return true;
        }

        private void ShowError(string message)
        {
            _txtError.Text = message;
            _txtError.IsVisible = true;
        }
    }

    public sealed class OpenAiCompatibleSamplerSettingsViewModel
    {
        public string LocTitle => Loc.Get("dialog_openai_sampler_title");
        public string LocHint => Loc.Get("hint_sampler_settings");
        public string LocUseCustom => Loc.Get("label_sampler_use_custom_settings");
        public string LocTemperature => Loc.Get("label_sampler_temperature");
        public string LocTopP => Loc.Get("label_sampler_top_p");
        public string LocTopK => Loc.Get("label_sampler_top_k");
        public string LocMinP => Loc.Get("label_sampler_min_p");
        public string LocFrequencyPenalty => Loc.Get("label_sampler_frequency_penalty");
        public string LocPresencePenalty => Loc.Get("label_sampler_presence_penalty");
        public string LocRepetitionPenalty => Loc.Get("label_sampler_repetition_penalty");
        public string LocResetDefaults => Loc.Get("label_sampler_reset_defaults");
        public string LocCancel => Loc.Get("btn_cancel");
        public string LocOk => Loc.Get("btn_ok");
    }
}
