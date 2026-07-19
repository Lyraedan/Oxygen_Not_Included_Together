using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Bionic;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Bionic
{
	public sealed class BionicElectrobankStatePacket : IPacket, IHostOnlyPacket
	{
		internal const float MaxHealth = 10f;
		internal const float MaxCharge = 120000f;
		internal const float MaxTimeSincePowerDrawn = 10f;
		internal const float MaxLifetime = 90000f;

		public int NetId;
		public float CurrentHealth;
		public float Charge;
		public float TimeSincePowerDrawn;
		public bool HasLifetime;
		public float LifetimeRemaining;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid bionic electrobank state");
			writer.Write(NetId);
			writer.Write(CurrentHealth);
			writer.Write(Charge);
			writer.Write(TimeSincePowerDrawn);
			writer.Write(HasLifetime);
			writer.Write(LifetimeRemaining);
		}

		public void Deserialize(BinaryReader reader)
		{
			NetId = reader.ReadInt32();
			CurrentHealth = reader.ReadSingle();
			Charge = reader.ReadSingle();
			TimeSincePowerDrawn = reader.ReadSingle();
			HasLifetime = reader.ReadBoolean();
			LifetimeRemaining = reader.ReadSingle();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid bionic electrobank state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				BionicRuntimeSync.TryApply(this);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal bool IsWireValid()
			=> NetId != 0 && IsBounded(CurrentHealth, MaxHealth) && IsBounded(Charge, MaxCharge) &&
			   IsBounded(TimeSincePowerDrawn, MaxTimeSincePowerDrawn) &&
			   IsBounded(LifetimeRemaining, MaxLifetime) && (HasLifetime || LifetimeRemaining == 0f);

		private static bool IsBounded(float value, float maximum)
			=> !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= maximum;
	}
}
