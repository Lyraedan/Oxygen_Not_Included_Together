using System;
using System.Linq;
using UnityEngine;

namespace ONI_Together.Networking.Synchronization
{
	internal static class DuplicantDeathSync
	{
		internal const int MaxDeathIdLength = 128;

		internal readonly struct RollbackState
		{
			internal readonly bool MonitorIsDead;
			internal readonly bool HasDeadTag;
			internal readonly bool HasCorpseTag;
			internal readonly bool InLiveRoster;
			internal readonly bool InModelRoster;
			internal readonly string DeathId;

			internal RollbackState(
				bool monitorIsDead, bool hasDeadTag, bool hasCorpseTag,
				bool inLiveRoster, bool inModelRoster, string deathId)
			{
				MonitorIsDead = monitorIsDead;
				HasDeadTag = hasDeadTag;
				HasCorpseTag = hasCorpseTag;
				InLiveRoster = inLiveRoster;
				InModelRoster = inModelRoster;
				DeathId = deathId;
			}
		}

		internal static bool TryCapture(
			GameObject gameObject, out bool isDead, out string deathId)
		{
			isDead = false;
			deathId = string.Empty;
			if (!TryGetDuplicant(gameObject, out _, out KPrefabID prefab, out DeathMonitor.Instance monitor))
				return false;
			isDead = IsDeadState(prefab.HasTag(GameTags.Dead), monitor.IsDead());
			if (!isDead)
				return true;
			deathId = GetDeathId(monitor);
			return IsValidDeathId(deathId);
		}

		internal static bool Apply(GameObject gameObject, bool isDead, string deathId)
		{
			if (!TryGetDuplicant(gameObject, out MinionIdentity identity,
				    out KPrefabID prefab, out DeathMonitor.Instance monitor))
				return false;
			if (!isDead)
				return ApplyAlive(identity, prefab, monitor);
			if (!IsValidDeathId(deathId))
				return false;
			Death death = Db.Get().Deaths.TryGet(deathId);
			if (!ResolvedDeathMatches(deathId, death?.Id))
				return false;
			bool alreadyDead = monitor.IsDead();
			string currentDeathId = GetDeathId(monitor);
			if (!alreadyDead)
				monitor.Kill(death);
			else if (!string.Equals(currentDeathId, death.Id, StringComparison.Ordinal))
				monitor.sm.death.Set(death, monitor);
			if (!monitor.IsDead())
				monitor.GoTo(monitor.sm.dead);
			prefab.AddTag(GameTags.Dead);
			prefab.AddTag(GameTags.Corpse);
			RemoveFromLiveRoster(identity);
			return SnapshotMatches(gameObject, true, death.Id);
		}

		internal static bool CanApply(GameObject gameObject, bool isDead, string deathId)
		{
			if (!TryGetDuplicant(gameObject, out _, out _, out _))
				return false;
			if (!isDead)
				return string.IsNullOrEmpty(deathId);
			if (!IsValidDeathId(deathId))
				return false;
			Death death = Db.Get().Deaths.TryGet(deathId);
			return ResolvedDeathMatches(deathId, death?.Id);
		}

		internal static bool TryCaptureRollbackState(
			GameObject gameObject, out RollbackState state)
		{
			state = default;
			if (!TryGetDuplicant(gameObject, out MinionIdentity identity,
				    out KPrefabID prefab, out DeathMonitor.Instance monitor))
				return false;
			state = new RollbackState(
				monitor.IsDead(), prefab.HasTag(GameTags.Dead),
				prefab.HasTag(GameTags.Corpse), IsInLiveRoster(identity),
				IsInLiveRosterByModel(identity), GetDeathId(monitor));
			return true;
		}

		internal static bool RestoreRollbackState(
			GameObject gameObject, RollbackState state)
		{
			if (!TryGetDuplicant(gameObject, out MinionIdentity identity,
				    out KPrefabID prefab, out DeathMonitor.Instance monitor))
				return false;
			if (state.MonitorIsDead)
			{
				Death death = Db.Get().Deaths.TryGet(state.DeathId);
				if (!ResolvedDeathMatches(state.DeathId, death?.Id))
					return false;
				monitor.sm.death.Set(death, monitor);
				if (!monitor.IsDead())
					monitor.GoTo(monitor.sm.dead);
			}
			else if (monitor.IsDead())
				monitor.GoTo(monitor.sm.alive);
			SetTag(prefab, GameTags.Dead, state.HasDeadTag);
			SetTag(prefab, GameTags.Corpse, state.HasCorpseTag);
			SetLiveRoster(identity, state.InLiveRoster);
			SetModelRoster(identity, state.InModelRoster);
			return monitor.IsDead() == state.MonitorIsDead
			       && prefab.HasTag(GameTags.Dead) == state.HasDeadTag
			       && prefab.HasTag(GameTags.Corpse) == state.HasCorpseTag
			       && IsInLiveRoster(identity) == state.InLiveRoster
			       && IsInLiveRosterByModel(identity) == state.InModelRoster;
		}

		internal static bool SnapshotMatches(
			GameObject gameObject, bool expectedDead, string expectedDeathId)
		{
			if (!TryGetDuplicant(gameObject, out MinionIdentity identity,
				    out KPrefabID prefab, out DeathMonitor.Instance monitor))
				return false;
			bool hasDeadTag = prefab.HasTag(GameTags.Dead);
			bool monitorIsDead = monitor.IsDead();
			bool corpse = prefab.HasTag(GameTags.Corpse);
			bool inLiveRoster = IsInLiveRoster(identity);
			bool inModelRoster = IsInLiveRosterByModel(identity);
			if (!SnapshotFieldsMatch(
				    expectedDead, hasDeadTag, monitorIsDead, corpse,
				    inLiveRoster, inModelRoster))
				return false;
			if (!expectedDead)
				return true;
			string actualDeathId = GetDeathId(monitor);
			return string.Equals(actualDeathId, expectedDeathId, StringComparison.Ordinal);
		}

		internal static bool SnapshotFieldsMatch(
			bool expectedDead, bool hasDeadTag, bool monitorIsDead, bool corpse,
			bool inLiveRoster, bool inModelRoster)
			=> hasDeadTag == expectedDead
			   && monitorIsDead == expectedDead
			   && corpse == expectedDead
			   && inLiveRoster == inModelRoster
			   && inLiveRoster != expectedDead;

		internal static bool IsInLiveRoster(MinionIdentity identity)
			=> identity != null && global::Components.LiveMinionIdentities.Items.Contains(identity);

		internal static bool IsInLiveRosterByModel(MinionIdentity identity)
			=> identity != null
			   && global::Components.LiveMinionIdentitiesByModel.TryGetValue(
				   identity.model, out global::Components.Cmps<MinionIdentity> byModel)
			   && byModel.Items.Contains(identity);

		internal static string GetDeathId(DeathMonitor.Instance monitor)
			=> monitor?.sm.death.Get(monitor)?.Id ?? Db.Get().Deaths.Generic.Id;

		internal static bool IsValidDeathId(string deathId)
			=> !string.IsNullOrEmpty(deathId) && deathId.Length <= MaxDeathIdLength;

		internal static bool IsDeadState(bool hasDeadTag, bool monitorIsDead)
			=> hasDeadTag || monitorIsDead;

		internal static string CanonicalDeathId(bool isDead, string deathId)
			=> isDead ? deathId ?? string.Empty : string.Empty;

		internal static bool ResolvedDeathMatches(string requestedId, string resolvedId)
			=> IsValidDeathId(requestedId)
			   && string.Equals(requestedId, resolvedId, StringComparison.Ordinal);

		private static bool ApplyAlive(
			MinionIdentity identity, KPrefabID prefab, DeathMonitor.Instance monitor)
		{
			if (monitor.IsDead() || prefab.HasTag(GameTags.Dead))
				monitor.GoTo(monitor.sm.alive);
			prefab.RemoveTag(GameTags.Dead);
			prefab.RemoveTag(GameTags.Corpse);
			AddToLiveRoster(identity);
			return SnapshotMatches(identity.gameObject, false, string.Empty);
		}

		private static bool TryGetDuplicant(
			GameObject gameObject, out MinionIdentity identity,
			out KPrefabID prefab, out DeathMonitor.Instance monitor)
		{
			identity = null;
			prefab = null;
			monitor = null;
			if (gameObject == null || gameObject.IsNullOrDestroyed()
			    || !gameObject.TryGetComponent(out identity)
			    || !gameObject.TryGetComponent(out prefab)
			    || !prefab.HasTag(GameTags.BaseMinion))
				return false;
			monitor = gameObject.GetSMI<DeathMonitor.Instance>();
			return monitor != null;
		}

		private static void AddToLiveRoster(MinionIdentity identity)
		{
			if (!IsInLiveRoster(identity))
				global::Components.LiveMinionIdentities.Add(identity);
			if (!global::Components.LiveMinionIdentitiesByModel.TryGetValue(
				    identity.model, out global::Components.Cmps<MinionIdentity> byModel))
			{
				byModel = new global::Components.Cmps<MinionIdentity>();
				global::Components.LiveMinionIdentitiesByModel.Add(identity.model, byModel);
			}
			if (!byModel.Items.Contains(identity))
				byModel.Add(identity);
		}

		private static void RemoveFromLiveRoster(MinionIdentity identity)
		{
			global::Components.LiveMinionIdentities.Remove(identity);
			if (global::Components.LiveMinionIdentitiesByModel.TryGetValue(identity.model, out var byModel))
				byModel.Remove(identity);
		}

		private static void SetTag(KPrefabID prefab, Tag tag, bool present)
		{
			if (present)
				prefab.AddTag(tag);
			else
				prefab.RemoveTag(tag);
		}

		private static void SetLiveRoster(MinionIdentity identity, bool present)
		{
			if (present && !IsInLiveRoster(identity))
				global::Components.LiveMinionIdentities.Add(identity);
			else if (!present)
				global::Components.LiveMinionIdentities.Remove(identity);
		}

		private static void SetModelRoster(MinionIdentity identity, bool present)
		{
			if (!global::Components.LiveMinionIdentitiesByModel.TryGetValue(
				    identity.model, out global::Components.Cmps<MinionIdentity> byModel))
			{
				if (!present)
					return;
				byModel = new global::Components.Cmps<MinionIdentity>();
				global::Components.LiveMinionIdentitiesByModel.Add(identity.model, byModel);
			}
			if (present && !byModel.Items.Contains(identity))
				byModel.Add(identity);
			else if (!present)
				byModel.Remove(identity);
		}
	}
}
