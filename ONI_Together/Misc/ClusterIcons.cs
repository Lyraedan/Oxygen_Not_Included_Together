using System;
using System.Collections.Generic;
using Klei.CustomSettings;
using ONI_Together.DebugTools;
using ProcGen;

namespace ONI_Together.Misc;

public static class ClusterIcons
    {
        // Astroid image grabbing inspired by Pholith : https://github.com/Pholith/ONI-Mods/blob/master/src/DiscordRP/
        private const string BaseUrl = "https://oxygennotincluded.wiki.gg/images/";

        public struct Entry
        {
            public string Id;
            public string Name;
            public string IconUrl;
        }

        private static readonly Dictionary<string, string> IconFiles = new()
        {
            // Base Game (no DLC)
            ["SandstoneDefault"]                  = "Terra_Asteroid.png",
            ["SandstoneFrozen"]                   = "Rime_Asteroid.png",
            ["ForestDefault"]                     = "Arboria_Asteroid.png",
            ["ForestLush"]                        = "Verdante_Asteroid.png",
            ["ForestHot"]                         = "Aridio_Asteroid.png",
            ["Badlands"]                          = "The_Badlands_Asteroid.png",
            ["Oceania"]                           = "Oceania_Asteroid.png",
            ["Volcanic"]                          = "Volcanea_Asteroid.png",
            ["Oasis"]                             = "Oasisse_Asteroid.png",
            ["KleiFest2023"]                      = "Skewed_Asteroid.png",
            ["CeresBaseGameCluster"]              = "Ceres_Asteroid.png",
            ["CeresBaseGameShatteredCluster"]     = "Blasted_Ceres_Asteroid.png",
            ["PrehistoricBaseGameCluster"]        = "Relica_Asteroid.png",
            ["PrehistoricShatteredBaseGameCluster"] = "RelicAAAAAAAGHH_Asteroid.png",
            ["AquaticBaseGameCluster"]            = "Marinea_Asteroid.png",

            // Classic (DLC, Vanilla-style)
            ["VanillaSandstoneCluster"]       = "Terra_Asteroid_(Spaced_Out).png",
            ["AquaticClassicCluster"]         = "Marinea_Asteroid_(Spaced_Out).png",
            ["PrehistoricClassicCluster"]     = "Relica_Asteroid_(Spaced_Out).png",
            ["CeresClassicCluster"]           = "Ceres_Asteroid_(Spaced_Out).png",
            ["VanillaOceaniaCluster"]         = "Oceania_Asteroid_(Spaced_Out).png",
            ["VanillaSwampCluster"]           = "Squelchy_Asteroid.png",
            ["VanillaSandstoneFrozenCluster"] = "Rime_Asteroid_(Spaced_Out).png",
            ["VanillaForestCluster"]          = "Verdante_Asteroid_(Spaced_Out).png",
            ["VanillaArboriaCluster"]         = "Arboria_Asteroid_(Spaced_Out).png",
            ["VanillaVolcanicCluster"]        = "Volcanea_Asteroid_(Spaced_Out).png",
            ["VanillaBadlandsCluster"]        = "The_Badlands_Asteroid_(Spaced_Out).png",
            ["VanillaAridioCluster"]          = "Aridio_Asteroid_(Spaced_Out).png",
            ["VanillaOasisCluster"]           = "Oasisse_Asteroid_(Spaced_Out).png",

            // Spaced Out!
            ["SandstoneStartCluster"]         = "Terra_Asteroid_(Spaced_Out).png",
            ["AquaticSpacedOutCluster"]       = "Marinea_Minor_Asteroid.png",
            ["PrehistoricSpacedOutCluster"]   = "Relica_Minor_Asteroid.png",
            ["CeresSpacedOutCluster"]         = "Ceres_Minor_Asteroid.png",
            ["SwampStartCluster"]             = "Quagmiris_Asteroid.png",
            ["ForestStartCluster"]            = "Folia_Asteroid.png",
            ["CeresSpacedOutShatteredCluster"]= "Ceres_Mantle_Asteroid.png",

            // Moonlets
            ["MiniClusterMetallicSwampyStart"]  = "Metallic_Swampy_Asteroid.png",
            ["MiniClusterBadlandsStart"]        = "The_Desolands_Asteroid.png",
            ["MiniClusterForestFrozenStart"]    = "Frozen_Forest_Asteroid.png",
            ["MiniClusterFlippedStart"]         = "Flipped_Asteroid.png",
            ["MiniClusterRadioactiveOceanStart"]= "Radioactive_Ocean_Asteroid.png",

            // The Lab
            ["KleiFest2023Cluster"]                = "Skewed_Asteroid_(Spaced_Out).png",
            ["PrehistoricShatteredClassicCluster"] = "RelicAAAAAAAGHH_Asteroid.png",
            ["CeresClassicShatteredCluster"]       = "Blasted_Ceres_Asteroid_(Spaced_Out).png",
        };

        public static string ResolveUrl(string clusterId)
        {
            var icon = IconFiles.TryGetValue(clusterId, out var found) ? found : "Asteroid.png";
            return $"{BaseUrl}{icon}";
        }

        public static Entry? ResolveCurrent()
        {
            var settings = CustomGameSettings.Instance;
            if (settings == null)
                return null;

            var config = CustomGameSettingConfigs.ClusterLayout;

            // ClusterLayout.levels is only populated by CustomGameSettings.LoadClusters(),
            // which runs when the new-game screen opens or a save loads. Until then it is
            // null, and Klei's GetCurrentQualitySetting -> ListSettingConfig.GetLevel
            // dereferences it without a null check (NRE on the frontend).
            var levels = config?.GetLevels();
            if (levels == null || levels.Count == 0)
                return null;

            try
            {
                var setting = settings.GetCurrentQualitySetting(config);

                if (setting == null || string.IsNullOrEmpty(setting.id))
                    return null;

                if (!SettingsCache.clusterLayouts.clusterCache.TryGetValue(
                        setting.id, out var clusterLayout))
                    return null;

                var clusterId = setting.id.Substring(setting.id.LastIndexOf('/') + 1);

                return new Entry {
                    Id = clusterId,
                    Name = Strings.Get(clusterLayout.name),
                    IconUrl = ResolveUrl(clusterId)
                };
            }
            catch (Exception ex)
            {
                DebugConsole.LogWarning($"[ClusterIcons] Failed to resolve cluster layout: {ex.Message}");
                return null;
            }
        }
    }