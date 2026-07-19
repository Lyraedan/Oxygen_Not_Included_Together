using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.Duplicant;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DuplicantActions;

public sealed class SkillMasteryRequestPacket : IPacket, IClientRelayable
{
	internal const int MaxSkillIdLength = 256;
	public int NetId;
	public string SkillId = string.Empty;

	public void Serialize(BinaryWriter writer)
	{
		if (!IsWireValid())
			throw new InvalidDataException("Invalid skill mastery request");
		writer.Write(NetId);
		writer.Write(SkillId);
	}

	public void Deserialize(BinaryReader reader)
	{
		NetId = reader.ReadInt32();
		SkillId = reader.ReadString();
		if (!IsWireValid())
			throw new InvalidDataException("Invalid skill mastery request");
	}

	public void OnDispatched()
	{
		DispatchContext context = PacketHandler.CurrentContext;
		bool protocolVerified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
		if (!ShouldAccept(MultiplayerSession.IsHost, context, protocolVerified) ||
		    !SkillResumeSync.TryResolveResume(NetId, out MinionResume resume))
			return;

		var skill = Db.Get().Skills.TryGet(SkillId);
		if (skill == null || skill.deprecated || !Game.IsCorrectDlcActiveForCurrentSave(skill) ||
		    skill.requiredDuplicantModel != null && skill.requiredDuplicantModel != resume.GetIdentity.model ||
		    resume.HasMasteredSkill(SkillId))
			return;
		MinionResume.SkillMasteryConditions[] conditions = resume.GetSkillMasteryConditions(SkillId);
		if (resume.CanMasterSkill(conditions))
			resume.MasterSkill(SkillId);
	}

	internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
		=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;

	private bool IsWireValid()
		=> NetId != 0 && !string.IsNullOrEmpty(SkillId) && SkillId.Length <= MaxSkillIdLength;
}
