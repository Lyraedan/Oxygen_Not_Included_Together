using ONI_MP.Misc;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Networking.States;
using Steamworks;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking;

namespace ONI_MP.Networking.Packets.Core
{
	public class PlayerCursorPacket : IPacket
	{
		public ulong PlayerID;
		public Vector3 Position;
		public Color Color;
		public CursorState CursorState;

		// Viewport for targeted sync
		public int ViewMinX, ViewMinY, ViewMaxX, ViewMaxY;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(PlayerID);
			writer.Write(Position);
			writer.Write(Color);
			writer.Write((int)CursorState);
			writer.Write(ViewMinX);
			writer.Write(ViewMinY);
			writer.Write(ViewMaxX);
			writer.Write(ViewMaxY);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			PlayerID = reader.ReadUInt64();
			Position = reader.ReadVector3();
			Color = reader.ReadColor();
			CursorState = (CursorState)reader.ReadInt32();
			ViewMinX = reader.ReadInt32();
			ViewMinY = reader.ReadInt32();
			ViewMaxX = reader.ReadInt32();
			ViewMaxY = reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (PlayerID == MultiplayerSession.LocalUserID)
				return;

			if (MultiplayerSession.TryGetCursorObject(PlayerID, out PlayerCursor cursor))
			{
				if (cursor != null)
				{
                    cursor.SetState(CursorState);
                    cursor.SetColor(Color);
                    cursor.SetVisibility(true);
                    cursor.StopCoroutine("InterpolateCursorPosition");
                    cursor.StartCoroutine(InterpolateCursorPosition(cursor.transform, Position));
				}
			}
			else
			{
				if (Utils.IsInGame())
				{
					MultiplayerSession.CreateNewPlayerCursor(PlayerID); // Create a cursor if one doesn't exist.
				}
			}


			// Forward to others if host
			if (MultiplayerSession.IsHost)
			{
				// Update Viewport in Syncer
				if (WorldStateSyncer.Instance != null)
				{
					WorldStateSyncer.Instance.UpdateClientView(PlayerID, ViewMinX, ViewMinY, ViewMaxX, ViewMaxY);
				}

				PacketSender.SendToAllOtherPeers(this);
			}
		}

		private IEnumerator InterpolateCursorPosition(Transform target, Vector3 targetPos)
		{
			using var _ = Profiler.Scope();

			Vector3 start = target.position;
			float duration = CursorManager.SendInterval;
			float elapsed = 0f;

			while (elapsed < duration)
			{
				elapsed += Time.unscaledDeltaTime;
				float t = elapsed / duration;
				target.position = Vector3.Lerp(start, targetPos, t);
				yield return null;
			}

			target.position = targetPos;
		}

	}
}
