using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Tools
{
	internal class EmptyPipePacket : DragToolPacket
	{
		public EmptyPipePacket() : base()
		{
			using var _ = Profiler.Scope();

			ToolInstance = EmptyPipeTool.Instance;
			ToolMode = DragToolMode.OnDragTool;
		}
	}
}
