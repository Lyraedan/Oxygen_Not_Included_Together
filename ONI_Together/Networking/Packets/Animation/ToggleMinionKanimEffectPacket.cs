using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using System.Linq;
using Shared.Profiling;

public class ToggleMinionKanimEffectPacket : IPacket
{
	public int NetId;
	public bool Enable;
	public string Context; // e.g. "dig", "sleep", "build"
	public string Event;   // e.g. "LaserOn", "LaserOff", "DreamsOn"

	public void Serialize(BinaryWriter writer)
	{
		using var _ = Profiler.Scope();

		writer.Write(NetId);
		writer.Write(Enable);
		writer.Write(Context);
		writer.Write(Event);
	}

	public void Deserialize(BinaryReader reader)
	{
		using var _ = Profiler.Scope();

		NetId = reader.ReadInt32();
		Enable = reader.ReadBoolean();
		Context = reader.ReadString();
		Event = reader.ReadString();
	}

	public void OnDispatched()
	{
		using var _ = Profiler.Scope();

		if (!NetworkIdentityRegistry.TryGet(NetId, out var go)) return;

		var toggler = go.GetComponentsInChildren<KBatchedAnimEventToggler>()
				.FirstOrDefault(t => t.enableEvent == Event || t.disableEvent == Event);

		if (toggler == null)
		{
			DebugConsole.LogWarning($"[ToggleMinionEffectPacket] Toggler with event '{Event}' not found");
			return;
		}

		toggler.GetComponentInParent<AnimEventHandler>()?.SetContext(Context);

		var hash = Hash.SDBMLower(Event);
		//data gets ignored by subscriptions
		toggler.Trigger(hash, null);
	}
}
