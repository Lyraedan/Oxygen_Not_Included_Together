using System.Collections.Generic;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.Networking.Components.StructureStateSyncers;

public class ReactorStateSyncer : StructureSyncerBase
{
    private Reactor reactor;
    private Reactor.StatesInstance smi;
    private Storage supplyStorage;
    private Storage reactionStorage;
    private Storage wasteStorage;
	private StorageSnapshotSync.SnapshotBatch _preparedStorageBatch;

    private float lastFuelTemp;
    private int lastMajorState = -1;
    private float tempChangeThreshold = 5f;
    
    protected override void Initialize()
    {
        reactor = GetComponent<Reactor>();
        smi = reactor.smi;
        supplyStorage = reactor.supplyStorage;
        reactionStorage = reactor.reactionStorage;
        wasteStorage = reactor.wasteStorage;
		checkOptionalsValuesForChanges = true;
        cullByViewport = false;
    }

    protected override void SampleState(out Variant value, out bool active, out Dictionary<string, Variant> optionalValues)
    {
        float fuelTemp = reactor.FuelTemperature;
        value = fuelTemp >= 0f ? fuelTemp : 0f;
        active = false;

        var sm = smi.sm;
        int majorState = 0;
        if (smi.IsInsideState(sm.dead)) majorState = 3;
        else if (smi.IsInsideState(sm.meltdown)) majorState = 2;
        else if (smi.IsInsideState(sm.on)) majorState = 1;

        optionalValues = new Dictionary<string, Variant>();
        optionalValues["major_state"] = majorState;
        optionalValues["reaction_underway"] = sm.reactionUnderway.Get(smi);
        optionalValues["melting_down"] = sm.meltingDown.Get(smi);
        optionalValues["melted"] = sm.melted.Get(smi);
        optionalValues["meltdown_mass_remaining"] = sm.meltdownMassRemaining.Get(smi);
        optionalValues["time_since_meltdown"] = sm.timeSinceMeltdown.Get(smi);
        optionalValues["can_vent"] = sm.canVent.Get(smi);
        optionalValues["spent_fuel"] = reactor.spentFuel;
        optionalValues["num_cycles_running"] = reactor.numCyclesRunning;
        optionalValues["fuel_delivery_enabled"] = reactor.fuelDeliveryEnabled;
        optionalValues["time_since_meltdown_emit"] = reactor.timeSinceMeltdownEmit;
        optionalValues["emit_rads"] = reactor.radEmitter.emitRads;
        
        PrimaryElement storedCoolant = smi.master.GetStoredCoolant();
        float waterMeterPercent = 0f;
        if (storedCoolant)
            waterMeterPercent = storedCoolant.Mass / 90f;
        
        optionalValues["water_meter_percent"] = waterMeterPercent;
        
        PrimaryElement activeFuel = smi.master.GetActiveFuel();
        float temperaturePercent = 0f;
        if (activeFuel != null)
            temperaturePercent = Mathf.Clamp01(activeFuel.Temperature / 3000f) / Reactor.meterFrameScaleHack;
        
        optionalValues["temperature_meter_percent"] = temperaturePercent;
        
        if (supplyStorage)
            StorageSnapshotSync.Encode(supplyStorage, optionalValues, "supply_");
        
        if (reactionStorage)
            StorageSnapshotSync.Encode(reactionStorage, optionalValues, "reaction_");
        
        if (wasteStorage != null)
            StorageSnapshotSync.Encode(wasteStorage, optionalValues, "waste_");
        
        lastFuelTemp = fuelTemp;
        lastMajorState = majorState;
    }

    protected override void ApplyState(StructureStatePacket packet)
    {
        if (reactor == null || smi == null) return;

            var opt = packet.OptionalValues;

            opt.TryGetValue("major_state", out var majorStateVal);
            opt.TryGetValue("reaction_underway", out var reactionUnderwayVal);
            opt.TryGetValue("melting_down", out var meltingDownVal);
            opt.TryGetValue("melted", out var meltedVal);
            opt.TryGetValue("meltdown_mass_remaining", out var meltdownMassVal);
            opt.TryGetValue("time_since_meltdown", out var timeSinceMeltdownVal);
            opt.TryGetValue("can_vent", out var canVentVal);
            opt.TryGetValue("spent_fuel", out var spentFuelVal);
            opt.TryGetValue("num_cycles_running", out var numCyclesVal);
            opt.TryGetValue("fuel_delivery_enabled", out var fuelDeliveryVal);
            opt.TryGetValue("time_since_meltdown_emit", out var timeSinceEmitVal);
            opt.TryGetValue("emit_rads", out var emitRadsVal);
            opt.TryGetValue("water_meter_percent", out var waterMeterVal);
            opt.TryGetValue("temperature_meter_percent", out var tempMeterVal);

            int targetState = majorStateVal.Int;
            bool reactionUnderway = reactionUnderwayVal.Boolean;
            bool meltingDown = meltingDownVal.Boolean;
            bool melted = meltedVal.Boolean;
            float meltdownMassRemaining = meltdownMassVal.Float;
            float timeSinceMeltdown = timeSinceMeltdownVal.Float;
            bool canVent = canVentVal.Boolean;
            float spentFuel = spentFuelVal.Float;
            int numCyclesRunning = numCyclesVal.Int;
            bool fuelDeliveryEnabled = fuelDeliveryVal.Boolean;
            float timeSinceMeltdownEmit = timeSinceEmitVal.Float;
            float rads = emitRadsVal.Float;
            float waterMeterPos = waterMeterVal.Float;
            float tempMeterPos = tempMeterVal.Float;

			StorageSnapshotSync.SnapshotBatch batch = _preparedStorageBatch;
			_preparedStorageBatch = null;
			bool storageApplied = batch?.Apply() == true;
			if (!storageApplied)
			{
				DebugConsole.LogError("[ReactorSync] Storage snapshot batch did not converge");
				RequestFreshState();
				return;
			}
            if (opt.ContainsKey("reaction_capacityKg"))
            {
                var fuel = reactionStorage.FindFirst(SimHashes.EnrichedUranium.CreateTag());
                if (fuel != null)
                {
                    var pe = fuel.GetComponent<PrimaryElement>();
                    if (pe != null) pe.Temperature = packet.Value.Float;
                }
            }
            var sm = smi.sm;
            sm.reactionUnderway.Set(reactionUnderway, smi);
            sm.meltingDown.Set(meltingDown, smi);
            sm.melted.Set(melted, smi);
            sm.meltdownMassRemaining.Set(meltdownMassRemaining, smi);
            sm.timeSinceMeltdown.Set(timeSinceMeltdown, smi);
            sm.canVent.Set(canVent, smi);

            reactor.spentFuel = spentFuel;
            reactor.numCyclesRunning = numCyclesRunning;
            reactor.fuelDeliveryEnabled = fuelDeliveryEnabled;
            reactor.timeSinceMeltdownEmit = timeSinceMeltdownEmit;
            reactor.radEmitter.emitRads = rads;
            reactor.waterMeter.SetPositionPercent(waterMeterPos);
            reactor.temperatureMeter.SetPositionPercent(tempMeterPos);

            int currentMajorState = 0;
            if (smi.IsInsideState(sm.dead)) currentMajorState = 3;
            else if (smi.IsInsideState(sm.meltdown)) currentMajorState = 2;
            else if (smi.IsInsideState(sm.on)) currentMajorState = 1;

            if (currentMajorState != targetState)
                ForceStateTransition(targetState, meltdownMassRemaining, spentFuel, rads);
    }

	private List<StorageSnapshotSync.SnapshotRequest> CreateStorageRequests(
		StructureStatePacket packet)
	{
		var requests = new List<StorageSnapshotSync.SnapshotRequest>();
		Storage[] storages = { supplyStorage, reactionStorage, wasteStorage };
		string[] prefixes = { "supply_", "reaction_", "waste_" };
		for (int index = 0; index < storages.Length; index++)
		{
			if (storages[index] == null)
				continue;
			requests.Add(new StorageSnapshotSync.SnapshotRequest
			{
				Storage = storages[index],
				Data = packet.OptionalValues,
				KeyPrefix = prefixes[index],
				SnapshotRevision = packet.Revision,
			});
		}
		return requests;
	}

	protected override bool TryAcceptPacketRevision(StructureStatePacket packet)
	{
		_preparedStorageBatch = null;
		if (!StorageSnapshotSync.TryPrepareBatch(
			    CreateStorageRequests(packet), out StorageSnapshotSync.SnapshotBatch batch)
		    || !NetworkIdentityRegistry.TryAcceptStateAndStorageSnapshotRevision(
			    packet.NetId, packet.SyncerTypeName, packet.Revision))
			return false;
		_preparedStorageBatch = batch;
		return true;
	}

    private void ForceStateTransition(int targetState, float meltdownMass, float spentFuel, float rads)
    {
        var sm = smi.sm;

        switch (targetState)
        {
            case 0:
                if (smi.IsInsideState(sm.on))
                    smi.GoTo(sm.off);
                break;
            case 1:
                if (smi.IsInsideState(sm.off))
                    sm.reactionUnderway.Set(true, smi);
                break;
            case 2:
                if (!smi.IsInsideState(sm.meltdown) && !smi.IsInsideState(sm.dead))
                {
                    supplyStorage.ConsumeAllIgnoringDisease();
                    reactionStorage.ConsumeAllIgnoringDisease();
                    wasteStorage.ConsumeAllIgnoringDisease();
                    float totalMass = supplyStorage.MassStored() + reactionStorage.MassStored() + wasteStorage.MassStored();
                    sm.meltdownMassRemaining.Set(10f + totalMass + spentFuel, smi);
                    smi.GoTo(sm.meltdown.pre);
                }
                break;
            case 3:
                if (smi.IsInsideState(sm.meltdown))
                    sm.meltdownMassRemaining.Set(0f, smi);
                else if (!smi.IsInsideState(sm.dead))
                    smi.GoTo(sm.dead);
                break;
        }
    }

    protected override bool ShouldForceSync()
    {
        if (reactor == null) return false;
        float currentTemp = reactor.FuelTemperature;
        if (currentTemp >= 0f && Mathf.Abs(currentTemp - lastFuelTemp) > tempChangeThreshold)
            return true;
        int currentState = 0;
        var sm = smi.sm;
        if (smi.IsInsideState(sm.dead)) currentState = 3;
        else if (smi.IsInsideState(sm.meltdown)) currentState = 2;
        else if (smi.IsInsideState(sm.on)) currentState = 1;
        if (currentState != lastMajorState) return true;
        return false;
    }
}
