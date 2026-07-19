using System;
using System.IO;
using System.Linq;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.Duplicant;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DuplicantActions;

public sealed class SkillHatRequestPacket : IPacket, IClientRelayable
{
	public int NetId;
	public string TargetHat = string.Empty;

	public void Serialize(BinaryWriter writer)
	{
		if (!IsWireValid())
			throw new InvalidDataException("Invalid skill hat request");
		writer.Write(NetId);
		writer.Write(TargetHat);
	}

	public void Deserialize(BinaryReader reader)
	{
		NetId = reader.ReadInt32();
		TargetHat = reader.ReadString();
		if (!IsWireValid())
			throw new InvalidDataException("Invalid skill hat request");
	}

	public void OnDispatched()
	{
		DispatchContext context = PacketHandler.CurrentContext;
		bool protocolVerified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
		if (!ShouldAccept(MultiplayerSession.IsHost, context, protocolVerified)
		    || !SkillResumeSync.TryResolveResume(NetId, out MinionResume resume)
		    || !IsKnownHat(resume, TargetHat))
			return;

		SkillResumeMutationScope scope = SkillResumeSync.BeginHostMutation(resume);
		Exception error = null;
		try
		{
			string target = string.IsNullOrEmpty(TargetHat) ? null : TargetHat;
			resume.SetHats(resume.CurrentHat, target);
			if (target == null)
				resume.ApplyTargetHat();
			else if (resume.OwnsHat(target))
				resume.CreateHatChangeChore();
		}
		catch (Exception exception)
		{
			error = exception;
			DebugConsole.LogWarning($"[SkillHatRequest] Failed for NetId {NetId}: {exception}");
		}
		finally
		{
			SkillResumeSync.CompleteHostMutation(scope, error);
		}
	}

	internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
		=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;

	internal static bool IsKnownHat(MinionResume resume, string targetHat)
		=> resume != null && (string.IsNullOrEmpty(targetHat)
		   || resume.GetAllHats().Any(hat => string.Equals(hat.Hat, targetHat, StringComparison.Ordinal)));

	private bool IsWireValid()
		=> NetId != 0 && TargetHat != null && TargetHat.Length <= SkillResumeStateData.MaxIdLength;
}
