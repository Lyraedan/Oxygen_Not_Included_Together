using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Patches.KleiPatches;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking.Packets.Core
{
public class PlayAnimPacket : IPacket
{

	public PlayAnimPacket() { }
	public PlayAnimPacket(int targetNetId, HashedString[] anims, bool queue, KAnim.PlayMode mode, float speed, float offset)
	{
		using var _ = Profiler.Scope();

		NetId = targetNetId;
		AnimHashes = anims;
		IsQueue = queue;
		Mode = mode;
		Speed = speed;
		TimeOffset = offset;
		TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	}

	public int NetId;
	public long TimeStamp;
	public HashedString[] AnimHashes = [];
	public KAnim.PlayMode Mode;
	public float Speed;
	public float TimeOffset;
	public bool IsQueue; // Supports Queue()
	bool MultipleAnims => AnimHashes.Count() > 1;

    public void Serialize(BinaryWriter writer)
	{
		using var _ = Profiler.Scope();

		writer.Write(NetId);
		writer.Write(TimeStamp);
		writer.Write((int)Mode);
		writer.Write(Speed);
		writer.Write(TimeOffset);
		writer.Write(IsQueue);

		writer.Write(AnimHashes.Count());
		foreach (var hashedString in AnimHashes)
			writer.Write(hashedString.hash);
	}

	public void Deserialize(BinaryReader reader)
	{
		using var _ = Profiler.Scope();

		NetId = reader.ReadInt32();
		TimeStamp = reader.ReadInt64();
		Mode = (KAnim.PlayMode)reader.ReadInt32();
		Speed = reader.ReadSingle();
		TimeOffset = reader.ReadSingle();
		IsQueue = reader.ReadBoolean();

		int count = reader.ReadInt32();
		AnimHashes = new HashedString[count];
		for (int i = 0; i < count; i++)
			AnimHashes[i] = new HashedString(reader.ReadInt32());
	}

	private static readonly Dictionary<int, long> LastIdUpdates = [];

	// Invariant #6: bound long-lived collections. Prune per-entity entry on cleanup,
	// clear the whole map on session teardown via NetworkIdentityRegistry.Clear().
	public static void ForgetNetId(int netId)
	{
		LastIdUpdates.Remove(netId);
	}

	public static void ClearState()
	{
		LastIdUpdates.Clear();
	}

	public void OnDispatched()
	{
		using var _ = Profiler.Scope();

		if (MultiplayerSession.IsHost)
			return;

		if (!NetworkIdentityRegistry.TryGet(NetId, out var go))
			return;

		// Keep the last event time per entity so older anim packets cannot rewind newer state.
		if (LastIdUpdates.TryGetValue(NetId, out var lastTimeStamp) && lastTimeStamp > TimeStamp)
			return;
		LastIdUpdates[NetId] = TimeStamp;


		if (!AnimHashes.Any())
		{
			DebugConsole.LogWarning("emtpy anim list dispatched for " + go.name);
			return;
		}

		//// Check for DuplicantClientController first (for duplicants)
		//var clientController = go.GetComponent<DuplicantClientController>();
		//if (clientController != null)
		//{
		//	if (IsMulti)
		//	{
		//		var hashedStrings = AnimHashes.ConvertAll(hash => new HashedString(hash)).ToArray();
		//		clientController.OnAnimationsReceived(hashedStrings, Mode);
		//	}
		//	else
		//	{
		//		clientController.OnAnimationReceived(new HashedString(SingleAnimHash), Mode, Speed, IsQueue);
		//	}
		//	return;
		//}

		//// Fallback: direct animation control for non-duplicant entities
		if (!go.TryGetComponent(out KBatchedAnimController kbac))
			return;

		if (MultipleAnims)
		{
			KAnimControllerBase_Patches.AllowAnims();
			try
			{
				kbac.Play(AnimHashes, Mode);
			}
			finally
			{
				KAnimControllerBase_Patches.ForbidAnims();
			}
		}
		else
		{
			if (IsQueue)
			{
				KAnimControllerBase_Patches.AllowAnims();
				try
				{
					kbac.Queue(AnimHashes.FirstOrDefault(), Mode, Speed, TimeOffset);
				}
				finally
				{
					KAnimControllerBase_Patches.ForbidAnims();
				}
			}
			else
			{
				KAnimControllerBase_Patches.AllowAnims();
				try
				{
					kbac.Play(AnimHashes.FirstOrDefault(), Mode, Speed, TimeOffset);
				}
				finally
				{
					KAnimControllerBase_Patches.ForbidAnims();
				}
			}

		}
		ForceAnimUpdate(kbac);
		if (go.TryGetComponent<AnimStateSyncer>(out var syncer))
			syncer.MarkSnapshotReceived();
		// Force updates for animation to tick properly
	}

	private void ForceAnimUpdate(KBatchedAnimController kbac)
	{
		using var _ = Profiler.Scope();

		try
		{
			kbac.SetVisiblity(true);
			kbac.forceRebuild = true;
			kbac.SuspendUpdates(false);
			kbac.ConfigureUpdateListener();
		}
		catch (Exception ex)
		{
			DebugConsole.LogError($"[PlayAnimPacket] Failed to force anim update for NetId {NetId}: {ex}");
		}

	}
}


}