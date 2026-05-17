using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Tools.Clear
{
	public class ClearPacket : DragToolPacket
	{
		public ClearPacket()
		{
			using var _ = Profiler.Scope();

			ToolInstance = ClearTool.Instance;
			ToolMode     = DragToolMode.OnDragTool;
		}
	}
}
