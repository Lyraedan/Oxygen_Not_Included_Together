using System;
using System.IO;
using ONI_MP.DebugTools;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking.Packets.Architecture
{

	public static class PacketHandler
	{
		private static bool _readyToProcess = true;
		private static float _notReadySince = float.MaxValue;
		private const float NOT_READY_TIMEOUT = 60f;

		public static bool readyToProcess
		{
			get => _readyToProcess;
			set
			{
				if (!value)
					_notReadySince = Time.unscaledTime;
				_readyToProcess = value;
			}
		}

		public static void HandleIncoming(byte[] data)
		{
			using var _ = Profiler.Scope();

			if (!_readyToProcess)
			{
				if (Time.unscaledTime - _notReadySince > NOT_READY_TIMEOUT)
				{
					DebugConsole.LogWarning($"[PacketHandler] readyToProcess was false for >{NOT_READY_TIMEOUT}s — force-recovering");
					_readyToProcess = true;
				}
				else
				{
					return;
				}
			}

			using (var ms = new MemoryStream(data))
			{
				using (var reader = new BinaryReader(ms))
				{
					int type = (int)reader.ReadInt32();
                    if (!PacketRegistry.HasRegisteredPacket(type))
                    {
                        DebugConsole.LogError($"Invalid PacketType received: {type}", false);
                        return;
                    }

                    using var scope = Profiler.Scope();

                    var packet = PacketRegistry.Create(type);
					packet.Deserialize(reader);
					Dispatch(packet);

                    scope.End(packet.GetType().Name, data.Length);

                    PacketTracker.TrackIncoming(new PacketTracker.PacketTrackData
                    {
						packet = packet,
						size = data.Length
                    });
                }
			}
		}

		private static void Dispatch(IPacket packet)
		{
			using var _ = Profiler.Scope();

			packet.OnDispatched();
		}
	}

}