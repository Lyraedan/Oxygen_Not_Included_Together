using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World.Handlers
{
    public class ToggleableHandler : IBuildingConfigHandler
    {

        static readonly AccessTools.FieldRef<Toggleable, List<KeyValuePair<IToggleHandler, Chore>>> TargetsRef =
            AccessTools.FieldRefAccess<Toggleable, List<KeyValuePair<IToggleHandler, Chore>>>("targets");

        private static readonly int[] _hashes = new int[]
        {
            NetworkingHash.ForConfigKey("QueueToggleable"),
            NetworkingHash.ForConfigKey("ToggleableChange"),
        };

        public int[] SupportedConfigHashes => _hashes;

        public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
        {
            using var _ = Profiler.Scope();

            if (go == null)
            {
                //DebugConsole.LogError($"[ToggleableHandler] HUGE Nope!");
                return false;
            }

            Toggleable toggleable = go.GetComponent<Toggleable>();

            if (toggleable == null) {
                //DebugConsole.LogError($"[ToggleableHandler] Big Nope!");
                return false;
            }

			if (packet.ConfigType != BuildingConfigType.Boolean
			    || !BuildingConfigPacket.IsBooleanValue(packet.Value))
				return false;

            var targets = TargetsRef(toggleable);
			int targetIndex = packet.SliderIndex;

			if (targetIndex < 0 || targetIndex >= targets.Count || targets[targetIndex].Key == null)
            {
                //DebugConsole.LogError($"[ToggleableHandler] Nope!");
                return false;
            }

            //DebugConsole.Log($"[ToggleableHandler] Handling Toggleable Change on {go.name} with Index {targetIndex}");

            bool targetState = packet.Value > 0.5f;

            if (packet.ConfigHash == NetworkingHash.ForConfigKey("QueueToggleable"))
            {
                //DebugConsole.Log($"[ToggleableHandler] Queue Toggleable Change current={toggleable.IsToggleQueued(targetIndex)} new={targetState}");

                // Check if we are already in our target state
                if (targetState != toggleable.IsToggleQueued(targetIndex)) toggleable.Toggle(targetIndex);
				packet.Value = toggleable.IsToggleQueued(targetIndex) ? 1f : 0f;
                return true;
            }
            else if (packet.ConfigHash == NetworkingHash.ForConfigKey("ToggleableChange"))
            {
                IToggleHandler handler = targets[targetIndex].Key;
                //DebugConsole.Log($"[ToggleableHandler] Changing Toggleable State current={handler.IsHandlerOn()} new={targetState}");

                // Check if we are already in our target state
                if (targetState != handler.IsHandlerOn())
                {
                    targets[targetIndex] = new KeyValuePair<IToggleHandler, Chore>(handler, null);
                    handler.HandleToggle();
                    toggleable.GetComponent<KSelectable>().RemoveStatusItem(Db.Get().BuildingStatusItems.PendingSwitchToggle);
                }
				packet.Value = handler.IsHandlerOn() ? 1f : 0f;
                return true;
            }

            return false;
        }
    }
}
