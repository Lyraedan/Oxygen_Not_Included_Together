using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	/// <summary>
	/// generic packet wrapper for mod API packets;
	/// each mod api registered packet type T will have its own ModApiPacket<T> type created at runtime
	/// </summary>
	/// <typeparam name="T">type of the api-registered mod class that inherits the shared IPacket</typeparam>
	internal class ModApiPacket<T> : IPacket, IModApiPacket, IPacketSkipsRegistration
	{
		public T WrappedInstance { get; private set; }
		Traverse Traverse;

		public ModApiPacket()
		{
			using var _ = Profiler.Scope();

			WrappedInstance = Activator.CreateInstance<T>();
			Traverse = Traverse.Create(WrappedInstance);
		}
		public void SetWrappedInstance(object instance)
		{
			using var _ = Profiler.Scope();

			WrappedInstance = (T)instance;
			Traverse = Traverse.Create(WrappedInstance);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Traverse.Method("Deserialize", reader).GetValue();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			Traverse.Method("OnDispatched").GetValue();
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			Traverse.Method("Serialize", writer).GetValue();
		}
	}
}
