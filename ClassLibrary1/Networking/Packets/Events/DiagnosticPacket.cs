using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Networking.Packets.World;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using static ColonyDiagnostic;
using static ColonyDiagnostic.DiagnosticResult;

namespace ONI_MP.Networking.Packets.Events
{
	internal class DiagnosticPacket : IPacket//, IBulkablePacket
	{
		public int MaxPackSize => 500;

		public uint IntervalMs => 1000;

		public string DiagnosticType;
		public Opinion DiagnosticOpinion;
		public string DiagnosticMsg;
		public DiagnosticPacket(string diagnosticTypeName, DiagnosticResult diagnosticResult)
		{
			using var _ = Profiler.Scope();

			DiagnosticType = diagnosticTypeName;
			DiagnosticOpinion = diagnosticResult.opinion;
			DiagnosticMsg = diagnosticResult.Message;
		}
		public DiagnosticPacket() { }

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			DiagnosticType = reader.ReadString();
			DiagnosticOpinion = (Opinion)reader.ReadInt32();
			DiagnosticMsg = reader.ReadString();
		}


		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(DiagnosticType);
			writer.Write((int) DiagnosticOpinion);
			writer.Write(DiagnosticMsg);
		}
		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsClient)
				return;

			ColonyDiagnostic_Patches.OnPacketReceived(this);
		}
		public DiagnosticResult ToResult()
		{
			using var _ = Profiler.Scope();

			return new DiagnosticResult(DiagnosticOpinion, DiagnosticMsg);
		}
	}
}
