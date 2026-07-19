using UnityEngine;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Patches.World.SideScreen;
using Shared;
using Shared.Profiling;
using Database;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles miscellaneous buildings that don't fit into other categories.
	/// Includes: LogicSwitch, LogicCounter, LimitValve, ManualGenerator, BottleEmptier,
	/// Checkbox controls, and other one-off handlers.
	/// </summary>
	public class MiscBuildingHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			// LogicSwitch
			NetworkingHash.ForConfigKey("LogicSwitchState"),
			NetworkingHash.ForConfigKey("LogicState"), // Alias for backwards compatibility
			// LogicCounter
			NetworkingHash.ForConfigKey("CounterMaxCount"),
			NetworkingHash.ForConfigKey("CounterAdvancedMode"),
			NetworkingHash.ForConfigKey("CounterResetAtMax"),
			NetworkingHash.ForConfigKey("CounterReset"),
			// CritterSensor
			NetworkingHash.ForConfigKey("CritterSensorCountCritters"),
			NetworkingHash.ForConfigKey("CritterSensorCountEggs"),
			NetworkingHash.ForConfigKey("CritterCountCritters"),
			NetworkingHash.ForConfigKey("CritterCountEggs"),
			// LimitValve (both old and new hash names)
			NetworkingHash.ForConfigKey("LimitValveLimit"),
			NetworkingHash.ForConfigKey("LimitValve"),
			// ManualGenerator
			NetworkingHash.ForConfigKey("ManualGeneratorThreshold"),
			// BottleEmptier
			NetworkingHash.ForConfigKey("BottleEmptierAllowManualPump"),
			// Checkbox control
			NetworkingHash.ForConfigKey("Checkbox"),
			// Automatable (both old and new hash names)
			NetworkingHash.ForConfigKey("AutomatableAutomationOnly"),
			NetworkingHash.ForConfigKey("AutomationOnly"),
			// DirectionControl (both names)
			NetworkingHash.ForConfigKey("LoopConveyorDirection"),
			NetworkingHash.ForConfigKey("DirectionControl"),
			// Valve rate
			NetworkingHash.ForConfigKey("Rate"),
			// FoodStorage
			NetworkingHash.ForConfigKey("FoodStorageSpicedFoodOnly"),
			// IceMachine
			NetworkingHash.ForConfigKey("IceMachineElement"),
			// Artable (paintings, sculptures)
			NetworkingHash.ForConfigKey("ArtableState"),
			NetworkingHash.ForConfigKey("ArtableDefault"),
			// SuitLocker
			NetworkingHash.ForConfigKey("SuitLockerRequestSuit"),
			NetworkingHash.ForConfigKey("SuitLockerNoSuit"),
			NetworkingHash.ForConfigKey("SuitLockerDropSuit"),
			// Gantry
			NetworkingHash.ForConfigKey("GantryToggle"),
			// SuitMarker (checkpoint clearance)
			NetworkingHash.ForConfigKey("SuitMarkerTraversal"),
			// FlatTagFilterable (meteor type selection)
			NetworkingHash.ForConfigKey("FlatTagFilter"),
			// Configurable consumer and sealed POI door actions
			SpiceGrinderWorkable_SetSelectedOption_Patch.ConfigHash,
			Door_OrderUnseal_Patch.ConfigHash,
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;
			int logicSwitchHash = NetworkingHash.ForConfigKey("LogicSwitchState");
			int logicStateHash = NetworkingHash.ForConfigKey("LogicState");

			if (hash == SpiceGrinderWorkable_SetSelectedOption_Patch.ConfigHash)
			{
				var grinder = go.GetComponent<SpiceGrinderWorkable>();
				if (grinder == null || packet.ConfigType != BuildingConfigType.String ||
				    string.IsNullOrEmpty(packet.StringValue))
					return false;

				string requestedId = packet.StringValue;
				string currentId = grinder.GetSelectedOption()?.GetID().Name;
				if (!SpiceGrinderWorkable_SetSelectedOption_Patch.ShouldApplyOption(currentId, requestedId))
					return true;

				IConfigurableConsumerOption[] options = grinder.GetSettingOptions();
				if (options == null)
					return false;

				var optionIds = new string[options.Length];
				for (int i = 0; i < options.Length; i++)
					optionIds[i] = options[i]?.GetID().Name;

				int optionIndex = SpiceGrinderWorkable_SetSelectedOption_Patch.FindOptionIndex(optionIds, requestedId);
				if (optionIndex < 0)
					return false;

				grinder.SetSelectedOption(options[optionIndex]);
				return true;
			}

			if (hash == Door_OrderUnseal_Patch.ConfigHash)
			{
				var door = go.GetComponent<Door>();
				if (door == null || packet.ConfigType != BuildingConfigType.Boolean || packet.Value != 1f)
					return false;

				if (Door_OrderUnseal_Patch.ShouldOrderUnseal(door))
					door.OrderUnseal();

				return true;
			}

			// LogicSwitch
			var logicSwitch = go.GetComponent<LogicSwitch>();
			//DebugConsole.Log($"[MiscBuildingHandler] Checking LogicSwitch: component={logicSwitch != null}, hash={hash}, expected={logicSwitchHash}");

			if (hash == logicSwitchHash || hash == logicStateHash)
			{
				if (packet.ConfigType != BuildingConfigType.Boolean
				    || !BuildingConfigPacket.IsBooleanValue(packet.Value))
					return false;
				if (logicSwitch != null)
				{
					bool targetState = packet.Value > 0.5f;
					// Use Traverse to call SetState since it may be private/protected
					logicSwitch.SetState(targetState);
					//DebugConsole.Log($"[MiscBuildingHandler] Set LogicSwitch state={targetState} on {go.name}");
					return true;
				}
				else
				{
					DebugConsole.LogWarning($"[MiscBuildingHandler] LogicSwitch component not found on {go.name}, trying IPlayerControlledToggle");
					// Try IPlayerControlledToggle interface instead
					var toggle = go.GetComponent<IPlayerControlledToggle>();
					if (toggle != null)
					{
						bool targetState = packet.Value > 0.5f;
						if (toggle.ToggledOn() != targetState)
						{
							toggle.ToggledByPlayer();
						}
						DebugConsole.Log($"[MiscBuildingHandler] Set IPlayerControlledToggle state={targetState} on {go.name}");
						return true;
					}
				}
			}

			// LogicCounter
			var counter = go.GetComponent<LogicCounter>();
			if (counter != null)
			{
				if (hash == NetworkingHash.ForConfigKey("CounterMaxCount"))
				{
					if (packet.ConfigType != BuildingConfigType.Float
					    || !BuildingConfigPacket.IsIntegralValue(packet.Value)
					    || !BuildingConfigPacket.IsInRange(packet.Value, 1f, 9999f))
						return false;
					counter.maxCount = (int)packet.Value;
					counter.SetCounterState();
					//DebugConsole.Log($"[MiscBuildingHandler] Set counter maxCount={counter.maxCount} on {go.name}");
					return true;
				}
				if (hash == NetworkingHash.ForConfigKey("CounterAdvancedMode"))
				{
					if (!IsBooleanPacket(packet)) return false;
					counter.advancedMode = packet.Value > 0.5f;
					counter.SetCounterState();
					//DebugConsole.Log($"[MiscBuildingHandler] Set counter advancedMode={counter.advancedMode} on {go.name}");
					return true;
				}
				if (hash == NetworkingHash.ForConfigKey("CounterResetAtMax"))
				{
					if (!IsBooleanPacket(packet)) return false;
					counter.resetCountAtMax = packet.Value > 0.5f;
					counter.SetCounterState();
					//DebugConsole.Log($"[MiscBuildingHandler] Set counter resetCountAtMax={counter.resetCountAtMax} on {go.name}");
					return true;
				}
				if (hash == NetworkingHash.ForConfigKey("CounterReset"))
				{
					if (!BuildingConfigPacket.IsBooleanValue(packet.Value)) return false;
					counter.ResetCounter();
					//DebugConsole.Log($"[MiscBuildingHandler] Reset counter on {go.name}");
					return true;
				}
			}

			// CritterSensor
			var critterSensor = go.GetComponent<LogicCritterCountSensor>();
			if (critterSensor != null)
			{
				if (hash == NetworkingHash.ForConfigKey("CritterSensorCountCritters") || hash == NetworkingHash.ForConfigKey("CritterCountCritters"))
				{
					if (!IsBooleanPacket(packet)) return false;
					critterSensor.countCritters = packet.Value > 0.5f;
					//DebugConsole.Log($"[MiscBuildingHandler] Set countCritters={critterSensor.countCritters} on {go.name}");
					return true;
				}
				if (hash == NetworkingHash.ForConfigKey("CritterSensorCountEggs") || hash == NetworkingHash.ForConfigKey("CritterCountEggs"))
				{
					if (!IsBooleanPacket(packet)) return false;
					critterSensor.countEggs = packet.Value > 0.5f;
					//DebugConsole.Log($"[MiscBuildingHandler] Set countEggs={critterSensor.countEggs} on {go.name}");
					return true;
				}
			}

			// LimitValve
			var limitValve = go.GetComponent<LimitValve>();
			if (limitValve != null && (hash == NetworkingHash.ForConfigKey("LimitValveLimit") || hash == NetworkingHash.ForConfigKey("LimitValve")))
			{
				if (packet.ConfigType != BuildingConfigType.Float
				    || !BuildingConfigPacket.IsInRange(packet.Value, 0f, 1_000_000_000f))
					return false;
				limitValve.Limit = packet.Value;
				//DebugConsole.Log($"[MiscBuildingHandler] Set LimitValve Limit={packet.Value} on {go.name}");
				return true;
			}

			// ManualGenerator
			var manualGenerator = go.GetComponent<ManualGenerator>();
			if (manualGenerator != null && hash == NetworkingHash.ForConfigKey("ManualGeneratorThreshold"))
			{
				if (packet.ConfigType != BuildingConfigType.Float
				    || !BuildingConfigPacket.IsInRange(packet.Value, 0f, 1f))
					return false;
				Traverse.Create(manualGenerator).Field("refillPercent").SetValue(packet.Value);
				//DebugConsole.Log($"[MiscBuildingHandler] Set ManualGenerator refillPercent={packet.Value} on {go.name}");
				return true;
			}

			// BottleEmptier
			var bottleEmptier = go.GetComponent<BottleEmptier>();
			if (bottleEmptier != null && hash == NetworkingHash.ForConfigKey("BottleEmptierAllowManualPump"))
			{
				if (!IsBooleanPacket(packet)) return false;
				bottleEmptier.allowManualPumpingStationFetching = packet.Value > 0.5f;
				//DebugConsole.Log($"[MiscBuildingHandler] Set BottleEmptier allowManualPump={packet.Value > 0.5f} on {go.name}");
				return true;
			}

			// ICheckboxControl
			if (hash == NetworkingHash.ForConfigKey("Checkbox"))
			{
				var checkbox = go.GetComponent<ICheckboxControl>();
				if (checkbox != null)
				{
					if (!IsBooleanPacket(packet)) return false;
					checkbox.SetCheckboxValue(packet.Value > 0.5f);
					//DebugConsole.Log($"[MiscBuildingHandler] Set Checkbox={packet.Value > 0.5f} on {go.name}");
					return true;
				}
			}

			// Automatable
			var automatable = go.GetComponent<Automatable>();
			if (automatable != null && (hash == NetworkingHash.ForConfigKey("AutomatableAutomationOnly") || hash == NetworkingHash.ForConfigKey("AutomationOnly")))
			{
				if (!IsBooleanPacket(packet)) return false;
				automatable.SetAutomationOnly(packet.Value > 0.5f);
				//DebugConsole.Log($"[MiscBuildingHandler] Set AutomationOnly={packet.Value > 0.5f} on {go.name}");
				return true;
			}

			// DirectionControl (Loop Conveyor, Wash Basin, etc.)
			var directionControl = go.GetComponent<DirectionControl>();
			if (directionControl != null && (hash == NetworkingHash.ForConfigKey("LoopConveyorDirection") || hash == NetworkingHash.ForConfigKey("DirectionControl")))
			{
				if (packet.ConfigType != BuildingConfigType.Float
				    || !BuildingConfigPacket.IsIntegralValue(packet.Value)
				    || !System.Enum.IsDefined(
					    typeof(WorkableReactable.AllowedDirection), (int)packet.Value))
					return false;
				directionControl.SetAllowedDirection((WorkableReactable.AllowedDirection)(int)packet.Value);
				//DebugConsole.Log($"[MiscBuildingHandler] Set Direction={(WorkableReactable.AllowedDirection)(int)packet.Value} on {go.name}");
				return true;
			}

			// Valve rate
			var valve = go.GetComponent<Valve>();
			if (valve != null && hash == NetworkingHash.ForConfigKey("Rate"))
			{
				if (packet.ConfigType != BuildingConfigType.Float
				    || !BuildingConfigPacket.IsInRange(packet.Value, 0f, 1_000_000_000f))
					return false;
				Traverse.Create(valve).Method("ChangeFlow", packet.Value).GetValue();
				//DebugConsole.Log($"[MiscBuildingHandler] Set Valve Rate={packet.Value} on {go.name}");
				return true;
			}

			// FoodStorage (Refrigerator spiced food toggle)
			var foodStorage = go.GetComponent<FoodStorage>();
			if (foodStorage != null && hash == NetworkingHash.ForConfigKey("FoodStorageSpicedFoodOnly"))
			{
				if (!IsBooleanPacket(packet)) return false;
				foodStorage.SpicedFoodOnly = packet.Value > 0.5f;
				//DebugConsole.Log($"[MiscBuildingHandler] Set SpicedFoodOnly={packet.Value > 0.5f} on {go.name}");
				return true;
			}

			// IceMachine element selection
			var iceMachine = go.GetComponent<IceMachine>();
			if (iceMachine != null && hash == NetworkingHash.ForConfigKey("IceMachineElement"))
			{
				if (packet.ConfigType == BuildingConfigType.String && !string.IsNullOrEmpty(packet.StringValue))
				{
					Tag tag = new Tag(packet.StringValue);
					FewOptionSideScreen.IFewOptionSideScreen.Option selected = default;
					bool found = false;
					foreach (FewOptionSideScreen.IFewOptionSideScreen.Option option in iceMachine.GetOptions())
					{
						if (option.tag == tag)
						{
							selected = option;
							found = true;
							break;
						}
					}
					if (!found)
						return false;
					iceMachine.OnOptionSelected(selected);
					packet.StringValue = iceMachine.GetSelectedOption().Name;
					//DebugConsole.Log($"[MiscBuildingHandler] Set IceMachine element={tag} on {go.name}");
					return true;
				}
			}

			// Artable (paintings, sculptures) - select specific art style
			var artable = go.GetComponent<Artable>();
			if (artable != null)
			{
				if (hash == NetworkingHash.ForConfigKey("ArtableState"))
				{
					if (packet.ConfigType == BuildingConfigType.String
					    && !string.IsNullOrEmpty(packet.StringValue))
					{
						bool allowed = false;
						foreach (ArtableStage stage in Db.GetArtableStages().GetPrefabStages(go.PrefabID()))
						{
							if (stage != null && stage.id == packet.StringValue && stage.IsUnlocked())
							{
								allowed = true;
								break;
							}
						}
						if (!allowed) return false;
						artable.SetUserChosenTargetState(packet.StringValue);
						//DebugConsole.Log($"[MiscBuildingHandler] Set Artable state={packet.StringValue} on {go.name}");
						return true;
					}
				}
				if (hash == NetworkingHash.ForConfigKey("ArtableDefault"))
				{
					if (!BuildingConfigPacket.IsBooleanValue(packet.Value)) return false;
					artable.SetDefault();
					//DebugConsole.Log($"[MiscBuildingHandler] Reset Artable to default on {go.name}");
					return true;
				}
			}

			// SuitLocker
			var suitLocker = go.GetComponent<SuitLocker>();
			if (suitLocker != null)
			{
				if (hash == NetworkingHash.ForConfigKey("SuitLockerRequestSuit"))
				{
					if (!IsBooleanPacket(packet) || packet.Value != 1f) return false;
					suitLocker.ConfigRequestSuit();
					//DebugConsole.Log($"[MiscBuildingHandler] ConfigRequestSuit on {go.name}");
					return true;
				}
				if (hash == NetworkingHash.ForConfigKey("SuitLockerNoSuit"))
				{
					if (!IsBooleanPacket(packet) || packet.Value != 0f) return false;
					suitLocker.ConfigNoSuit();
					//DebugConsole.Log($"[MiscBuildingHandler] ConfigNoSuit on {go.name}");
					return true;
				}
				if (hash == NetworkingHash.ForConfigKey("SuitLockerDropSuit"))
				{
					if (!IsBooleanPacket(packet) || packet.Value != 1f) return false;
					suitLocker.DropSuit();
					//DebugConsole.Log($"[MiscBuildingHandler] DropSuit on {go.name}");
					return true;
				}
			}

			// Gantry
			if (hash == NetworkingHash.ForConfigKey("GantryToggle"))
			{
				var gantry = go.GetComponent<Gantry>();
				if (gantry != null)
				{
					if (!IsBooleanPacket(packet)) return false;
					bool targetState = packet.Value > 0.5f;
					if (gantry.IsSwitchedOn != targetState)
						gantry.Toggle();
					return true;
				}
			}

			// SuitMarker (checkpoint clearance - traverse only when room available)
			var suitMarker = go.GetComponent<SuitMarker>();
			if (suitMarker != null && hash == NetworkingHash.ForConfigKey("SuitMarkerTraversal"))
			{
				if (!IsBooleanPacket(packet)) return false;
				if (packet.Value > 0.5f)
				{
					// Use Traverse to call private method
					Traverse.Create(suitMarker).Method("OnEnableTraverseIfUnequipAvailable").GetValue();
					//DebugConsole.Log($"[MiscBuildingHandler] SuitMarker clearance=OnlyWhenRoomAvailable on {go.name}");
				}
				else
				{
					Traverse.Create(suitMarker).Method("OnDisableTraverseIfUnequipAvailable").GetValue();
					//DebugConsole.Log($"[MiscBuildingHandler] SuitMarker clearance=Always on {go.name}");
				}
				return true;
			}

			// FlatTagFilterable (meteor type selection, etc.)
			var flatTagFilter = go.GetComponent<FlatTagFilterable>();
			if (flatTagFilter != null)
			{
				if (hash == NetworkingHash.ForConfigKey("FlatTagFilter"))
				{
					if (packet.ConfigType != BuildingConfigType.String
					    || string.IsNullOrEmpty(packet.StringValue)
					    || !BuildingConfigPacket.IsBooleanValue(packet.Value))
						return false;
					Tag tag = new Tag(packet.StringValue);
					if (!flatTagFilter.tagOptions.Contains(tag))
						return false;
					bool shouldBeSelected = packet.Value > 0.5f;
					bool isSelected = flatTagFilter.selectedTags.Contains(tag);

					// Only toggle if state doesn't match
					if (isSelected != shouldBeSelected)
					{
						flatTagFilter.ToggleTag(tag);
					}
					//DebugConsole.Log($"[MiscBuildingHandler] FlatTagFilter tag={tag.Name}, selected={shouldBeSelected} on {go.name}");
					return true;
				}
			}

			return false;
		}

		private static bool IsBooleanPacket(BuildingConfigPacket packet)
			=> packet.ConfigType == BuildingConfigType.Boolean
			   && BuildingConfigPacket.IsBooleanValue(packet.Value);
	}
}
