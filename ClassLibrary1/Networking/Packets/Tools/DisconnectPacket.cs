using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking.Packets.Tools
{
	internal class DisconnectPacket : DragToolPacket
	{
		public DisconnectPacket() : base()
		{
			using var _ = Profiler.Scope();

			ToolInstance = DisconnectTool.Instance;
			ToolMode = DragToolMode.OnDragComplete;
		}
	}
}
