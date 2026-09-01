namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The app's Discord destinations, in one place. Every button, help entry and EMI
    /// offer that sends a user to Discord goes through these so a server move is one edit.
    /// </summary>
    public static class DiscordLinks
    {
        /// <summary>Server invite — the front door, for users who are not members yet.</summary>
        public const string Invite = "https://discord.gg/YxVAMt4qaZ";

        /// <summary>The #asset-packs forum — the pack catalogue, where people share packs daily.
        /// A deep link only works for members; pair it with <see cref="Invite"/> for the rest.</summary>
        public const string PackCatalogue = "https://discord.com/channels/1456573221489999934/1511409848699584653";
    }
}
