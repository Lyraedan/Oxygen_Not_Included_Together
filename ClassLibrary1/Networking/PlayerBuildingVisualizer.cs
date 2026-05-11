using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using UnityEngine;
using static Grid.Restriction;
using static LogicGateVisualizer;
using static STRINGS.DUPLICANTS.MODIFIERS;

namespace ONI_MP.Networking
{
    /// <summary>
    /// KNOWN ISSUE: If the simulation is paused, other player visualizers act strange
    /// KNOWN ISSUE: Ladders visual seems to act inconsistently, sometimes it'll appear, sometimes it won't
    /// </summary>
    public class PlayerBuildingVisualizer
    {
        public enum VisualizerType
        {
            BUILDING,
            UTILITY,
            TILE
        }

        private GameObject visualizer;
        private string lastPrefabId = string.Empty;
        private Color color = Color.white; // Base color

        private VisualizerType visualizerType = VisualizerType.BUILDING;
        private Orientation CurrentOrientation;
        private BuildingDef CurrentDef;

        private Color currentColor = Color.white; // Color based on if the tile is valid or not

        private Color visualColor // Valid color
        { 
            get
            {
                return Color.Lerp(color, Color.white, 0.75f);
            }
        }
        private Color darkerColor // Invalid color
        {
            get
            {
                return Color.Lerp(color, Color.black, 0.75f);
            }
        }

        private int _cell = Grid.InvalidCell;
        public int Cell
        {
            set
            {
                if (_cell == value)
                    return;

                if (visualizer != null && CurrentDef != null)
                {
                    visualizer.transform.position = Grid.CellToPosCBC(value, CurrentDef.SceneLayer);
                    if (visualizer.TryGetComponent<KBatchedAnimController>(out var kbac))
                    {
                        UpdateVisualColor(value);
                        kbac.TintColour = currentColor;
                    }

                    switch (visualizerType)
                    {
                        case VisualizerType.UTILITY:
                            UpdateUtilityConnectionVis(value);
                            break;
                        case VisualizerType.TILE:
                            UpdateTileVisual(value);
                            break;
                        default:
                        case VisualizerType.BUILDING:
                            // Nothing to do
                            break;
                    }
                }

                OnCellChanged?.Invoke(value);
                _cell = value;
            }
            get
            {
                return _cell;
            }
        }

        public System.Action<int> OnCellChanged; // Leave this incase we want to do something with it later

        public void UpdateVisualizer(VisualizerType type, string buildingPrefabId, Vector3 position, Orientation orientation, Color visualColor)
        {
            this.color = visualColor;
            this.CurrentOrientation = orientation;
            int posCell = Grid.PosToCell(position);

            if (lastPrefabId.Equals(buildingPrefabId) && !visualizer.IsNullOrDestroyed())
            {
                UpdateCell(position); // Instead of updating the visualizer object update its position
                return;
            }

            // Destroy the visualiser if nothing is selected
            if (string.IsNullOrEmpty(buildingPrefabId) || !lastPrefabId.Equals(buildingPrefabId))
            {
                if (!visualizer.IsNullOrDestroyed())
                {
                    switch(visualizerType)
                    {
                        case VisualizerType.TILE:
                            RemoveTileVisual(posCell); // Unique tile removal
                            break;
                    }
                    Util.KDestroyGameObject(visualizer); // Destroy the visualiser
                    visualizer = null;
                }
            }

            BuildingDef def = Assets.GetBuildingDef(buildingPrefabId);
            if (def == CurrentDef) // Same def somehow leaked through
                return;

            if (def != null)
            {
                CurrentDef = def;
                lastPrefabId = buildingPrefabId;

                Vector3 pos = Grid.CellToPosCBC(posCell, def.SceneLayer);
                visualizer = GameUtil.KInstantiate(def.BuildingPreview, pos, Grid.SceneLayer.Front, "OtherPlayerBuildingVisualizer", LayerMask.NameToLayer("Place"));
                visualizer.transform.SetPosition(pos);
                visualizer.SetActive(true);

                switch(visualizerType)
                {
                    default:
                    case VisualizerType.BUILDING:
                        HandleBuildingVisual(posCell);
                        break;
                    // These are unimplemented atm
                    case VisualizerType.UTILITY:
                        HandleUtlilityVisual(posCell);
                        break;
                    case VisualizerType.TILE:
                        HandleTileVisual(posCell);
                        break;
                }
            }
        }

        private void HandleBuildingVisual(int cell)
        {
            if (visualizer.TryGetComponent<Rotatable>(out var rotatable))
            {
                rotatable.SetOrientation(CurrentOrientation);
            }

            if (visualizer.TryGetComponent<KBatchedAnimController>(out var kbac))
            {
                kbac.visibilityType = KAnimControllerBase.VisibilityType.Always;
                kbac.isMovable = true;
                kbac.Offset = Vector3.zero;
                UpdateVisualColor(cell);
                kbac.TintColour = visualColor;

                kbac.SetLayer(LayerMask.NameToLayer("Place"));
                kbac.Play("place");
            }
            else
            {
                visualizer.SetLayerRecursively(LayerMask.NameToLayer("Place"));
            }
        }

        private void HandleUtlilityVisual(int cell)
        {
            
        }

        private void UpdateUtilityConnectionVis(int cell)
        {
            // Called when the Cell changes
        }

        private void HandleTileVisual(int cell)
        {
            
        }

        private void RemoveTileVisual(int cell)
        {
            // Called when the build tool closes SPECIFIC to the TILE type
        }

        private void UpdateTileVisual(int cell)
        {

        }

        public void UpdateCell(Vector3 position)
        {
            int cell = Grid.PosToCell(position);
            if (cell != Grid.InvalidCell)
            {
                Cell = cell;
            }
        }

        public void UpdateVisualColor(int cell)
        {
            bool isValid = BuildingUtils.ValidCell(visualizer, CurrentDef, cell, CurrentOrientation);
            if (isValid)
            {
                currentColor = visualColor;
            } else
            {
                currentColor = darkerColor;
            }
        }
    }
}
