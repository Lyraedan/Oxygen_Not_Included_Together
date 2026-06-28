using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.States;
using Steamworks;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.OxySync;
using Shared.Profiling;
using UnityEngine;
using YamlDotNet.Core;

namespace ONI_Together.Networking.Packets.Core
{
	public class PlayerCursorPacket : IPacket
	{
		public ulong PlayerID;
		public Vector3 Position;
		public Color Color;
		public CursorState CursorState;

		// Building visualizer
		public string BuildingPrefabId;
		public Orientation BuildingOrientation = Orientation.Neutral;
		public bool BuildingAllowed;

		// Build area display (The <number> x <numer> display area)
		public bool Dragging = false;
		public Vector3 AreaDownPos;
		public DragTool.Mode DragMode = DragTool.Mode.Box;
		public Vector2 LengthLimit = Vector2.zero;

        // Utility path visualizer
        public bool HasUtilityPath = false;
        public uint[] UtilityPathData;

        // Viewport for targeted sync
        public int ViewMinX, ViewMinY, ViewMaxX, ViewMaxY;

        public void Serialize(BinaryWriter writer)
		{
		    using var _ = Profiler.Scope();

		    writer.Write(PlayerID);
		    writer.Write(Position);
		    writer.Write(Color);

		    ushort flags = 0;
		    flags |= (ushort)((int)CursorState & 0x1F);
		    flags |= (ushort)(((int)BuildingOrientation & 0x7) << 5);
		    flags |= (ushort)(((int)DragMode & 0x7) << 8);

		    if (BuildingAllowed)
		        flags |= 1 << 11;

		    if (Dragging)
		        flags |= 1 << 12;

		    if (HasUtilityPath)
		        flags |= 1 << 13;

		    writer.Write(flags);

		    uint viewMin = ((uint)(ushort)ViewMinX << 16) | (ushort)ViewMinY;
		    uint viewMax = ((uint)(ushort)ViewMaxX << 16) | (ushort)ViewMaxY;

		    writer.Write(viewMin);
		    writer.Write(viewMax);

		    writer.Write(BuildingPrefabId);

		    if (Dragging)
		    {
		        writer.Write(AreaDownPos);
		        writer.Write(LengthLimit);
		    }

		    if (HasUtilityPath)
		    {
		        writer.Write(UtilityPathData.Length);
		        for (int i = 0; i < UtilityPathData.Length; i++)
		            writer.Write(UtilityPathData[i]);
		    }
		}

		public void Deserialize(BinaryReader reader)
		{
		    using var _ = Profiler.Scope();

		    PlayerID = reader.ReadUInt64();
		    Position = reader.ReadVector3();
		    Color = reader.ReadColor();

		    ushort flags = reader.ReadUInt16();
		    CursorState = (CursorState)(flags & 0x1F);
		    BuildingOrientation = (Orientation)((flags >> 5) & 0x7);
		    DragMode = (DragTool.Mode)((flags >> 8) & 0x7);
		    BuildingAllowed = (flags & (1 << 11)) != 0;
		    Dragging = (flags & (1 << 12)) != 0;
		    HasUtilityPath = (flags & (1 << 13)) != 0;

		    uint viewMin = reader.ReadUInt32();
		    uint viewMax = reader.ReadUInt32();

		    ViewMinX = (short)(viewMin >> 16);
		    ViewMinY = (short)(viewMin & 0xFFFF);

		    ViewMaxX = (short)(viewMax >> 16);
		    ViewMaxY = (short)(viewMax & 0xFFFF);

		    BuildingPrefabId = reader.ReadString();

		    if (Dragging)
		    {
		        AreaDownPos = reader.ReadVector3();
		        LengthLimit = reader.ReadVector2();
		    }

		    if (HasUtilityPath)
		    {
		        int count = reader.ReadInt32();
		        UtilityPathData = new uint[count];
		        for (int i = 0; i < count; i++)
		            UtilityPathData[i] = reader.ReadUInt32();
		    }
		    else
		        UtilityPathData = null;
		}
        public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (PlayerID == MultiplayerSession.LocalUserID)
				return;

			if (MultiplayerSession.TryGetCursorObject(PlayerID, out PlayerCursor cursor))
			{
				cursor.SetState(CursorState);
				cursor.SetColor(Color);
				cursor.SetVisibility(true);
				cursor.StopCoroutine("InterpolateCursorPosition");
				cursor.StartCoroutine(InterpolateCursorPosition(cursor, cursor.transform, Position));
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

		private IEnumerator InterpolateCursorPosition(PlayerCursor cursor, Transform target, Vector3 targetPos)
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
				UpdateVisualizers(cursor, target.position);
				yield return null;
			}

			target.position = targetPos;
			UpdateVisualizers(cursor, target.position);
		}

		private void UpdateVisualizers(PlayerCursor cursor, Vector3 position)
		{
			cursor.buildingVisualiser.UpdateVisualizer(BuildingPrefabId, position, BuildingOrientation, Color, BuildingAllowed);
			cursor.areaVisualizer.UpdateArea(Color, AreaDownPos, Position, Dragging, DragMode, LengthLimit);
			cursor.utilityVisualizer.UpdatePath(BuildingPrefabId, UtilityPathData, Color);

			// Dynamically adjust the players interest group on the server based off their cursor position
			if (MultiplayerSession.IsHost)
			{
				int currentWorld = cursor.GetMyWorldId();
				if (currentWorld >= 0 && currentWorld != cursor.InterestGroup)
				{
					if (cursor.InterestGroup != -1)
						InterestGroupManager.RemovePlayerFromGroup(PlayerID, cursor.InterestGroup);
					cursor.InterestGroup = currentWorld;
					InterestGroupManager.AddPlayerToGroup(PlayerID, cursor.InterestGroup);
				}
			}
		}

	}
}
