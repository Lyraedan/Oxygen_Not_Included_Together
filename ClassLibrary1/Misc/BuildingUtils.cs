using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static LogicGateVisualizer;

namespace ONI_MP.Misc
{
    public static class BuildingUtils
    {
        public static ObjectLayer GetActualObjectLayer(Building building)
        {
            if (building == null || building.Def == null)
                return ObjectLayer.Building; // Default as building layer

            int baseCell = Grid.PosToCell(building.transform.position);

            // First check intended layer
            ObjectLayer defLayer = building.Def.ObjectLayer;

            if (Grid.Objects[baseCell, (int)defLayer] == building.gameObject)
                return defLayer;

            // Check all occupied cells
            if (building.Def.PlacementOffsets != null)
            {
                foreach (var offset in building.Def.PlacementOffsets)
                {
                    int cell = Grid.OffsetCell(baseCell, offset);

                    for (int i = 0; i < (int)ObjectLayer.NumLayers; i++)
                    {
                        if (Grid.Objects[cell, i] == building.gameObject)
                            return (ObjectLayer)i;
                    }
                }
            }

            // Fallback
            for (int i = 0; i < (int)ObjectLayer.NumLayers; i++)
            {
                if (Grid.Objects[baseCell, i] == building.gameObject)
                    return (ObjectLayer)i;
            }

            // We somehow found nothing
            return defLayer;
        }

        public static void GetLayerInfo(Building building, out ObjectLayer layer, out bool isReplacement)
        {
            layer = GetActualObjectLayer(building);
            isReplacement = IsReplacementLayer(layer);
            layer = NormalizeLayer(layer);
        }

        public static bool IsReplacementLayer(ObjectLayer layer)
        {
            switch (layer)
            {
                case ObjectLayer.ReplacementTile:
                case ObjectLayer.ReplacementGasConduit:
                case ObjectLayer.ReplacementLiquidConduit:
                case ObjectLayer.ReplacementSolidConduit:
                case ObjectLayer.ReplacementWire:
                case ObjectLayer.ReplacementLogicWire:
                case ObjectLayer.ReplacementTravelTube:
                case ObjectLayer.ReplacementLadder:
                case ObjectLayer.ReplacementBackwall:
                    return true;
                default:
                    return false;
            }
        }

        public static ObjectLayer NormalizeLayer(ObjectLayer layer)
        {
            switch (layer)
            {
                case ObjectLayer.ReplacementTile:
                    return ObjectLayer.FoundationTile;

                case ObjectLayer.ReplacementGasConduit:
                    return ObjectLayer.GasConduit;

                case ObjectLayer.ReplacementLiquidConduit:
                    return ObjectLayer.LiquidConduit;

                case ObjectLayer.ReplacementSolidConduit:
                    return ObjectLayer.SolidConduit;

                case ObjectLayer.ReplacementWire:
                    return ObjectLayer.Wire;

                case ObjectLayer.ReplacementLogicWire:
                    return ObjectLayer.LogicWire;

                case ObjectLayer.ReplacementTravelTube:
                    return ObjectLayer.TravelTube;

                case ObjectLayer.ReplacementLadder:
                    return ObjectLayer.LadderTile;

                case ObjectLayer.ReplacementBackwall:
                    return ObjectLayer.Backwall;

                default:
                    return layer;
            }
        }

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
    }
}
