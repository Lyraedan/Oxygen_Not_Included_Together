using System;
using System.Collections.Generic;
using System.Linq;

namespace ONI_Together_DedicatedServer.Security
{
    public static class ColonyDlcCheck
    {
        public const string SpacedOut = "EXPANSION1_ID";

        private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
        {
            [SpacedOut] = "Spaced Out!",
            ["DLC2_ID"] = "The Frosty Planet Pack",
            ["DLC3_ID"] = "The Bionic Booster Pack",
            ["DLC4_ID"] = "The Prehistoric Planet Pack",
            ["DLC5_ID"] = "Beached",
        };

        public static string DisplayName(string dlcId) => Names.TryGetValue(dlcId, out var name) ? name : dlcId;

        public static string Describe(IEnumerable<string> dlcIds)
        {
            var list = dlcIds.ToList();
            return list.Count == 0 ? "base game only" : string.Join(", ", list.Select(DisplayName));
        }

        public static DlcCheckResult Check(IReadOnlyCollection<string> colonyDlcIds, IReadOnlyCollection<string> activeDlcIds, IReadOnlyCollection<string> ownedDlcIds)
        {
            var colony = new HashSet<string>(colonyDlcIds, StringComparer.Ordinal);
            var active = new HashSet<string>(activeDlcIds, StringComparer.Ordinal);
            var available = new HashSet<string>(active.Concat(ownedDlcIds), StringComparer.Ordinal);

            bool spacedOutColony = colony.Contains(SpacedOut);
            if (spacedOutColony && !active.Contains(SpacedOut))
                return DlcCheckResult.Deny(available.Contains(SpacedOut)
                    ? "This server's colony is a Spaced Out! colony. Enable Spaced Out! in the game's DLC settings, then join again."
                    : "This server's colony needs Spaced Out!, which your game does not own.");
            if (!spacedOutColony && active.Contains(SpacedOut))
                return DlcCheckResult.Deny("This server's colony is a base game colony. Disable Spaced Out! in the game's DLC settings, then join again.");

            var missing = colony.Where(id => id != SpacedOut && !available.Contains(id)).ToList();
            if (missing.Count > 0)
                return DlcCheckResult.Deny($"This server's colony needs DLC your game does not own: {Describe(missing)}.");

            return DlcCheckResult.Ok();
        }
    }
}
