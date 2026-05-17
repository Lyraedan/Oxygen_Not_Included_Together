using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace Shared.Helpers
{
	public static class PacketRegistrationHelper
	{
		public static void AutoRegisterPackets(Assembly asm, Action<Type> registerPacketAction, out int count, out TimeSpan duration)
		{
			using var _ = Profiler.Scope();

			var startTime = System.DateTime.Now;

			var PacketsToRegister = asm.GetTypes().Where(p =>
				 p.GetInterfaces().Contains(typeof(IPacket)) &&
				 !p.GetInterfaces().Contains(typeof(IPacketSkipsRegistration)));

			count = PacketsToRegister.Count();
			foreach (var packetType in PacketsToRegister)
			{
				registerPacketAction(packetType);
			}

			var endTime = System.DateTime.Now;
			duration = endTime - startTime;
		}
	}
}
