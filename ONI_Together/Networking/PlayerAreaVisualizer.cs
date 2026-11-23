using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace ONI_Together.Networking
{
    public class PlayerAreaVisualizer
    {
        private GameObject _visualizer;
        private GameObject _textPrefab;
        private Guid _areaVisualizerText = Guid.Empty;
        private SpriteRenderer _areaRenderer;

        private Color _color = Color.white;

        private DragTool.Mode _mode = DragTool.Mode.Box;

        private bool cellChangedSinceDown = false;
        private Vector3 _lastDownPos;

        public Color Color
        {
            get => _color;
            set => _color = value;
        }

        public void InstantiateNewVisualizer()
        {
            if(_visualizer != null)
            {
                return;
            }

            GameObject areaVisualizer = Util.KInstantiate(DeconstructTool.Instance.areaVisualizer);
            areaVisualizer.name = "PlayerVisualizer";

            _areaRenderer = areaVisualizer.GetComponent<SpriteRenderer>();
            _areaRenderer.color = Color;
            _areaRenderer.material.color = Color;

            _visualizer = areaVisualizer;
        }

        private void EnsureAreaText()
        {
            if (_areaVisualizerText != Guid.Empty)
                return;

            _textPrefab = DigTool.Instance.areaVisualizerTextPrefab;
            _areaVisualizerText = NameDisplayScreen.Instance.AddAreaText("", _textPrefab);
            NameDisplayScreen.Instance.GetWorldText(_areaVisualizerText).GetComponent<LocText>().color = Color;
        }

        public void DestroyArea()
        {
            if (_visualizer != null)
            {
                RemoveCurrentAreaText();
                Util.KDestroyGameObject(_visualizer);
                _visualizer = null;
                _areaRenderer = null;
                cellChangedSinceDown = false;
                _lastDownPos = Vector3.zero;
            }
        }

        public void UpdateArea(Color color, Vector3 downPos, Vector3 cursorPos, bool dragging, DragTool.Mode dragMode, Vector2 lengthLimit, bool hasBrushPreview, byte brushRadius)
        {
            Color = color;

            if (!ShouldShow(dragging, dragMode, hasBrushPreview))
            {
                DestroyArea();
                cellChangedSinceDown = false;
                _lastDownPos = Vector3.zero;
                return;
            }

            if (downPos != _lastDownPos)
            {
                _lastDownPos = downPos;
                cellChangedSinceDown = false;
            }

            InstantiateNewVisualizer();

            if (hasBrushPreview)
            {
                if (!Grid.IsValidCell(Grid.PosToCell(cursorPos)))
                {
                    DestroyArea();
                    return;
                }

                RemoveCurrentAreaText();
                ApplyBrushRect(cursorPos, brushRadius);
                _visualizer.SetActive(true);
                return;
            }

            EnsureAreaText();

            if (Grid.PosToCell(cursorPos) != Grid.PosToCell(downPos))
            {
                cellChangedSinceDown = true;
            }

            cursorPos = SnapCursorForLineMode(downPos, cursorPos, dragMode);
            cursorPos = ClampToLengthLimit(downPos, cursorPos, dragMode, lengthLimit);
            ApplyAreaRect(downPos, cursorPos);
            _visualizer.SetActive(true);
        }

        private static Vector2 GetRegularizedPos(Vector2 input, bool minimize)
        {
            Vector3 halfCell = new Vector3(Grid.HalfCellSizeInMeters, Grid.HalfCellSizeInMeters, 0f);
            Vector3 cellCenter = Grid.CellToPosCCC(Grid.PosToCell(input), Grid.SceneLayer.Background);
            return (Vector2)(cellCenter + (minimize ? -halfCell : halfCell));
        }

        private static bool ShouldShow(bool dragging, DragTool.Mode mode, bool hasBrushPreview)
        {
            return hasBrushPreview || (dragging && (mode == DragTool.Mode.Box || mode == DragTool.Mode.Line));
        }

        private void ApplyBrushRect(Vector3 cursorPos, byte brushRadius)
        {
            Vector3 center = Grid.CellToPosCCC(Grid.PosToCell(cursorPos), Grid.SceneLayer.Background);
            // ONI's brush offsets cover 2 * radius - 1 cells. Radius 1 is the
            // single-cell footprint used by regular DragTools in brush mode.
            float diameter = Mathf.Max(1, brushRadius * 2 - 1) * Grid.CellSizeInMeters;

            _visualizer.transform.position = new Vector3(center.x, center.y, 0f);
            _areaRenderer.size = new Vector2(diameter, diameter);
        }

        // Dont need this tbh
        private static void ClampToWorld(ref Vector3 pos)
        {
            var world = ClusterManager.Instance.activeWorld;
            pos.x = Mathf.Clamp(pos.x, world.minimumBounds.x, world.maximumBounds.x);
            pos.y = Mathf.Clamp(pos.y, world.minimumBounds.y, world.maximumBounds.y);
        }

        private static Vector3 SnapCursorForLineMode(Vector3 downPos, Vector3 cursorPos, DragTool.Mode mode)
        {
            if (mode != DragTool.Mode.Line)
                return cursorPos;

            Vector3 offset = cursorPos - downPos;
            DragTool.DragAxis axis = Mathf.Abs(offset.x) >= Mathf.Abs(offset.y) ? DragTool.DragAxis.Horizontal : DragTool.DragAxis.Vertical;

            if (axis == DragTool.DragAxis.Horizontal)
                cursorPos.y = downPos.y;
            else
                cursorPos.x = downPos.x;

            return cursorPos;
        }

        private static Vector3 ClampToLengthLimit(Vector3 downPos, Vector3 cursorPos, DragTool.Mode mode, Vector2 lengthLimit)
        {
            if (lengthLimit == Vector2.zero)
                return cursorPos;

            float cellSize = Grid.CellSizeInMeters;
            Vector3 offset = cursorPos - downPos;

            float maxX = Mathf.Max(0f, lengthLimit.x - 1f) * cellSize;
            float maxY = Mathf.Max(0f, lengthLimit.y - 1f) * cellSize;

            if (mode == DragTool.Mode.Box)
            {
                offset.x = Mathf.Clamp(offset.x, -maxX, maxX);
                offset.y = Mathf.Clamp(offset.y, -maxY, maxY);
            }
            else if (mode == DragTool.Mode.Line)
            {
                if (cursorPos.y == downPos.y)
                    offset.x = Mathf.Clamp(offset.x, -maxX, maxX);
                else
                    offset.y = Mathf.Clamp(offset.y, -maxY, maxY);
            }

            return downPos + offset;
        }

        private void ApplyAreaRect(Vector3 downPos, Vector3 cursorPos)
        {
            Vector2 input1 = Vector3.Max(downPos, cursorPos);
            Vector2 input2 = Vector3.Min(downPos, cursorPos);

            Vector2 reg1 = GetRegularizedPos(input1, false);
            Vector2 reg2 = GetRegularizedPos(input2, true);

            Vector2 size = reg1 - reg2;
            Vector2 center = (reg1 + reg2) * 0.5f;

            _visualizer.transform.position = new Vector3(center.x, center.y, 0f);
            _areaRenderer.size = size;

            UpdateText(size, center);
        }

        private void UpdateText(Vector3 size, Vector3 center)
        {
            if (_areaVisualizerText != Guid.Empty)
            {
                Vector2I sizeI = new Vector2I(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
                LocText component = NameDisplayScreen.Instance.GetWorldText(_areaVisualizerText).GetComponent<LocText>();
                component.text = string.Format(global::STRINGS.UI.TOOLS.TOOL_AREA_FMT, sizeI.x, sizeI.y, sizeI.x * sizeI.y);
                TransformExtensions.SetPosition(position: center, transform: component.transform);
            }
        }

        public void RemoveCurrentAreaText()
        {
            if (_areaVisualizerText != Guid.Empty)
            {
                NameDisplayScreen.Instance.RemoveWorldText(_areaVisualizerText);
                _areaVisualizerText = Guid.Empty;
            }
        }

    }
}
