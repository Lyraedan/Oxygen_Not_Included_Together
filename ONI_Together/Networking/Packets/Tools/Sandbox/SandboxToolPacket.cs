using System;
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Sandbox
{
    public enum SandboxToolAction : byte
    {
        Brush,
        Sprinkle,
        Flood,
        Sample,
        Heat,
        Stress,
        Spawn,
        Destroy,
        Reveal,
        ClearFloor,
        CritterRemoval,
        StoryTrait
    }

    /// <summary>
    /// Replicates sandbox tool actions together with the settings that produced
    /// them. Per-cell brush actions are bulkable to keep drag traffic bounded.
    /// </summary>
    public sealed class SandboxToolPacket : IPacket, IBulkablePacket, IClientRelayable
    {
        public static bool ProcessingIncoming { get; private set; }

        public int MaxPackSize => 128;
        public uint IntervalMs => 50;

        public SandboxToolAction Action;
        public int Cell;
        public int DistanceFromOrigin;
        public Vector3 Position;

        public int ElementIndex;
        public int DiseaseCount;
        public int MoraleAdjustment;
        public float Mass;
        public float Temperature;
        public float TemperatureAdditive;
        public float StressAdditive;
        public string DiseaseId = string.Empty;
        public string EntityId = string.Empty;
        public string StoryId = string.Empty;

        public static SandboxToolPacket Capture(
            SandboxToolAction action,
            int cell,
            int distanceFromOrigin = 0,
            Vector3 position = default)
        {
            var packet = new SandboxToolPacket
            {
                Action = action,
                Cell = cell,
                DistanceFromOrigin = distanceFromOrigin,
                Position = position
            };

            SandboxSettings settings = SandboxToolParameterMenu.instance?.settings;
            if (settings == null)
                return packet;

            packet.ElementIndex = settings.GetIntSetting(SandboxSettings.KEY_SELECTED_ELEMENT);
            packet.DiseaseCount = settings.GetIntSetting(SandboxSettings.KEY_DISEASE_COUNT);
            packet.MoraleAdjustment = settings.GetIntSetting(SandboxSettings.KEY_MORALE_ADJUSTMENT);
            packet.Mass = settings.GetFloatSetting(SandboxSettings.KEY_MASS);
            packet.Temperature = settings.GetFloatSetting(SandboxSettings.KEY_TEMPERATURE);
            packet.TemperatureAdditive = settings.GetFloatSetting(SandboxSettings.KEY_TEMPERATURE_ADDITIVE);
            packet.StressAdditive = settings.GetFloatSetting(SandboxSettings.KEY_STRESS_ADDITIVE);
            packet.DiseaseId = settings.GetStringSetting(SandboxSettings.KEY_SELECTED_DISEASE) ?? string.Empty;
            packet.EntityId = settings.GetStringSetting(SandboxSettings.KEY_SELECTED_ENTITY) ?? string.Empty;
            packet.StoryId = settings.GetStringSetting(SandboxSettings.KEY_SELECTED_STORY) ?? string.Empty;
            return packet;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)Action);
            writer.Write(Cell);
            writer.Write(DistanceFromOrigin);
            writer.Write(Position);
            writer.Write(ElementIndex);
            writer.Write(DiseaseCount);
            writer.Write(MoraleAdjustment);
            writer.Write(Mass);
            writer.Write(Temperature);
            writer.Write(TemperatureAdditive);
            writer.Write(StressAdditive);
            writer.Write(DiseaseId ?? string.Empty);
            writer.Write(EntityId ?? string.Empty);
            writer.Write(StoryId ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            Action = (SandboxToolAction)reader.ReadByte();
            Cell = reader.ReadInt32();
            DistanceFromOrigin = reader.ReadInt32();
            Position = reader.ReadVector3();
            ElementIndex = reader.ReadInt32();
            DiseaseCount = reader.ReadInt32();
            MoraleAdjustment = reader.ReadInt32();
            Mass = reader.ReadSingle();
            Temperature = reader.ReadSingle();
            TemperatureAdditive = reader.ReadSingle();
            StressAdditive = reader.ReadSingle();
            DiseaseId = reader.ReadString();
            EntityId = reader.ReadString();
            StoryId = reader.ReadString();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!Grid.IsValidCell(Cell) || SandboxToolParameterMenu.instance?.settings == null)
                return;

            ProcessingIncoming = true;
            try
            {
                ExecuteNativeTool();
            }
            catch (Exception exception)
            {
                DebugConsole.LogWarning($"[SandboxTool] Failed to apply {Action} at cell {Cell}: {exception}");
            }
            finally
            {
                ProcessingIncoming = false;
            }
        }

        private void ExecuteNativeTool()
        {
            switch (Action)
            {
                case SandboxToolAction.Brush:
                    using (ApplySettings(SettingsMask.Material))
                        FindTool(SandboxBrushTool.instance)?.OnPaintCell(Cell, DistanceFromOrigin);
                    break;
                case SandboxToolAction.Sprinkle:
                    using (ApplySettings(SettingsMask.Material))
                        FindTool(SandboxSprinkleTool.instance)?.OnPaintCell(Cell, DistanceFromOrigin);
                    break;
                case SandboxToolAction.Flood:
                    using (ApplySettings(SettingsMask.Material))
                        FindTool(SandboxFloodTool.instance)?.PaintCell(Cell);
                    break;
                case SandboxToolAction.Sample:
                    ApplySampleSettings();
                    break;
                case SandboxToolAction.Heat:
                    using (ApplySettings(SettingsMask.Heat))
                        FindTool(SandboxHeatTool.instance)?.OnPaintCell(Cell, DistanceFromOrigin);
                    break;
                case SandboxToolAction.Stress:
                    using (ApplySettings(SettingsMask.Stress))
                        FindTool(SandboxStressTool.instance)?.OnPaintCell(Cell, DistanceFromOrigin);
                    break;
                case SandboxToolAction.Spawn:
                    using (ApplySettings(SettingsMask.Entity))
                    {
                        SandboxSpawnerTool tool = UnityEngine.Object.FindFirstObjectByType<SandboxSpawnerTool>(
                            FindObjectsInactive.Include);
                        if (tool != null)
                        {
                            int previousCell = tool.currentCell;
                            tool.currentCell = Cell;
                            try { tool.Place(Cell); }
                            finally { tool.currentCell = previousCell; }
                        }
                    }
                    break;
                case SandboxToolAction.Destroy:
                    FindTool(SandboxDestroyerTool.instance)?.OnPaintCell(Cell, DistanceFromOrigin);
                    break;
                case SandboxToolAction.Reveal:
                    FindTool(SandboxFOWTool.instance)?.OnPaintCell(Cell, DistanceFromOrigin);
                    break;
                case SandboxToolAction.ClearFloor:
                    FindTool(SandboxClearFloorTool.instance)?.OnPaintCell(Cell, DistanceFromOrigin);
                    break;
                case SandboxToolAction.CritterRemoval:
                    FindTool(SandboxCritterTool.instance)?.OnPaintCell(Cell, DistanceFromOrigin);
                    break;
                case SandboxToolAction.StoryTrait:
                    using (ApplySettings(SettingsMask.Story))
                    {
                        SandboxStoryTraitTool tool = UnityEngine.Object.FindFirstObjectByType<SandboxStoryTraitTool>(
                            FindObjectsInactive.Include);
                        tool?.OnLeftClickDown(Position);
                    }
                    break;
            }
        }

        private static T FindTool<T>(T instance) where T : UnityEngine.Object
        {
            return instance != null
                ? instance
                : UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        }

        private void ApplySampleSettings()
        {
            SandboxSettings settings = SandboxToolParameterMenu.instance.settings;
            settings.SetIntSetting(SandboxSettings.KEY_SELECTED_ELEMENT, ElementIndex);
            settings.SetIntSetting(SandboxSettings.KEY_DISEASE_COUNT, DiseaseCount);
            settings.SetFloatSetting(SandboxSettings.KEY_MASS, Mass);
            settings.SetFloatSetting(SandboxSettings.KEY_TEMPERATURE, Temperature);
            settings.SetStringSetting(SandboxSettings.KEY_SELECTED_DISEASE, DiseaseId);
            SandboxToolParameterMenu.instance.RefreshDisplay();
        }

        private IDisposable ApplySettings(SettingsMask mask)
        {
            return new SettingsScope(SandboxToolParameterMenu.instance.settings, this, mask);
        }

        [Flags]
        private enum SettingsMask
        {
            None = 0,
            Material = 1,
            Heat = 2,
            Stress = 4,
            Entity = 8,
            Story = 16
        }

        private sealed class SettingsScope : IDisposable
        {
            private readonly SandboxSettings _settings;
            private readonly SettingsMask _mask;
            private readonly int _elementIndex;
            private readonly int _diseaseCount;
            private readonly int _moraleAdjustment;
            private readonly float _mass;
            private readonly float _temperature;
            private readonly float _temperatureAdditive;
            private readonly float _stressAdditive;
            private readonly string _diseaseId;
            private readonly string _entityId;
            private readonly string _storyId;

            public SettingsScope(SandboxSettings settings, SandboxToolPacket packet, SettingsMask mask)
            {
                _settings = settings;
                _mask = mask;

                if ((mask & SettingsMask.Material) != 0)
                {
                    _elementIndex = settings.GetIntSetting(SandboxSettings.KEY_SELECTED_ELEMENT);
                    _diseaseCount = settings.GetIntSetting(SandboxSettings.KEY_DISEASE_COUNT);
                    _mass = settings.GetFloatSetting(SandboxSettings.KEY_MASS);
                    _temperature = settings.GetFloatSetting(SandboxSettings.KEY_TEMPERATURE);
                    _diseaseId = settings.GetStringSetting(SandboxSettings.KEY_SELECTED_DISEASE);
                    settings.SetIntSetting(SandboxSettings.KEY_SELECTED_ELEMENT, packet.ElementIndex);
                    settings.SetIntSetting(SandboxSettings.KEY_DISEASE_COUNT, packet.DiseaseCount);
                    settings.SetFloatSetting(SandboxSettings.KEY_MASS, packet.Mass);
                    settings.SetFloatSetting(SandboxSettings.KEY_TEMPERATURE, packet.Temperature);
                    settings.SetStringSetting(SandboxSettings.KEY_SELECTED_DISEASE, packet.DiseaseId);
                }
                if ((mask & SettingsMask.Heat) != 0)
                {
                    _temperatureAdditive = settings.GetFloatSetting(SandboxSettings.KEY_TEMPERATURE_ADDITIVE);
                    settings.SetFloatSetting(SandboxSettings.KEY_TEMPERATURE_ADDITIVE, packet.TemperatureAdditive);
                }
                if ((mask & SettingsMask.Stress) != 0)
                {
                    _stressAdditive = settings.GetFloatSetting(SandboxSettings.KEY_STRESS_ADDITIVE);
                    _moraleAdjustment = settings.GetIntSetting(SandboxSettings.KEY_MORALE_ADJUSTMENT);
                    settings.SetFloatSetting(SandboxSettings.KEY_STRESS_ADDITIVE, packet.StressAdditive);
                    settings.SetIntSetting(SandboxSettings.KEY_MORALE_ADJUSTMENT, packet.MoraleAdjustment);
                }
                if ((mask & SettingsMask.Entity) != 0)
                {
                    _entityId = settings.GetStringSetting(SandboxSettings.KEY_SELECTED_ENTITY);
                    settings.SetStringSetting(SandboxSettings.KEY_SELECTED_ENTITY, packet.EntityId);
                }
                if ((mask & SettingsMask.Story) != 0)
                {
                    _storyId = settings.GetStringSetting(SandboxSettings.KEY_SELECTED_STORY);
                    settings.SetStringSetting(SandboxSettings.KEY_SELECTED_STORY, packet.StoryId);
                }
            }

            public void Dispose()
            {
                if ((_mask & SettingsMask.Material) != 0)
                {
                    _settings.SetIntSetting(SandboxSettings.KEY_SELECTED_ELEMENT, _elementIndex);
                    _settings.SetIntSetting(SandboxSettings.KEY_DISEASE_COUNT, _diseaseCount);
                    _settings.SetFloatSetting(SandboxSettings.KEY_MASS, _mass);
                    _settings.SetFloatSetting(SandboxSettings.KEY_TEMPERATURE, _temperature);
                    _settings.SetStringSetting(SandboxSettings.KEY_SELECTED_DISEASE, _diseaseId);
                }
                if ((_mask & SettingsMask.Heat) != 0)
                    _settings.SetFloatSetting(SandboxSettings.KEY_TEMPERATURE_ADDITIVE, _temperatureAdditive);
                if ((_mask & SettingsMask.Stress) != 0)
                {
                    _settings.SetFloatSetting(SandboxSettings.KEY_STRESS_ADDITIVE, _stressAdditive);
                    _settings.SetIntSetting(SandboxSettings.KEY_MORALE_ADJUSTMENT, _moraleAdjustment);
                }
                if ((_mask & SettingsMask.Entity) != 0)
                    _settings.SetStringSetting(SandboxSettings.KEY_SELECTED_ENTITY, _entityId);
                if ((_mask & SettingsMask.Story) != 0)
                    _settings.SetStringSetting(SandboxSettings.KEY_SELECTED_STORY, _storyId);
            }
        }
    }
}
