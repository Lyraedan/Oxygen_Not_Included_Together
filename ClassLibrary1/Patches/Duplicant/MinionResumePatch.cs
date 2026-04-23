using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.DuplicantActions;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.Duplicant
{
	// Sync Skill Mastery
	[HarmonyPatch(typeof(MinionResume), "MasterSkill")]
	public static class MinionResumePatch
	{
		public static void Postfix(MinionResume __instance, string skillId)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return;
			if (SkillMasteryPacket.IsApplying) return;

			var identity = __instance.GetComponent<NetworkIdentity>();
			if (identity != null)
			{
				var packet = new SkillMasteryPacket
				{
					NetId = identity.NetId,
					SkillId = skillId
				};

				if (MultiplayerSession.IsHost)
				{
					PacketSender.SendToAllClients(packet);
				}
				else
				{
					PacketSender.SendToHost(packet);
				}

				DebugConsole.Log($"[MinionResumePatch] Sent skill mastery for {identity.name}: {skillId}");
			}
		}
	}
}
