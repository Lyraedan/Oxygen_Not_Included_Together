using ONI_Together.Misc.World;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DLC;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using ONI_Together.Networking.Packets.DLC.Frosty;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Patches.Bionics;
using ONI_Together.Patches.DLC.Aquatic;
using ONI_Together.Patches.DLC.Bionic;
using ONI_Together.Patches.DLC.Common;
using ONI_Together.Patches.DLC.Cosmetics;
using ONI_Together.Patches.DLC.Frosty;
using ONI_Together.Patches.DLC.Prehistoric;
using ONI_Together.Patches.DLC.SpacedOut;
using ONI_Together.Patches.Duplicant;

namespace ONI_Together.Networking
{
	public static class SessionStateReset
	{
		public static void Reset()
		{
			ResetCore();
			ResetAquatic();
			ResetPrehistoric();
			ResetBionic();
			ResetSpacedOut();
			ResetFrosty();
			PoiTechSync.ResetSessionState();
			CosmeticsSyncGuard.ResetSessionState();
		}

		private static void ResetCore()
		{
			WorldStateSyncer.SetAuthoritativeRepairSuppressed(false);
			WorldStateSyncer.SetWorldScanPaused(false);
#if DEBUG
				ONI_Together.DebugTools.SoakTickBarrier.ResetSessionState();
				ONI_Together.Networking.Packets.World.SoakHashDomainKeyframeTracker.Reset();
#endif
				ReadyManager.ResetSessionState();
				WorldDataRequestPacket.ResetSessionState();
				PacketHandler.ResetSessionState();
			HostBroadcastPacket.ResetSessionState();
			ChunkedPacket.ResetSessionState();
			PacketSender.ResetSessionState();
			NetworkConfig.TransportPacketSender?.ResetSessionState();
			GameServerHardSync.ResetSessionState();
			InstantiationBatcher.ResetSessionState();
			WorldUpdateBatcher.ResetSessionState();
			SaveChunkAssembler.ResetSessionState();
			SaveFileTransferManager.ResetSessionState();
			TcpTransferStartPacket.CancelActiveDownload();
			SyncProgressPacket.ResetSessionState();
			BuildingConfigPacket.ResetSessionState();
			DuplicantChoreBroadcaster.ResetSessionState();
				StatusBroadcaster.ResetSessionState();
				EntityPositionHandler.ResetSessionState();
				SkillResumeSync.ResetSessionState();
			DuplicantPriorityPacket.ResetSessionState();
			NetworkIdentity.ResetSessionState();
			PlantGrowthSyncer.ResetSessionState();
		}

		private static void ResetAquatic()
		{
			AquaticSync.ResetSessionState();
			UnderwaterVentSync.ResetSessionState();
			UnderwaterVentStatePacket.ResetSessionState();
			MinnowPoiSync.ResetSessionState();
			SeaTreeBranchSync.ResetSessionState();
			SeaTreeBranchStatePacket.ResetSessionState();
			OxyCoralSync.ResetSessionState();
			OxyCoralBubblePacket.ResetSessionState();
		}

		private static void ResetPrehistoric()
		{
			LargeImpactorOutcomePacket.ResetSessionState();
			LargeImpactorSync.ResetSessionState();
			CarnivorousPlantSync.ResetSessionState();
			VineBranchSync.ResetSessionState();
			FossilMarkerSync.ResetSessionState();
		}

		private static void ResetBionic()
		{
			BionicSyncGuard.ResetSessionState();
			ExplorerGeyserRevealSync.ResetSessionState();
			RemoteWorkerDockSync.ResetSessionState();
			RemoteWorkerDockSelectionSync.ResetSessionState();
			BionicExplosionSync.ResetSessionState();
			BionicRuntimeSync.ResetSessionState();
		}

		private static void ResetSpacedOut()
		{
			CryoTankSync.ResetSessionState();
			SpacedOutSyncGuard.ResetSessionState();
			HighEnergyParticleSync.ResetSessionState();
			RailGunPayloadSync.ResetSessionState();
			PlantMutationSync.ResetSessionState();
			SetLockerSync.ResetSessionState();
			CritterTrapGasSync.ResetSessionState();
			CritterTrapGasPacket.ResetSessionState();
			ClusterDiscoverySync.ResetSessionState();
		}

		private static void ResetFrosty()
		{
			FrostySyncGuard.ResetSessionState();
			MiniCometSync.ResetSessionState();
			IceKettleSync.ResetSessionState();
			SpaceTreeBranchSync.ResetSessionState();
			SpaceTreeSeededCometSync.ResetSessionState();
			SpaceTreeImpactPacket.ResetSessionState();
		}
	}
}
