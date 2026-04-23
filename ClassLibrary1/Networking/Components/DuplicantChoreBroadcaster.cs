using HarmonyLib;
using ONI_MP.Networking.Packets.Chores;
using Shared.Profiling;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ONI_MP.Networking.Components
{
	public class DuplicantChoreBroadcaster : KMonoBehaviour, IRender200ms
	{
		public static readonly HashSet<int> SubscribedNetIds = new();
		public static readonly HashSet<int> PendingImmediate = new();

		private const float BroadcastIntervalSeconds = 0.5f;

		private static readonly FieldInfo _providersField =
			AccessTools.Field(typeof(ChoreConsumer), "providers");
		private static readonly FieldInfo _selectedField =
			AccessTools.Field(typeof(KSelectable), "selected");

		[MyCmpGet] private NetworkIdentity identity;
		[MyCmpGet] private ChoreConsumer consumer;
		[MyCmpGet] private KSelectable selectable;

		private readonly ChoreConsumer.PreconditionSnapshot _scratchSnapshot = new();
		private float timeSinceLastBroadcast;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();
			base.OnSpawn();
			timeSinceLastBroadcast = 0f;
		}

		public void Render200ms(float dt)
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHostInSession) return;
			if (identity == null || consumer == null || selectable == null) return;
			if (!SubscribedNetIds.Contains(identity.NetId)) return;

			timeSinceLastBroadcast += dt;
			bool immediate = PendingImmediate.Remove(identity.NetId);
			if (!immediate && timeSinceLastBroadcast < BroadcastIntervalSeconds) return;
			timeSinceLastBroadcast = 0f;

			BroadcastSnapshot();
		}

		private void BroadcastSnapshot()
		{
			using var _ = Profiler.Scope();

			var providers = _providersField.GetValue(consumer) as List<ChoreProvider>;
			if (providers == null) return;

			bool wasSelected = selectable.IsSelected;
			_selectedField.SetValue(selectable, true);

			try
			{
				_scratchSnapshot.succeededContexts.Clear();
				_scratchSnapshot.failedContexts.Clear();
				consumer.consumerState.Refresh();

				for (int i = 0; i < providers.Count; i++)
					providers[i].CollectChores(consumer.consumerState, _scratchSnapshot.succeededContexts, _scratchSnapshot.failedContexts);

				_scratchSnapshot.succeededContexts.Sort();
				_scratchSnapshot.failedContexts.Sort();
			}
			finally
			{
				_selectedField.SetValue(selectable, wasSelected);
			}

			var packet = new ChoreErrandsPacket { DupeNetId = identity.NetId };
			AppendCurrentChore(packet);

			var lastContext = default(Chore.Precondition.Context);
			bool hasLastContext = false;
			AppendEntriesMerged(packet, _scratchSnapshot.succeededContexts, ref lastContext, ref hasLastContext);
			AppendEntriesMerged(packet, _scratchSnapshot.failedContexts, ref lastContext, ref hasLastContext);

			PacketSender.SendToAllClients(packet);
		}

		private void AppendCurrentChore(ChoreErrandsPacket packet)
		{
			var currentDriver = consumer.choreDriver;
			if (currentDriver == null) return;
			var current = currentDriver.GetCurrentChore();
			if (current == null || current.target.isNull) return;
			var targetGO = current.target.gameObject;
			if (targetGO == null) return;

			packet.Entries.Add(BuildEntry(current, targetGO, isCurrent: true));
		}

		private void AppendEntriesMerged(ChoreErrandsPacket packet, List<Chore.Precondition.Context> contexts,
			ref Chore.Precondition.Context lastContext, ref bool hasLastContext)
		{
			var currentDriver = consumer.choreDriver;
			for (int i = contexts.Count - 1; i >= 0 && packet.Entries.Count < ChoreErrandsPacket.MaxEntries; i--)
			{
				var ctx = contexts[i];
				if (ctx.chore == null || ctx.chore.target.isNull) continue;
				if (!ctx.IsPotentialSuccess()) continue;
				if (ctx.chore.driver == currentDriver) continue;

				var targetGO = ctx.chore.target.gameObject;
				if (targetGO == null) continue;

				if (hasLastContext && GameUtil.AreChoresUIMergeable(ctx, lastContext))
				{
					var last = packet.Entries[packet.Entries.Count - 1];
					last.MoreAmount++;
					packet.Entries[packet.Entries.Count - 1] = last;
					continue;
				}

				packet.Entries.Add(BuildEntry(ctx.chore, targetGO, isCurrent: false));
				lastContext = ctx;
				hasLastContext = true;
			}
		}

		private ErrandEntry BuildEntry(Chore chore, GameObject targetGO, bool isCurrent)
		{
			string targetLabel;
			if (targetGO == gameObject)
				targetLabel = global::STRINGS.UI.UISIDESCREENS.MINIONTODOSIDESCREEN.SELF_LABEL.text;
			else
				targetLabel = targetGO.GetProperName();

			return new ErrandEntry
			{
				ChoreTypeId = chore.choreType?.Id ?? string.Empty,
				TargetCell = Grid.PosToCell(targetGO),
				TargetLabel = targetLabel,
				PriorityClass = (int)chore.masterPriority.priority_class,
				Priority = chore.masterPriority.priority_value,
				PersonalPriority = consumer.GetPersonalPriority(chore.choreType),
				IsCurrent = isCurrent,
				IconSpriteName = ResolveIconSprite(chore.choreType)
			};
		}

		private string ResolveIconSprite(ChoreType choreType)
		{
			if (choreType == null || choreType.groups == null || choreType.groups.Length == 0)
				return string.Empty;
			var best = choreType.groups[0];
			for (int i = 1; i < choreType.groups.Length; i++)
			{
				if (consumer.GetPersonalPriority(best) < consumer.GetPersonalPriority(choreType.groups[i]))
					best = choreType.groups[i];
			}
			return best?.sprite ?? string.Empty;
		}
	}
}
