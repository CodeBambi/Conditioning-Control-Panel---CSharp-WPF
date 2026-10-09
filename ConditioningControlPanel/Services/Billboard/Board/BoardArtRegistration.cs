namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>Registers the "board" art view. Called once at startup by the deck's wiring.</summary>
    public static class BoardArtRegistration
    {
        public const string Key = "board";

        public static void Register() =>
            BillboardArt.Register(Key, data => new global::ConditioningControlPanel.Controls.Billboard.BoardTileView(data));
    }
}
