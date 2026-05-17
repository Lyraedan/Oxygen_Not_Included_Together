using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DuplicantActions
{
	public class SkillMasteryPacket : IPacket
	{
		public int NetId;
		public string SkillId;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(SkillId ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			SkillId = reader.ReadString();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
			{
				Apply();
				PacketSender.SendToAllClients(this);
			}
			else
			{
				Apply();
			}
		}

		private void Apply()
		{
			using var _ = Profiler.Scope();

			if (!NetworkIdentityRegistry.TryGet(NetId, out var identity) || identity == null)
			{
				DebugConsole.LogWarning($"[SkillMasteryPacket] NetId {NetId} not found.");
				return;
			}

			if (identity.gameObject == null)
			{
				DebugConsole.LogWarning($"[SkillMasteryPacket] NetId {NetId} has null gameObject.");
				return;
			}

			var resume = identity.gameObject.GetComponent<MinionResume>();
			if (resume == null)
			{
				DebugConsole.LogWarning($"[SkillMasteryPacket] NetId {NetId} has no MinionResume.");
				return;
			}

			if (Db.Get().Skills.TryGet(SkillId) == null)
			{
				DebugConsole.LogWarning($"[SkillMasteryPacket] SkillId {SkillId} not found.");
				return;
			}

			if (resume.HasMasteredSkill(SkillId))
			{
				return;
			}

			IsApplying = true;
			try
			{
				// Force mastery. MinionResume.MasterSkill(skillId) usually checks points.
				// We might need to bypass checks or ensure points are available?
				// For sync, we assume the sender validated it.
				// We want to force it.

				// resume.MasterSkill(SkillId); // This deducts points and triggers effects.

				// If points are desynced, this might fail or create negative points.
				// Ideally we sync experience/points too.
				// But for now, let's call the game method.

				resume.MasterSkill(SkillId); // This deducts points and triggers effects.

				SkillsScreen skillScreen = Object.FindFirstObjectByType< SkillsScreen >();
				if( skillScreen != null )
					skillScreen.RefreshAll();

				DebugConsole.Log($"[SkillMasteryPacket] Applied Skill {SkillId} to {identity.name}");
			}
			finally
			{
				IsApplying = false;
			}
		}

		public static bool IsApplying = false;
	}
}
