using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ONI_Together.Networking.Packets.Architecture
{
	/// <summary>
	/// interface to check if a packet is a ModApiPacket, because generic types cannot be used in "is" checks
	/// </summary>
	internal interface IModApiPacket
	{
		public void SetWrappedInstance(object instance);
	}
}
