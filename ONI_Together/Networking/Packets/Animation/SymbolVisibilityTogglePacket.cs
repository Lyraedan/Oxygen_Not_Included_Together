using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Animation
{
	internal class SymbolVisibilityTogglePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		int NetId;
		KAnimHashedString Symbol;
		bool Is_Visible;

		public SymbolVisibilityTogglePacket() { }
		public SymbolVisibilityTogglePacket(KAnimControllerBase kbac, KAnimHashedString symbol, bool is_visible)
		{
			using var _ = Profiler.Scope();

			NetId = kbac.GetNetId();
			Symbol = symbol;
			Is_Visible = is_visible;
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			Symbol = new KAnimHashedString(reader.ReadInt32());
			Is_Visible = reader.ReadBoolean();

		}


		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(Symbol.hash);
			writer.Write(Is_Visible);
		}
		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGetComponent<KBatchedAnimController>(NetId,out var kbac))
				return;
			kbac.SetSymbolVisiblity(Symbol, Is_Visible);
		}
	}
}
