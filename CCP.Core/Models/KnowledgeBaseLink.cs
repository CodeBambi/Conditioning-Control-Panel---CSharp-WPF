using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Represents a link in the global knowledge base.
    /// These links are shared across ALL personality presets and appended to every AI prompt.
    /// </summary>
    public class KnowledgeBaseLink : INotifyPropertyChanged
    {
        private string _url = "";
        private string _title = "";
        private string _description = "";

        /// <summary>
        /// The URL of the resource (e.g., hypnotube video link).
        /// </summary>
        [JsonProperty("url")]
        public string Url
        {
            get => _url;
            set { _url = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Display title for the link (e.g., "Bambi Training Video").
        /// </summary>
        [JsonProperty("title")]
        public string Title
        {
            get => _title;
            set { _title = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>
        /// Optional description of what the link contains.
        /// </summary>
        [JsonProperty("description")]
        public string Description
        {
            get => _description;
            set { _description = value ?? ""; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Formats this link for inclusion in AI prompts.
        /// </summary>
        public string ToPromptText()
        {
            var result = $"- {Title}: {Url}";
            if (!string.IsNullOrWhiteSpace(Description))
            {
                result += $"\n  ({Description})";
            }
            return result;
        }
    }
}
