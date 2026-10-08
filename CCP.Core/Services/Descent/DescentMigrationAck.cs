using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// The ack half of the migration handshake (moved from WPF ProfileSyncService.HandleDescentMigrationAck /
    /// EnsureCycleBonus, abac4fa02). THE ACK IS THE ONLY THING THAT MAY WRITE <c>DescentMigrationCompleted</c>.
    /// It rides every sync of a migrated account, so it also heals the lasting XP bonus on a device that never
    /// ran the migration itself. Never touches level or XP. Both heads call it; neither saves here.
    /// </summary>
    public static class DescentMigrationAck
    {
        /// <summary>A completed migration owes the lasting XP bonus, whichever way it went (owner, 2026-10-06:
        /// migrated = bonus); a server-recorded Cycle choice also owes Cycle I. Only ever raises. True when it wrote.</summary>
        public static bool EnsureCycleBonus(Models.AppSettings settings, string? serverChoice)
        {
            var changed = false;
            if (serverChoice == DescentMigrationChoices.Cycle && settings.DescentCycle < 1)
            {
                settings.DescentCycle = 1;
                changed = true;
            }
            if (settings.DescentCycleXpBonus < DescentCycleXp.CycleXpBonus)
            {
                settings.DescentCycleXpBonus = DescentCycleXp.CycleXpBonus;
                changed = true;
            }
            if (changed)
                Log.Information("[Descent] Restored the migration XP bonus from the server's record (choice={Choice}).",
                    serverChoice ?? "unknown");
            return changed;
        }

        /// <summary>Settle an ack (<c>completed: true</c>). Idempotent. True when settings changed (caller saves).</summary>
        public static bool Apply(Models.AppSettings settings, bool? completed, string? serverChoice)
        {
            if (completed != true) return false;
            var changed = EnsureCycleBonus(settings, serverChoice);
            if (settings.DescentMigrationCompleted) return changed;   // already settled

            // The server's echo wins; it can only differ from our pending choice if another device migrated.
            var choice = DescentMigrationChoices.IsValid(serverChoice)
                ? serverChoice
                : settings.PendingDescentMigrationChoice;

            settings.DescentMigrationCompleted = true;
            settings.DescentMigrationChoice = choice;
            settings.PendingDescentMigrationChoice = null;
            settings.DescentMigrationOffered = false;
            Log.Information("[Descent] Migration ACKNOWLEDGED by server (choice={Choice}). Curve v2 is now this account's curve, permanently.",
                choice ?? "unknown");
            return true;
        }

        /// <summary>The <c>descent_migration</c> block of a /v2/user/sync response; absent or malformed = nothing.</summary>
        public static bool Apply(Models.AppSettings settings, JObject response) =>
            response["descent_migration"] is JObject block
            && Apply(settings, block["completed"]?.Type == JTokenType.Boolean ? block.Value<bool>("completed") : null,
                     block["choice"]?.Type == JTokenType.String ? block.Value<string>("choice") : null);
    }
}
