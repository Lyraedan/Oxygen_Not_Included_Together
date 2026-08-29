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
		/// False until the host has actually told us what this building is doing.
		///
		/// The getter patches read the fields above. They used to answer from IsOperational
		/// unconditionally, and it was initialised to true, so a client building reported
		/// itself as running while its own components were still being initialised. In that
		/// state Operational.UpdateOperational sees a
		/// transition into operational during KMonoBehaviour.InitializeComponent and fires
		/// OnOperationalChanged early - which is fatal for any Klei handler that assumes
		/// OnSpawn has already run. SweepBotStation.RequestNewSweepBot is one: it throws a
		/// NullReferenceException, and because the stack contains no mod frames, ONI's crash
		/// handler simply disables the whole mod.
		///
		/// Vanilla does not hit this because an unpowered building is not operational during
		/// load. Every getter patch is gated on this flag, so until the first packet lands a
		/// client building answers exactly as vanilla would.
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
