using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Steamworks;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Deconstruct
{
	public class DeconstructPacket : DragToolPacket
	{
		public DeconstructPacket() : base()
		{
			using var _ = Profiler.Scope();

			ToolInstance = DeconstructTool.Instance;
			ToolMode = DragToolMode.OnDragTool;
		}
	}
}
