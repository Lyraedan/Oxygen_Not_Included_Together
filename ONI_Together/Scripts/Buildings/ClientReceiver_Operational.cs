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
			if (MultiplayerSession.IsClient)
				PacketSender.SendToHost(new RequestOperationalStatePacket(this));
		}

		public bool IsFunctional { get; set; }

		public bool IsOperational { get; set; }

		public bool IsActive { get; set; }

		/// <summary>
		/// False until the host has actually told us what this building is doing. Every
		/// getter patch is gated on this flag, so until the first packet lands a client
		/// building answers exactly as vanilla would (unpowered = not operational during
		/// load).
		///
		/// Defaulting to "operational" instead makes Operational.UpdateOperational fire
		/// OnOperationalChanged during KMonoBehaviour.InitializeComponent - fatal for Klei
		/// handlers that assume OnSpawn has run (SweepBotStation throws, and with no mod
		/// frames on the stack ONI's crash handler disables the whole mod).
		/// </summary>
		public bool HasHostState { get; private set; }

		public void ApplyHostState(bool isOperational, bool isFunctional, bool isActive)
		{
			IsOperational = isOperational;
			IsFunctional = isFunctional;
			IsActive = isActive;
			HasHostState = true;
		}
	}
}
