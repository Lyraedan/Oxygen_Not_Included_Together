using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Bionic;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.Bionic
{
	public sealed class BionicExplosionVelocityCorrection
	{
		public int TargetNetId;
		public Vector2 Velocity;

		internal bool IsWireValid(float maximum)
			=> TargetNetId != 0 && IsBounded(Velocity.x, maximum) && IsBounded(Velocity.y, maximum);

		private static bool IsBounded(float value, float maximum)
			=> !float.IsNaN(value) && !float.IsInfinity(value) && value >= -maximum && value <= maximum;
	}

	public sealed class BionicExplosionVelocityPacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxCorrections = 512;
		internal const float MaxVelocity = 64f;
		public int ExplosionNetId;
		public int Sequence;
		public List<BionicExplosionVelocityCorrection> Corrections = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid bionic explosion velocity outcome");
			writer.Write(ExplosionNetId);
			writer.Write(Sequence);
			writer.Write(Corrections.Count);
			foreach (BionicExplosionVelocityCorrection correction in Corrections)
			{
				writer.Write(correction.TargetNetId);
				writer.Write(correction.Velocity.x);
				writer.Write(correction.Velocity.y);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			ExplosionNetId = reader.ReadInt32();
			Sequence = reader.ReadInt32();
			int count = reader.ReadInt32();
			if (count <= 0 || count > MaxCorrections)
				throw new InvalidDataException("Invalid bionic explosion velocity count");
			Corrections = new List<BionicExplosionVelocityCorrection>(count);
			for (int i = 0; i < count; i++)
			{
				Corrections.Add(new BionicExplosionVelocityCorrection
				{
					TargetNetId = reader.ReadInt32(),
					Velocity = new Vector2(reader.ReadSingle(), reader.ReadSingle())
				});
			}
			if (!IsWireValid())
				throw new InvalidDataException("Invalid bionic explosion velocity outcome");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				BionicExplosionSync.TryApplyVelocities(this);
		}

		internal bool IsWireValid()
		{
			if (ExplosionNetId == 0 || Sequence <= 0 || Corrections == null ||
			    Corrections.Count <= 0 || Corrections.Count > MaxCorrections)
				return false;
			var targets = new HashSet<int>();
			foreach (BionicExplosionVelocityCorrection correction in Corrections)
			{
				if (correction == null || !correction.IsWireValid(MaxVelocity) ||
				    !targets.Add(correction.TargetNetId))
					return false;
			}
			return true;
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
