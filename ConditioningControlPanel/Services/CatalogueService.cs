namespace ConditioningControlPanel.Services
{
    /// <summary>The catalogue client (Core <see cref="CatalogueClient"/>) fed from App state.</summary>
    public class CatalogueService : CatalogueClient
    {
        public CatalogueService()
            : base(() => App.Settings?.Current?.AuthToken, () => App.UnifiedUserId, UpdateService.AppVersion) { }
    }
}
