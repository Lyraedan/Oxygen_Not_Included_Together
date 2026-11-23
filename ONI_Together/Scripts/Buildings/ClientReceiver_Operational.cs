using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World.Buildings;
using ONI_Together.Networking.States;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Scripts.Buildings
{
	internal class ClientReceiver_Operational : KMonoBehaviour
	{
		[MyCmpGet] NetworkIdentity o;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();
			if (MultiplayerSession.IsHost)
				return;

			// Built while playing: ask for this one. A building from the save spawns while the
			// client is still loading, so those are all asked for at once when it is in game.
			if (MultiplayerSession.IsClient && GameClient.State == ClientState.InGame)
				PacketSender.SendToHost(new RequestOperationalStatePacket(this));
			else
				RequestOperationalStatePacket.RequestAllWhenInGame();
		}

		public bool IsFunctional { get; set; }

		public bool IsOperational { get; set; } = true;

		public bool IsActive { get; set; }
	}
}
