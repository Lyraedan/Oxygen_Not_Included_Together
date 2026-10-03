using ONI_Together.Networking.Components;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>Applies small host-authoritative building and state-machine values.</summary>
	public sealed class AuthoritativeStateHandler : IBuildingConfigHandler
	{
		public const string HitPointsKey = "BuildingHitPoints";
		public const string EmptyConduitKey = "EmptyConduitMarked";

		private static readonly int[] _hashes =
		{
			HitPointsKey.GetHashCode(),
			EmptyConduitKey.GetHashCode(),
			StateMachineStateSyncer.ConfigKey.GetHashCode()
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			if (packet.ConfigHash == HitPointsKey.GetHashCode())
			{
				var hp = go.GetComponent<BuildingHP>();
				if (hp == null)
					return false;
				hp.SetHitPoints(Mathf.Clamp(Mathf.RoundToInt(packet.Value), 0, hp.MaxHitPoints));
				return true;
			}

			if (packet.ConfigHash == EmptyConduitKey.GetHashCode())
			{
				var workable = go.GetComponent<EmptyConduitWorkable>();
				if (workable == null)
					return false;
				if (packet.Value > 0.5f)
					workable.MarkForEmptying();
				else
					workable.CancelEmptying();
				return true;
			}

			if (packet.ConfigHash != StateMachineStateSyncer.ConfigKey.GetHashCode() ||
				string.IsNullOrEmpty(packet.StringValue))
				return false;

			int separator = packet.StringValue.IndexOf('\n');
			if (separator <= 0 || separator >= packet.StringValue.Length - 1)
				return false;

			string typeName = packet.StringValue.Substring(0, separator);
			string stateName = packet.StringValue.Substring(separator + 1);
			foreach (var controller in go.GetComponents<StateMachineController>())
			{
				if (controller == null)
					continue;

				foreach (var smi in controller.GetAllSMI<StateMachine.Instance>())
				{
					if (smi?.GetType().FullName != typeName)
						continue;
					if (smi.GetCurrentState()?.name != stateName)
						smi.GoTo(stateName);
					return true;
				}
			}

			return false;
		}
	}
}
