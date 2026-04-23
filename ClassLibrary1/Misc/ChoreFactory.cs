using ONI_MP.DebugTools;
using ONI_MP.Misc;
using STRINGS;
using System.Collections.Generic;
using System.Reflection;
using Shared.Profiling;
using UnityEngine;
using static Chore;

namespace ONI_MP.Misc
{
public static class ChoreFactory
{
	public static Chore Create(string choreTypeId, Precondition.Context context, GameObject dupeGO, Vector3 pos, int cell, string prefabId)
	{
		using var _ = Profiler.Scope();

		var type = Db.Get().ChoreTypes.Get(choreTypeId);
		var consumer = dupeGO.GetComponent<ChoreConsumer>();

		switch (choreTypeId)
		{
			case "Aggressive": return CreateAggressive(consumer);
			case "Attack": return CreateAttack(consumer);
			case "BalloonArtist":
			case "Banshee":
			case "RoboDancer":
			case "Sigh":
			case "UglyCry":
			case "StressEmote": return CreateNamedEmote(choreTypeId, type, consumer);
			case "BeIncapacitated": return CreateBeIncapacitated(consumer);
			case "BeOffline": return CreateBeOffline(consumer);
			case "BingeEat": return CreateBingeEat(consumer);
			case "BionicBedTimeMode": return CreateBionicBedTimeMode(consumer);
			case "BionicGunkSpill": return CreateBionicGunkSpill(consumer);
			case "BionicMassOxygenAbsorb":
			case "BionicAbsorbOxygen":
			case "BionicAbsorbOxygen_Critical": return CreateBionicOxygenAbsorb(consumer, choreTypeId);
			case "DeliverFood": return CreateDeliverFood(consumer);
			case "Die": return CreateDie(dupeGO, consumer);
			case "Disinfect": return CreateWorkChore<Disinfectable>(type, consumer, cell);
			case "DropUnusedInventory": return CreateDropUnusedInventory(consumer);
			case "Eat": return CreateEat(consumer);
			case "Entombed": return CreateEntombed(consumer);
			case "Equip": return CreateEquip(consumer);
			case "FetchArea": return CreateFetchArea(context);
			case "Fetch": return CreateFetch(consumer);
			case "FindAndConsumeOxygenSource": return CreateOxygenSourceChore(consumer);
			case "FixedCapture": return CreateFixedCapture(cell);
			case "Flee": return CreateFlee(dupeGO);
			case "FoodFight": return CreateFoodFight(consumer);
			case "Harvest": return CreateWorkChore<Harvestable>(type, consumer, cell);
			case "IdleChore": return CreateIdle(consumer);
			case "Mingle": return CreateMingle(consumer);
			case "Mop": return CreateWorkChore<Moppable>(type, consumer, cell);
			case "Mourn": return CreateMourn(consumer);
			case "Move": return CreateMove(consumer, dupeGO);
			case "MovePickupable": return CreateMovePickupable(consumer);
			case "MoveToQuarantine": return CreateMoveToQuarantine(consumer);
			case "MoveToSafety": return CreateMoveToSafety(consumer);
			case "Party": return CreateParty(consumer);
			case "Pee": return CreatePee(consumer);
			case "PutOnHat": return CreatePutOnHat(consumer);
			case "Rancher": return CreateRancher(consumer, cell);
			case "ReactEmote": return CreateNamedEmote("ReactEmote", type, consumer);
			case "RecoverBreath": return CreateRecoverBreath(consumer);
			case "RecoverFromCold": return CreateRecoverFromCold(consumer);
			case "RecoverFromHeat": return CreateRecoverFromHeat(consumer);
			case "ReloadElectrobank": return CreateReloadElectrobank(consumer);
			case "Remote": return CreateRemote(consumer);
			case "Repair": return CreateWorkChore<Repairable>(type, consumer, cell);
			case "RescueIncapacitated": return CreateRescueIncapacitated(consumer);
			case "RescueSweepBot": return CreateRescueSweepBot(consumer);
			case "SeekAndInstallBionicUpgrade": return CreateSeekAndInstallUpgrade(consumer);
			case "Sleep": return CreateSleep(dupeGO);
			case "StressIdle": return CreateStressIdle(consumer);
			case "StressShock": return CreateStressShock(dupeGO);
			case "Sweep": return CreateWorkChore<Pickupable>(type, consumer, cell);
			case "SwitchRoleHat": return CreateSwitchRoleHat(dupeGO);
			//case "TakeMedicine": return CreateTakeMedicine(dupeGO, pillWorkable); // TODO Figure out
			case "TakeOffHat": return CreateTakeOffHat(dupeGO);
			case "UseSolidLubricant": return CreateUseSolidLubricant(consumer);
			case "Vomit": return CreateVomit(dupeGO);
			case "WaterCooler": return CreateWaterCoolerChore(dupeGO);
			default:
				DebugConsole.LogWarning($"[ChoreFactory] Unhandled chore type: {choreTypeId}");
				return null;
		}
	}


	private static Chore CreateRancher(ChoreConsumer consumer, int cell)
	{
		using var _ = Profiler.Scope();

		if (!Grid.IsValidCell(cell))
		{
			DebugConsole.LogWarning($"[ChoreFactory] Invalid cell passed to CreateRancher: {cell}");
			return null;
		}

		var buildingObj = Grid.Objects[cell, (int)ObjectLayer.Building];
		if (buildingObj == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No building found at cell {cell} for Rancher chore.");
			return null;
		}

		var kPrefab = buildingObj.GetComponent<KPrefabID>();
		if (kPrefab == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No KPrefabID on building at cell {cell} for Rancher chore.");
			return null;
		}

		var smi = kPrefab.GetSMI<RanchStation.Instance>();
		if (smi == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] KPrefabID at cell {cell} does not have RanchStation.Instance.");
			return null;
		}

		return new RancherChore(kPrefab);
	}


	private static Chore CreatePutOnHat(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		var smTarget = consumer.GetComponent<IStateMachineTarget>();
		if (smTarget == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] {consumer.name} does not implement IStateMachineTarget for PutOnHatChore.");
			return null;
		}

		return new PutOnHatChore(smTarget, Db.Get().ChoreTypes.SwitchHat);
	}


	private static Chore CreatePee(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		return new PeeChore(consumer);
	}

	private static Chore CreateParty(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		var smTarget = consumer.GetComponent<IStateMachineTarget>();
		if (smTarget == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] {consumer.name} does not implement IStateMachineTarget for PartyChore.");
			return null;
		}

		const float searchRadius = 12f;
		Vector3 origin = consumer.transform.position;

		var workable = Utils.FindNearbyWorkable(origin, searchRadius, go =>
				go.HasTag(GameTags.Partying) && go.GetComponent<Workable>() != null
		)?.GetComponent<Workable>();

		if (workable == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No valid Workable (Partying) found near {consumer.name} for PartyChore.");
			return null;
		}

		return new PartyChore(smTarget, workable);
	}


	private static Chore CreateMoveToSafety(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		return new MoveToSafetyChore(consumer);
	}

	private static Chore CreateMoveToQuarantine(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		var smTarget = consumer.GetComponent<IStateMachineTarget>();
		if (smTarget == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] {consumer.name} does not implement IStateMachineTarget for MoveToQuarantineChore.");
			return null;
		}

		const float searchRadius = 12f;
		Vector3 origin = consumer.transform.position;

		var quarantineObj = Utils.FindNearbyWorkable(origin, searchRadius, go =>
				go.HasTag(GameTags.Medicine) && go.GetComponent<KMonoBehaviour>() != null
		);

		if (quarantineObj == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No valid quarantine area (MedicalCot) found near {consumer.name} for MoveToQuarantineChore.");
			return null;
		}

		return new MoveToQuarantineChore(smTarget, quarantineObj.GetComponent<KMonoBehaviour>());
	}

	private static Chore CreateMovePickupable(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		var smTarget = consumer.GetComponent<IStateMachineTarget>();
		if (smTarget == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] {consumer.name} does not implement IStateMachineTarget for MovePickupableChore.");
			return null;
		}

		// Try to find a Pickupable nearby (that is NOT already stored)
		const float searchRadius = 12f;
		Vector3 origin = consumer.transform.position;

		var pickupable = Utils.FindNearbyWorkable(origin, searchRadius, go =>
				go.HasTag(GameTags.Pickupable) &&
				go.GetComponent<Pickupable>() != null &&
				!go.HasTag(GameTags.Stored)
		)?.GetComponent<Pickupable>();

		if (pickupable == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No valid pickupable item found near {consumer.name} for MovePickupableChore.");
			return null;
		}

		// Now find a valid delivery target (Storage)
		var storageTarget = Utils.FindNearbyWorkable(origin, searchRadius, go =>
				go.GetComponent<Storage>() != null &&
				go.GetComponent<CancellableMove>() != null
		)?.GetComponent<KMonoBehaviour>();

		if (storageTarget == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No valid delivery target found near {consumer.name} for MovePickupableChore.");
			return null;
		}

		return new MovePickupableChore(storageTarget, pickupable.gameObject, chore => { });
	}


	private static Chore CreateMove(ChoreConsumer consumer, GameObject targetObject)
	{
		using var _ = Profiler.Scope();

		if (consumer == null || targetObject == null)
		{
			DebugConsole.LogWarning("[ChoreFactory] Invalid consumer or targetObject for MoveChore.");
			return null;
		}

		var smTarget = consumer.GetComponent<IStateMachineTarget>();
		if (smTarget == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] {consumer.name} does not implement IStateMachineTarget.");
			return null;
		}

		const float searchRadius = 12f;
		Vector3 origin = consumer.transform.position;
		int targetCell = Grid.PosToCell(targetObject);

		int closestCell = Grid.InvalidCell;
		float closestDistSq = float.MaxValue;

		CellOffset[] offsets = OffsetGroups.Standard;

		for (int i = 0; i < offsets.Length; i++)
		{
			int offsetCell = Grid.OffsetCell(targetCell, offsets[i]);

			if (!Utils.IsWalkableCell(offsetCell))
				continue;

			float distSq = (Grid.CellToPos(offsetCell) - origin).sqrMagnitude;
			if (distSq < closestDistSq && distSq <= searchRadius * searchRadius)
			{
				closestDistSq = distSq;
				closestCell = offsetCell;
			}
		}

		if (closestCell == Grid.InvalidCell)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No valid walkable cell found near {targetObject.name}.");
			return null;
		}

		return new MoveChore(smTarget, Db.Get().ChoreTypes.MoveTo, smi => closestCell);
	}



	private static Chore CreateMourn(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		return new MournChore(consumer);
	}

	private static Chore CreateMingle(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		return new MingleChore(consumer);
	}

	private static Chore CreateIdle(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		return new IdleChore(consumer);
	}

	private static Chore CreateFoodFight(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		var smTarget = consumer.GetComponent<IStateMachineTarget>();
		if (smTarget == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] {consumer.name} does not implement IStateMachineTarget for FoodFightChore.");
			return null;
		}

		const float searchRadius = 16f;
		Vector3 origin = consumer.transform.position;

		// Find the nearest FoodFight locator
		GameObject locator = Utils.FindClosestGameObjectWithTag(origin, GameTags.Decoration, searchRadius);
		if (locator == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No valid FoodFight locator found near {consumer.name}.");
			return null;
		}

		// Ensure the duplicant has an edible
		var rationMonitor = consumer.GetSMI<RationMonitor.Instance>();
		if (rationMonitor == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] {consumer.name} has no RationMonitor.");
			return null;
		}

		var edible = rationMonitor.GetEdible();
		if (edible == null || edible.gameObject == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] {consumer.name} has no edible item for FoodFightChore.");
			return null;
		}

		return new FoodFightChore(smTarget, locator);
	}



	// Placeholder for each specialized method, one per chore (to be defined)
	private static Chore CreateAggressive(ChoreConsumer consumer) => new AggressiveChore(consumer);
	private static Chore CreateAttack(ChoreConsumer consumer) => new AttackChore(consumer, null);
	private static Chore CreateBeIncapacitated(ChoreConsumer consumer) => new BeIncapacitatedChore(consumer);
	private static Chore CreateBeOffline(ChoreConsumer consumer) => new BeOfflineChore(consumer);
	private static Chore CreateBingeEat(ChoreConsumer consumer) => new BingeEatChore(consumer);
	private static Chore CreateBionicBedTimeMode(ChoreConsumer consumer) => new BionicBedTimeModeChore(consumer);
	private static Chore CreateBionicGunkSpill(ChoreConsumer consumer) => new BionicGunkSpillChore(consumer);
	private static Chore CreateBionicOxygenAbsorb(ChoreConsumer consumer, string typeId) => new BionicMassOxygenAbsorbChore(consumer, typeId == "BionicAbsorbOxygen_Critical");
	private static Chore CreateDeliverFood(ChoreConsumer consumer) => new DeliverFoodChore(consumer);
	private static Chore CreateDropUnusedInventory(ChoreConsumer consumer) => new DropUnusedInventoryChore(Db.Get().ChoreTypes.DropUnusedInventory, consumer);
	private static Chore CreateEat(ChoreConsumer consumer) => new EatChore(consumer);
	private static Chore CreateEntombed(ChoreConsumer consumer) => new EntombedChore(consumer, "anim_dupe_thoughts_kanim");
	private static Chore CreateEquip(ChoreConsumer consumer) => new EquipChore(consumer);
	private static Chore CreateFetchArea(Precondition.Context context) => new FetchAreaChore(context);
	private static Chore CreateFetch(ChoreConsumer consumer) => new FetchChore(Db.Get().ChoreTypes.Fetch, consumer.GetComponent<Storage>(), 200f, new HashSet<Tag> { GameTags.Edible }, FetchChore.MatchCriteria.MatchTags, Tag.Invalid, new Tag[0], consumer.GetComponent<ChoreProvider>());
	private static Chore CreateOxygenSourceChore(ChoreConsumer consumer) => new FindAndConsumeOxygenSourceChore(consumer.gameObject.GetComponent<IStateMachineTarget>(), false);
	private static Chore CreateFixedCapture(int cell)
	{
		using var _ = Profiler.Scope();

		var obj = Grid.Objects[cell, (int)ObjectLayer.Building];
		var prefab = obj?.GetComponent<KPrefabID>();
		return prefab?.GetSMI<FixedCapturePoint.Instance>() != null ? new FixedCaptureChore(prefab) : null;
	}
	private static Chore CreateFlee(GameObject dupeGO)
	{
		using var _ = Profiler.Scope();

		var enemy = Utils.FindEntityInRadius(
				dupeGO.transform.position,
				8f,
				go => go != dupeGO && go.GetComponent<AttackableBase>() != null
		);

		return enemy != null ? new FleeChore(dupeGO.GetComponent<IStateMachineTarget>(), enemy) : null;
	}


	private static Chore CreateRecoverFromHeat(ChoreConsumer consumer) => new RecoverFromHeatChore(consumer);
	private static Chore CreateRecoverFromCold(ChoreConsumer consumer) => new RecoverFromColdChore(consumer);
	private static Chore CreateRecoverBreath(ChoreConsumer consumer) => new RecoverBreathChore(consumer);
	private static Chore CreateRemote(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		var smTarget = consumer.GetComponent<IStateMachineTarget>();
		if (smTarget == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] {consumer.name} does not implement IStateMachineTarget for RemoteChore.");
			return null;
		}

		const float searchRadius = 12f;
		Vector3 origin = consumer.transform.position;

		// Search for the closest terminal with a RemoteWorkTerminal component
		var terminalGO = Utils.FindNearbyWorkable(origin, searchRadius, go =>
				go.GetComponent<RemoteWorkTerminal>() != null);

		if (terminalGO == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No valid RemoteWorkTerminal found near {consumer.name}.");
			return null;
		}

		var terminal = terminalGO.GetComponent<RemoteWorkTerminal>();
		return new RemoteChore(terminal);
	}

	private static Chore CreateReloadElectrobank(ChoreConsumer consumer) => new ReloadElectrobankChore(consumer);
	private static Chore CreateRescueIncapacitated(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		Vector3 origin = consumer.transform.position;
		float searchRadius = 10f;

		GameObject incapacitated = Utils.FindEntityInRadius(origin, searchRadius, go =>
				go.HasTag(GameTags.Incapacitated) &&
				go.GetSMI<BeIncapacitatedChore.StatesInstance>() != null);

		if (incapacitated == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No incapacitated duplicant found near {consumer.name}");
			return null;
		}

		return new RescueIncapacitatedChore(consumer, incapacitated);
	}

	private static Chore CreateRescueSweepBot(ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		Vector3 origin = consumer.transform.position;
		float searchRadius = 10f;

		// Find a trapped sweep bot with a SweepBotTrappedStates.Instance
		GameObject sweepBot = Utils.FindEntityInRadius(origin, searchRadius, go =>
				go.HasTag(GameTags.Robot) &&
				go.GetComponent<Storage>() != null &&
				go.GetSMI<SweepBotTrappedStates.Instance>() != null);

		if (sweepBot == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No trapped SweepBot found near {consumer.name}");
			return null;
		}

		var trappedSMI = sweepBot.GetSMI<SweepBotTrappedStates.Instance>();
		var baseStation = trappedSMI?.sm.GetSweepLocker(trappedSMI)?.gameObject;

		if (baseStation == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No base station found for SweepBot {sweepBot.name}");
			return null;
		}

		return new RescueSweepBotChore(consumer, sweepBot, baseStation);
	}

	private static Chore CreateSeekAndInstallUpgrade(ChoreConsumer consumer) => new SeekAndInstallBionicUpgradeChore(consumer);
	private static Chore CreateSleep(GameObject dupeGO)
	{
		using var _ = Profiler.Scope();

		GameObject bed = null;
		bool bedIsLocator = true;

		// Try to get a valid assigned bed (if any)
		Ownables ownables = dupeGO.GetComponent<MinionIdentity>()?.GetComponent<Ownables>();
		if (ownables != null)
		{
			AssignableSlotInstance slot = ownables.GetSlot(Db.Get().AssignableSlots.Bed);
			if (slot != null && slot.assignable != null)
			{
				bed = slot.assignable.gameObject;
				bedIsLocator = false;
			}
		}

		// Fallback: create a temporary sleep locator on the floor
		if (bed == null)
		{
			bed = SleepChore.GetSafeFloorLocator(dupeGO).gameObject;
			bedIsLocator = true;
		}

		return new SleepChore(Db.Get().ChoreTypes.Sleep, dupeGO.GetComponent<IStateMachineTarget>(), bed, bedIsLocator, isInterruptable: true);
	}


	private static Chore CreateStressIdle(ChoreConsumer consumer) => new StressIdleChore(consumer);
	public static Chore CreateStressShock(GameObject dupe, Notification notification = null)
	{
		using var _ = Profiler.Scope();

		if (dupe == null || dupe.GetComponent<ChoreProvider>() == null)
			return null;

		// Avoid duplicates
		var existingChore = dupe.GetComponent<ChoreDriver>()?.GetCurrentChore();
		if (existingChore is StressShockChore)
			return null;

		// Create the chore
		var stressShockChore = new StressShockChore(
				Db.Get().ChoreTypes.StressShock,
				dupe.GetComponent<IStateMachineTarget>(),
				new Notification(
						"Stress Shock",
						NotificationType.BadMinor,
						(List<Notification> notifList, object data) => "A duplicant is stress shocking!"
				)
		);

		return stressShockChore;
	}

	public static Chore CreateSwitchRoleHat(GameObject dupe)
	{
		using var _ = Profiler.Scope();

		if (dupe == null || dupe.GetComponent<ChoreProvider>() == null)
			return null;

		// Avoid duplicates
		var currentChore = dupe.GetComponent<ChoreDriver>()?.GetCurrentChore();
		if (currentChore is SwitchRoleHatChore)
			return null;

		// Ensure the dupe has a state machine target
		var smiTarget = dupe.GetComponent<IStateMachineTarget>();
		if (smiTarget == null)
			return null;

		// Create the chore
		var hatChore = new SwitchRoleHatChore(smiTarget, Db.Get().ChoreTypes.SwitchHat);
		return hatChore;
	}

	public static Chore CreateTakeMedicine(GameObject dupe, MedicinalPillWorkable pillWorkable)
	{
		using var _ = Profiler.Scope();

		if (dupe == null || pillWorkable == null)
			return null;

		// Ensure the dupe has the necessary components
		if (dupe.GetComponent<ChoreProvider>() == null ||
				dupe.GetComponent<IStateMachineTarget>() == null ||
				dupe.GetComponent<ConsumableConsumer>() == null)
			return null;

		// Check if the medicine can be taken
		if (!pillWorkable.CanBeTakenBy(dupe))
			return null;

		// Check consumption permission
		if (!dupe.GetComponent<ConsumableConsumer>().IsPermitted(pillWorkable.PrefabID().Name))
			return null;

		// Create and return the TakeMedicineChore
		return new TakeMedicineChore(pillWorkable);
	}

	public static Chore CreateTakeOffHat(GameObject dupe)
	{
		using var _ = Profiler.Scope();

		if (dupe == null)
			return null;

		var resume = dupe.GetComponent<MinionResume>();
		var choreProvider = dupe.GetComponent<ChoreProvider>();
		var smTarget = dupe.GetComponent<IStateMachineTarget>();

		// Ensure the dupe has a hat to remove and required components
		if (resume == null || resume.CurrentHat == null || choreProvider == null || smTarget == null)
			return null;

		// Avoid duplicate chore
		var currentChore = dupe.GetComponent<ChoreDriver>()?.GetCurrentChore();
		if (currentChore is TakeOffHatChore)
			return null;

		// Create the chore
		return new TakeOffHatChore(smTarget, Db.Get().ChoreTypes.SwitchHat);
	}

	private static Chore CreateUseSolidLubricant(ChoreConsumer consumer) => new UseSolidLubricantChore(consumer);
	public static Chore CreateVomit(GameObject dupe)
	{
		using var _ = Profiler.Scope();

		if (dupe == null)
			return null;

		var choreProvider = dupe.GetComponent<ChoreProvider>();
		var smTarget = dupe.GetComponent<IStateMachineTarget>();

		// Required components
		if (choreProvider == null || smTarget == null)
			return null;

		// Prevent duplicate vomiting
		var currentChore = dupe.GetComponent<ChoreDriver>()?.GetCurrentChore();
		if (currentChore is VomitChore)
			return null;

		// Optional: Ensure dupe is sick/stressed/etc.
		// Example: if not stressed, skip chore
		var stress = Db.Get().Amounts.Stress.Lookup(dupe);
		if (stress == null || stress.value < 0.9f) // adjust as needed
			return null;

		// Create vomit notification and status item
		Notification vomitNotification = new Notification(
				DUPLICANTS.STATUSITEMS.VOMITING.NOTIFICATION_NAME,
				NotificationType.BadMinor,
				(List<Notification> notificationList, object data) =>
						string.Format(DUPLICANTS.STATUSITEMS.VOMITING.NOTIFICATION_TOOLTIP, dupe.GetProperName()),
				null, // tooltip icon (optional)
				false, // show in tooltip
				0f, // delay time
				null, null, null);

		StatusItem statusItem = Db.Get().DuplicantStatusItems.Vomiting;

		// Create and return the chore
		return new VomitChore(Db.Get().ChoreTypes.Vomit, smTarget, statusItem, vomitNotification);
	}

	public static Chore CreateWaterCoolerChore(GameObject dupe)
	{
		using var scope = Profiler.Scope();

		if (dupe == null || dupe.GetComponent<IStateMachineTarget>() == null)
			return null;

		// Find the closest WaterCooler that has a SocialGatheringPointWorkable
		GameObject found = Utils.FindNearbyWorkable(
				dupe.transform.position,
				64f,
				go => go.TryGetComponent<WaterCooler>(out _) && go.TryGetComponent<SocialGatheringPointWorkable>(out _)
		);

		if (found == null || !found.TryGetComponent(out SocialGatheringPointWorkable workable))
			return null;

		return new WaterCoolerChore(
				dupe.GetComponent<IStateMachineTarget>(),
				workable
		);
	}

	private static Chore CreateWorkChore<T>(ChoreType type, ChoreConsumer consumer, int cell) where T : Workable
	{
		using var _ = Profiler.Scope();

		if (!Grid.IsValidCell(cell))
		{
			DebugConsole.LogWarning($"[ChoreFactory] Invalid grid cell: {cell}");
			return null;
		}

		var choreProvider = consumer.GetComponent<ChoreProvider>();
		if (choreProvider == null)
		{
			DebugConsole.LogWarning("[ChoreFactory] Consumer has no ChoreProvider.");
			return null;
		}

		ObjectLayer[] layersToCheck = new ObjectLayer[]
		{
						ObjectLayer.Building,
						ObjectLayer.Pickupables,
						ObjectLayer.Plants,
						ObjectLayer.AttachableBuilding,
						ObjectLayer.Critter,
						ObjectLayer.Gantry
		};

		foreach (var layer in layersToCheck)
		{
			var obj = Grid.Objects[cell, (int)layer];
			if (obj == null)
				continue;

			var workable = obj.GetComponent<T>();
			if (workable != null)
			{
				return new WorkChore<T>(type, workable, choreProvider, run_until_complete: true);
			}
		}

		DebugConsole.LogWarning($"[ChoreFactory] No workable of type {typeof(T).Name} found in cell {cell}");
		return null;
	}

	private static Chore CreateNamedEmote(string id, ChoreType type, ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		var emote = Db.Get().Emotes.Get(id) as Klei.AI.Emote;
		if (emote == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] Emote '{id}' not found or invalid type.");
			return null;
		}

		return new EmoteChore(consumer, type, emote);
	}

	private static Chore CreateDie(GameObject dupeGO, ChoreConsumer consumer)
	{
		using var _ = Profiler.Scope();

		var deathMonitor = dupeGO.GetSMI<DeathMonitor.Instance>();
		if (deathMonitor == null) return null;
		var field = typeof(DeathMonitor.Instance).GetField("death", BindingFlags.NonPublic | BindingFlags.Instance);
		var death = field?.GetValue(deathMonitor) as Death;
		return death != null ? new DieChore(consumer, death) : null;
	}

	public static Chore CreateDig(ChoreConsumer consumer, int cell)
	{
		using var _ = Profiler.Scope();

		if (!Grid.IsValidCell(cell))
		{
			DebugConsole.LogWarning($"[ChoreFactory] Invalid dig cell: {cell}");
			return null;
		}

		Diggable diggable = Diggable.GetDiggable(cell);
		if (diggable == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] No Diggable found at cell {cell}");
			return null;
		}

		if (diggable.chore == null)
		{
			DebugConsole.LogWarning($"[ChoreFactory] Diggable at cell {cell} has no chore instance");
			return null;
		}

		return diggable.chore;
	}


}

}