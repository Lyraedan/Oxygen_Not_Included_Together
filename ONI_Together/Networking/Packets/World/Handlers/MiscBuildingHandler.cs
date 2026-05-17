using UnityEngine;
using HarmonyLib;
using ONI_Together.DebugTools;
using Shared.Profiling;

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
			"LogicSwitchState".GetHashCode(),
			"LogicState".GetHashCode(), // Alias for backwards compatibility
			// LogicCounter
			"CounterMaxCount".GetHashCode(),
			"CounterAdvancedMode".GetHashCode(),
			"CounterResetAtMax".GetHashCode(),
			"CounterReset".GetHashCode(),
			// CritterSensor
			"CritterSensorCountCritters".GetHashCode(),
			"CritterSensorCountEggs".GetHashCode(),
			"CritterCountCritters".GetHashCode(),
			"CritterCountEggs".GetHashCode(),
			// LimitValve (both old and new hash names)
			"LimitValveLimit".GetHashCode(),
			"LimitValve".GetHashCode(),
			// ManualGenerator
			"ManualGeneratorThreshold".GetHashCode(),
			// BottleEmptier
			"BottleEmptierAllowManualPump".GetHashCode(),
			// Checkbox control
			"Checkbox".GetHashCode(),
			// Automatable (both old and new hash names)
			"AutomatableAutomationOnly".GetHashCode(),
			"AutomationOnly".GetHashCode(),
			// DirectionControl (both names)
			"LoopConveyorDirection".GetHashCode(),
			"DirectionControl".GetHashCode(),
			// Valve rate
			"Rate".GetHashCode(),
			// FoodStorage
			"FoodStorageSpicedFoodOnly".GetHashCode(),
			// IceMachine
			"IceMachineElement".GetHashCode(),
			// Artable (paintings, sculptures)
			"ArtableState".GetHashCode(),
			"ArtableDefault".GetHashCode(),
			// SuitLocker
			"SuitLockerRequestSuit".GetHashCode(),
			"SuitLockerNoSuit".GetHashCode(),
			"SuitLockerDropSuit".GetHashCode(),
			// RemoteWorkTerminal (DLC3)
			"RemoteWorkTerminalDock".GetHashCode(),
			// SuitMarker (checkpoint clearance)
			"SuitMarkerTraversal".GetHashCode(),
			// FlatTagFilterable (meteor type selection)
			"FlatTagFilter".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;
			int logicSwitchHash = "LogicSwitchState".GetHashCode();
			int logicStateHash = "LogicState".GetHashCode();

			// LogicSwitch
			var logicSwitch = go.GetComponent<LogicSwitch>();
			DebugConsole.Log($"[MiscBuildingHandler] Checking LogicSwitch: component={logicSwitch != null}, hash={hash}, expected={logicSwitchHash}");

			if (hash == logicSwitchHash || hash == logicStateHash)
			{
				if (logicSwitch != null)
				{
					bool targetState = packet.Value > 0.5f;
					// Use Traverse to call SetState since it may be private/protected
					Traverse.Create(logicSwitch).Method("SetState", new[] { typeof(bool) }).GetValue(targetState);
					DebugConsole.Log($"[MiscBuildingHandler] Set LogicSwitch state={targetState} on {go.name}");
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
				if (hash == "CounterMaxCount".GetHashCode())
				{
					counter.maxCount = (int)packet.Value;
					counter.SetCounterState();
					DebugConsole.Log($"[MiscBuildingHandler] Set counter maxCount={counter.maxCount} on {go.name}");
					return true;
				}
				if (hash == "CounterAdvancedMode".GetHashCode())
				{
					counter.advancedMode = packet.Value > 0.5f;
					counter.SetCounterState();
					DebugConsole.Log($"[MiscBuildingHandler] Set counter advancedMode={counter.advancedMode} on {go.name}");
					return true;
				}
				if (hash == "CounterResetAtMax".GetHashCode())
				{
					counter.resetCountAtMax = packet.Value > 0.5f;
					counter.SetCounterState();
					DebugConsole.Log($"[MiscBuildingHandler] Set counter resetCountAtMax={counter.resetCountAtMax} on {go.name}");
					return true;
				}
				if (hash == "CounterReset".GetHashCode())
				{
					counter.ResetCounter();
					DebugConsole.Log($"[MiscBuildingHandler] Reset counter on {go.name}");
					return true;
				}
			}

			// CritterSensor
			var critterSensor = go.GetComponent<LogicCritterCountSensor>();
			if (critterSensor != null)
			{
				if (hash == "CritterSensorCountCritters".GetHashCode() || hash == "CritterCountCritters".GetHashCode())
				{
					critterSensor.countCritters = packet.Value > 0.5f;
					DebugConsole.Log($"[MiscBuildingHandler] Set countCritters={critterSensor.countCritters} on {go.name}");
					return true;
				}
				if (hash == "CritterSensorCountEggs".GetHashCode() || hash == "CritterCountEggs".GetHashCode())
				{
					critterSensor.countEggs = packet.Value > 0.5f;
					DebugConsole.Log($"[MiscBuildingHandler] Set countEggs={critterSensor.countEggs} on {go.name}");
					return true;
				}
			}

			// LimitValve
			var limitValve = go.GetComponent<LimitValve>();
			if (limitValve != null && (hash == "LimitValveLimit".GetHashCode() || hash == "LimitValve".GetHashCode()))
			{
				limitValve.Limit = packet.Value;
				DebugConsole.Log($"[MiscBuildingHandler] Set LimitValve Limit={packet.Value} on {go.name}");
				return true;
			}

			// ManualGenerator
			var manualGenerator = go.GetComponent<ManualGenerator>();
			if (manualGenerator != null && hash == "ManualGeneratorThreshold".GetHashCode())
			{
				Traverse.Create(manualGenerator).Field("refillPercent").SetValue(packet.Value);
				DebugConsole.Log($"[MiscBuildingHandler] Set ManualGenerator refillPercent={packet.Value} on {go.name}");
				return true;
			}

			// BottleEmptier
			var bottleEmptier = go.GetComponent<BottleEmptier>();
			if (bottleEmptier != null && hash == "BottleEmptierAllowManualPump".GetHashCode())
			{
				bottleEmptier.allowManualPumpingStationFetching = packet.Value > 0.5f;
				DebugConsole.Log($"[MiscBuildingHandler] Set BottleEmptier allowManualPump={packet.Value > 0.5f} on {go.name}");
				return true;
			}

			// ICheckboxControl
			if (hash == "Checkbox".GetHashCode())
			{
				var checkbox = go.GetComponent<ICheckboxControl>();
				if (checkbox != null)
				{
					checkbox.SetCheckboxValue(packet.Value > 0.5f);
					DebugConsole.Log($"[MiscBuildingHandler] Set Checkbox={packet.Value > 0.5f} on {go.name}");
					return true;
				}
			}

			// Automatable
			var automatable = go.GetComponent<Automatable>();
			if (automatable != null && (hash == "AutomatableAutomationOnly".GetHashCode() || hash == "AutomationOnly".GetHashCode()))
			{
				automatable.SetAutomationOnly(packet.Value > 0.5f);
				DebugConsole.Log($"[MiscBuildingHandler] Set AutomationOnly={packet.Value > 0.5f} on {go.name}");
				return true;
			}

			// DirectionControl (Loop Conveyor, Wash Basin, etc.)
			var directionControl = go.GetComponent<DirectionControl>();
			if (directionControl != null && (hash == "LoopConveyorDirection".GetHashCode() || hash == "DirectionControl".GetHashCode()))
			{
				directionControl.SetAllowedDirection((WorkableReactable.AllowedDirection)(int)packet.Value);
				DebugConsole.Log($"[MiscBuildingHandler] Set Direction={(WorkableReactable.AllowedDirection)(int)packet.Value} on {go.name}");
				return true;
			}

			// Valve rate
			var valve = go.GetComponent<Valve>();
			if (valve != null && hash == "Rate".GetHashCode())
			{
				Traverse.Create(valve).Method("ChangeFlow", packet.Value).GetValue();
				DebugConsole.Log($"[MiscBuildingHandler] Set Valve Rate={packet.Value} on {go.name}");
				return true;
			}

			// FoodStorage (Refrigerator spiced food toggle)
			var foodStorage = go.GetComponent<FoodStorage>();
			if (foodStorage != null && hash == "FoodStorageSpicedFoodOnly".GetHashCode())
			{
				foodStorage.SpicedFoodOnly = packet.Value > 0.5f;
				DebugConsole.Log($"[MiscBuildingHandler] Set SpicedFoodOnly={packet.Value > 0.5f} on {go.name}");
				return true;
			}

			// IceMachine element selection
			var iceMachine = go.GetComponent<IceMachine>();
			if (iceMachine != null && hash == "IceMachineElement".GetHashCode())
			{
				if (packet.ConfigType == BuildingConfigType.String && !string.IsNullOrEmpty(packet.StringValue))
				{
					Tag tag = new Tag(packet.StringValue);
					iceMachine.OnOptionSelected(new FewOptionSideScreen.IFewOptionSideScreen.Option(tag, null, null));
					DebugConsole.Log($"[MiscBuildingHandler] Set IceMachine element={tag} on {go.name}");
					return true;
				}
			}

			// Artable (paintings, sculptures) - select specific art style
			var artable = go.GetComponent<Artable>();
			if (artable != null)
			{
				if (hash == "ArtableState".GetHashCode())
				{
					if (!string.IsNullOrEmpty(packet.StringValue))
					{
						artable.SetUserChosenTargetState(packet.StringValue);
						DebugConsole.Log($"[MiscBuildingHandler] Set Artable state={packet.StringValue} on {go.name}");
						return true;
					}
				}
				if (hash == "ArtableDefault".GetHashCode())
				{
					artable.SetDefault();
					DebugConsole.Log($"[MiscBuildingHandler] Reset Artable to default on {go.name}");
					return true;
				}
			}

			// SuitLocker
			var suitLocker = go.GetComponent<SuitLocker>();
			if (suitLocker != null)
			{
				if (hash == "SuitLockerRequestSuit".GetHashCode())
				{
					suitLocker.ConfigRequestSuit();
					DebugConsole.Log($"[MiscBuildingHandler] ConfigRequestSuit on {go.name}");
					return true;
				}
				if (hash == "SuitLockerNoSuit".GetHashCode())
				{
					suitLocker.ConfigNoSuit();
					DebugConsole.Log($"[MiscBuildingHandler] ConfigNoSuit on {go.name}");
					return true;
				}
				if (hash == "SuitLockerDropSuit".GetHashCode())
				{
					suitLocker.DropSuit();
					DebugConsole.Log($"[MiscBuildingHandler] DropSuit on {go.name}");
					return true;
				}
			}

			// RemoteWorkTerminal (DLC3)
			if (hash == "RemoteWorkTerminalDock".GetHashCode())
			{
				var terminal = go.GetComponent<RemoteWorkTerminal>();
				if (terminal != null)
				{
					int dockNetId = packet.SliderIndex;
					RemoteWorkerDock targetDock = null;

					if (dockNetId != -1)
					{
						if (NetworkIdentityRegistry.TryGet(dockNetId, out var dockIdentity) && dockIdentity != null)
						{
							targetDock = dockIdentity.gameObject.GetComponent<RemoteWorkerDock>();
						}
					}

					terminal.FutureDock = targetDock;
					DebugConsole.Log($"[MiscBuildingHandler] Set RemoteWorkTerminal FutureDock={targetDock?.GetProperName() ?? "null"} on {go.name}");
					return true;
				}
			}

			// SuitMarker (checkpoint clearance - traverse only when room available)
			var suitMarker = go.GetComponent<SuitMarker>();
			if (suitMarker != null && hash == "SuitMarkerTraversal".GetHashCode())
			{
				if (packet.Value > 0.5f)
				{
					// Use Traverse to call private method
					Traverse.Create(suitMarker).Method("OnEnableTraverseIfUnequipAvailable").GetValue();
					DebugConsole.Log($"[MiscBuildingHandler] SuitMarker clearance=OnlyWhenRoomAvailable on {go.name}");
				}
				else
				{
					Traverse.Create(suitMarker).Method("OnDisableTraverseIfUnequipAvailable").GetValue();
					DebugConsole.Log($"[MiscBuildingHandler] SuitMarker clearance=Always on {go.name}");
				}
				return true;
			}

			// FlatTagFilterable (meteor type selection, etc.)
			var flatTagFilter = go.GetComponent<FlatTagFilterable>();
			if (flatTagFilter != null)
			{
				if (hash == "FlatTagFilter".GetHashCode())
				{
					Tag tag = new Tag(packet.StringValue);
					bool shouldBeSelected = packet.Value > 0.5f;
					bool isSelected = flatTagFilter.selectedTags.Contains(tag);

					// Only toggle if state doesn't match
					if (isSelected != shouldBeSelected)
					{
						flatTagFilter.ToggleTag(tag);
					}
					DebugConsole.Log($"[MiscBuildingHandler] FlatTagFilter tag={tag.Name}, selected={shouldBeSelected} on {go.name}");
					return true;
				}
			}

			return false;
		}
	}
}
