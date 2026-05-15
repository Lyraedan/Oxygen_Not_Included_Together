using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static LogicGateVisualizer;
using static ONI_MP.Networking.Components.StructureStateSyncers.StorageStateSyncer;

namespace ONI_MP.Misc
{
    public static class BuildingUtils
    {
        public static bool ValidCell(GameObject visualizer, BuildingDef def, int cell, Orientation orientation)
        {
            if (Grid.IsValidCell(cell)
                && Grid.IsVisible(cell))
            {
                bool IsValidPlaceLocation = def.IsValidPlaceLocation(visualizer, cell, orientation, out string failReason);
                bool IgnorableFailReason =
                    failReason == global::STRINGS.UI.TOOLTIPS.HELP_BUILDLOCATION_WALL
                    || failReason == global::STRINGS.UI.TOOLTIPS.HELP_BUILDLOCATION_CORNER
                    || failReason == global::STRINGS.UI.TOOLTIPS.HELP_BUILDLOCATION_CORNER_FLOOR
                    || (failReason == global::STRINGS.UI.TOOLTIPS.HELP_BUILDLOCATION_BACK_WALL_REQUIRED);

                bool validCell = (IsValidPlaceLocation || IgnorableFailReason);
                bool replacement = false;
                return (validCell || replacement);
            }

            return false;
        }

        public static void EncodeStorageContents(Storage storage, out List<Variant> optionalValues)
        {
            var items = new List<StorageData>();
            for (int i = 0; i < storage.items.Count; i++)
            {
                if (storage.items[i] == null) continue;
                var pe = storage.items[i].GetComponent<PrimaryElement>();
                if (pe == null || pe.Mass <= 0f) continue;
                if (!storage.items[i].TryGetComponent<KPrefabID>(out var prefabID))
                    continue;

                items.Add(new StorageData
                {
                    PrefabTagHash = prefabID.PrefabTag.GetHashCode(),
                    Mass = pe.Mass,
                    Units = pe.Units,
                    Temperature = pe.Temperature,
                    DiseaseIdx = pe.DiseaseIdx,
                    DiseaseCount = pe.DiseaseCount
                });
            }

            optionalValues = new List<Variant>(2 + items.Count * 6);
            optionalValues.Add(storage.capacityKg);
            optionalValues.Add(items.Count);
            foreach (var item in items)
            {
                optionalValues.Add(item.PrefabTagHash);
                optionalValues.Add(item.Mass);
                optionalValues.Add(item.Units);
                optionalValues.Add(item.Temperature);
                optionalValues.Add(item.DiseaseIdx);
                optionalValues.Add(item.DiseaseCount);
            }
        }

        public static void RebuildStorageFromData(Storage storage, List<Variant> data, string diseaseReason = "Multiplayer Sync")
        {
            if (storage == null || data.Count < 2) return;
            ClearStorage(storage);

            int count = data[1].Int;
            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                int baseIdx = 2 + i * 6;
                if (baseIdx + 6 > data.Count) break;

                int hash = data[baseIdx].Int;
                float mass = data[baseIdx + 1].Float;
                float temperature = data[baseIdx + 3].Float;
                byte diseaseIdx = data[baseIdx + 4].Byte;
                int diseaseCount = data[baseIdx + 5].Int;
                if (mass <= 0f) continue;

                Tag tag = new Tag(hash);
                Element elementByHash = ElementLoader.GetElement(tag);
                if (elementByHash != null)
                {
                    storage.AddElement(elementByHash.id, mass, temperature, diseaseIdx, diseaseCount);
                }
                else
                {
                    var item = Assets.GetPrefab(tag);
                    if (item == null) continue;

                    var scrapObject = GameUtil.KInstantiate(item, storage.transform.position, Grid.SceneLayer.Ore);
                    if (scrapObject.TryGetComponent<PrimaryElement>(out var pe))
                    {
                        pe.Mass = mass;
                        pe.Temperature = temperature;
                        if (diseaseIdx != byte.MaxValue)
                            pe.AddDisease(diseaseIdx, diseaseCount, diseaseReason);
                    }
                    scrapObject.SetActive(true);
                    storage.Store(scrapObject, true, true);
                }
            }
        }

        private static void ClearStorage(Storage storage)
        {
            for (int i = storage.items.Count - 1; i >= 0; i--)
                storage.items[i].DeleteObject();
            storage.items.Clear();
        }
    }
}
