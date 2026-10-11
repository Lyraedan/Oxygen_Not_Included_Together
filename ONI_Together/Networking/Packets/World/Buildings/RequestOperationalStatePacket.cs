using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Scripts.Buildings;
using System;
using System.Collections.Generic;
using System.IO;
using Shared;
using Shared.Profiling;
using UnityEngine;
namespace ONI_Together.Networking.Packets.World.Buildings
{
	internal class RequestOperationalStatePacket : IPacket
	{
		public RequestOperationalStatePacket() { }
		public RequestOperationalStatePacket(MonoBehaviour o)
		{
			using var _ = Profiler.Scope();

			NetId = o.GetNetId();
		}

		/// <summary>0 asks for every building, answered to <see cref="Requester"/> only.</summary>
		public int NetId;
		public ulong Requester;
		public bool IsActive, IsOperational, IsFunctional;

		private static Game _subscribedGame;

		/// <summary>
		/// Client: one request for every building once the client is in game, instead of one per
		/// building while the world loads (thousands of packets, or none when it is not connected yet).
		/// </summary>
		public static void RequestAllWhenInGame()
		{
			var game = Game.Instance;
			if (game == null || game == _subscribedGame)
				return;

			_subscribedGame = game;
			game.Subscribe(MP_HASHES.GameClient_OnConnectedInGame, _ =>
				PacketSender.SendToHost(new RequestOperationalStatePacket { Requester = MultiplayerSession.LocalUserID }));
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			Requester = reader.ReadUInt64();
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(Requester);
		}

		private void SendAllToRequester()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(Requester, out var player) || player.Connection == null)
				return;

			foreach (var operational in UnityEngine.Object.FindObjectsByType<Operational>(FindObjectsSortMode.None))
			{
				var state = new OperationalStatePacket(operational);
				if (state.NetId != 0)
					PacketSender.SendToConnection(player.Connection, state);
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			if (NetId == 0)
			{
				SendAllToRequester();
				return;
			}

			if (!NetworkIdentityRegistry.TryGet(NetId, out var entity))
				return;
			if (!entity.TryGetComponent<Operational>(out var server))
				return;

			server.IsOperational = server.IsOperational;
		}
	}
}
