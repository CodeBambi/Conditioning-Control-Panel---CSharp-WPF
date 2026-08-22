namespace ConditioningControlPanel.Models.AiEnrichment
{
    public class KnowledgeTrigger : IBaseModel
    {
        public string Trigger { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
