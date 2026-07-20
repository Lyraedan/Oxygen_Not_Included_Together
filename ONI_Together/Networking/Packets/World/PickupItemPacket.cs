using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using static Storage;
using static Klei.AI.Attribute;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
    /// <summary>
    /// Modified version of GroundItemPickedUpPacket
    /// </summary>
    public class PickupItemPacket : IPacket, IBulkablePacket
    {
        public int NetId;
        public float UnitsTaken;
        public float UnitsRemaining;
        public bool SourceRemains;

        public int MaxPackSize => 500;

        public uint IntervalMs => 250;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
            writer.Write(NetId);
            writer.Write(UnitsTaken);
            writer.Write(UnitsRemaining);
            writer.Write(SourceRemains);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
            NetId = reader.ReadInt32();
            UnitsTaken = reader.ReadSingle();
            UnitsRemaining = reader.ReadSingle();
            SourceRemains = reader.ReadBoolean();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!NetworkIdentityRegistry.TryGetComponent<Pickupable>(NetId, out var pickupable))
                return; // skip

            DisplayFX(pickupable.gameObject);

            if (SourceRemains)
            {
                var primaryElement = pickupable.GetComponent<PrimaryElement>();
                if (primaryElement != null)
                    primaryElement.Units = UnitsRemaining;
            }
            else
            {
                Util.KDestroyGameObject(pickupable.gameObject);
            }
        }

        public void DisplayFX(GameObject go)
        {
            if (PopFXManager.Instance == null)
                return;

            PrimaryElement component = go.GetComponent<PrimaryElement>();
            if (component == null)
                return;

            LocString locString = global::STRINGS.UI.PICKEDUP;
            Transform target_transform = go.transform;
            Vector3 offset = Vector3.zero;

            string text = (Assets.IsTagCountable(go.PrefabID()) ? string.Format(locString, (int)UnitsTaken, go.GetProperName()) : string.Format(locString, GameUtil.GetFormattedMass(UnitsTaken * component.MassPerUnit), go.GetProperName()));
            PopFXManager.Instance.SpawnFX(Def.GetUISprite(go).first, PopFXManager.Instance.sprite_Plus, text, target_transform, offset);
        }
    }
}
