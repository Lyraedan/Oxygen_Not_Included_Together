using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	internal class WorkableProgressPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		private const int MaxTargetTypeNameLength = 4096;
		private int TargetNetId;
		private string TargetTypeName;
		private RemoteProgressKind ProgressKind;
		private float PercentComplete;
		private bool ShowProgressBar;
		private float WorkTimeRemaining;
		private float WorkTimeTotal;
		internal ulong Revision;

		public WorkableProgressPacket() { }

		public static bool TryCreate(
			Workable workable,
			bool showProgressBar,
			out WorkableProgressPacket packet)
		{
			using var _ = Profiler.Scope();

			packet = null;
			if (workable == null || workable.IsNullOrDestroyed())
				return false;

			var candidate = new WorkableProgressPacket();
			candidate.PopulateFromWorkable(workable, showProgressBar);
			return candidate.TryFinalizeCreation(out packet);
		}

		public static bool TryCreateComplexFabricator(
			ComplexFabricator fabricator,
			bool showProgressBar,
			out WorkableProgressPacket packet)
		{
			using var _ = Profiler.Scope();

			packet = null;
			if (fabricator == null || fabricator.IsNullOrDestroyed())
				return false;

			var candidate = new WorkableProgressPacket
			{
				TargetNetId = fabricator.GetNetId(),
				TargetTypeName = fabricator.GetType().AssemblyQualifiedName,
				ProgressKind = RemoteProgressKind.ComplexFabricatorOrder,
				PercentComplete = Mathf.Clamp01(fabricator.OrderProgress),
				ShowProgressBar = showProgressBar,
				WorkTimeRemaining = showProgressBar ? 1f - Mathf.Clamp01(fabricator.OrderProgress) : 0f,
				WorkTimeTotal = 1f
			};
			return candidate.TryFinalizeCreation(out packet);
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			if (Revision == 0)
				Revision = NetworkIdentityRegistry.NextAuthorityRevision();
			NormalizeProgressState();
			if (!HasValidWireState())
				throw new InvalidDataException("Invalid workable progress state");
			writer.Write(TargetNetId);
			writer.Write(TargetTypeName ?? string.Empty);
			writer.Write((int)ProgressKind);
			writer.Write(PercentComplete);
			writer.Write(ShowProgressBar);
			writer.Write(WorkTimeRemaining);
			writer.Write(WorkTimeTotal);
			writer.Write(Revision);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			TargetNetId = reader.ReadInt32();
			TargetTypeName = reader.ReadString();
			ProgressKind = (RemoteProgressKind)reader.ReadInt32();
			PercentComplete = reader.ReadSingle();
			ShowProgressBar = reader.ReadBoolean();
			WorkTimeRemaining = reader.ReadSingle();
			WorkTimeTotal = reader.ReadSingle();
			Revision = reader.ReadUInt64();
			if (!HasValidWireState())
				throw new InvalidDataException("Invalid workable progress state");
		}

		private void NormalizeProgressState()
		{
			if (!ShowProgressBar || !IsFinite(PercentComplete)
			    || !IsFinite(WorkTimeRemaining) || !IsFinite(WorkTimeTotal)
			    || WorkTimeTotal <= 0f)
			{
				ShowProgressBar = false;
				PercentComplete = 0f;
				WorkTimeRemaining = 0f;
				WorkTimeTotal = 0f;
				return;
			}

			PercentComplete = Mathf.Clamp01(PercentComplete);
			WorkTimeRemaining = Mathf.Clamp(WorkTimeRemaining, 0f, WorkTimeTotal);
		}

		private bool TryFinalizeCreation(out WorkableProgressPacket packet)
		{
			NormalizeProgressState();
			Revision = NetworkIdentityRegistry.NextAuthorityRevision();
			if (!HasValidWireState())
			{
				packet = null;
				return false;
			}

			packet = this;
			return true;
		}

		private bool HasValidWireState()
		{
			bool knownKind = ProgressKind == RemoteProgressKind.WorkablePercent
			                 || ProgressKind == RemoteProgressKind.ComplexFabricatorOrder;
			if (TargetNetId == 0 || string.IsNullOrEmpty(TargetTypeName)
			    || TargetTypeName.Length > MaxTargetTypeNameLength
			    || !knownKind || Revision == 0 || !IsFinite(PercentComplete)
			    || !IsFinite(WorkTimeRemaining) || !IsFinite(WorkTimeTotal)
			    || PercentComplete < 0f || PercentComplete > 1f
			    || WorkTimeRemaining < 0f || WorkTimeTotal < 0f)
				return false;
			if (!ShowProgressBar)
				return PercentComplete == 0f && WorkTimeRemaining == 0f && WorkTimeTotal == 0f;
			return WorkTimeTotal > 0f && WorkTimeRemaining <= WorkTimeTotal;
		}

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;
			if (!NetworkIdentityRegistry.TryAcceptStateRevision(TargetNetId, RevisionDomain, Revision))
				return;

			if (TryApply())
				return;

			if (Game.Instance != null)
			{
				Game.Instance.StartCoroutine(RetryApply(Clone()));
			}
		}

		private void PopulateFromWorkable(Workable workable, bool showProgressBar)
		{
			using var _ = Profiler.Scope();

			TargetNetId = workable.GetNetId();
			TargetTypeName = workable.GetType().AssemblyQualifiedName;
			ProgressKind = RemoteProgressKind.WorkablePercent;
			PercentComplete = Mathf.Clamp01(workable.GetPercentComplete());
			ShowProgressBar = showProgressBar;
			WorkTimeRemaining = showProgressBar ? workable.WorkTimeRemaining : 0f;
			WorkTimeTotal = workable.GetWorkTime();
		}

		private WorkableProgressPacket Clone()
		{
			return new WorkableProgressPacket
			{
				TargetNetId = TargetNetId,
				TargetTypeName = TargetTypeName,
				ProgressKind = ProgressKind,
				PercentComplete = PercentComplete,
				ShowProgressBar = ShowProgressBar,
				WorkTimeRemaining = WorkTimeRemaining,
				WorkTimeTotal = WorkTimeTotal,
				Revision = Revision
			};
		}

		private bool TryApply()
		{
			using var _ = Profiler.Scope();
			if (!NetworkIdentityRegistry.IsCurrentStateRevision(TargetNetId, RevisionDomain, Revision))
				return true;

			switch (ProgressKind)
			{
				case RemoteProgressKind.WorkablePercent:
					return TryApplyWorkableProgress();

				case RemoteProgressKind.ComplexFabricatorOrder:
					return TryApplyComplexFabricatorProgress();

				default:
					return true;
			}
		}

		private string RevisionDomain => $"workable:{(int)ProgressKind}:{TargetTypeName}";

		internal static bool ShouldApplyRevision(ulong current, ulong incoming)
			=> NetworkIdentityRegistry.IsNewerRevision(current, incoming);

		private bool TryApplyWorkableProgress()
		{
			using var _ = Profiler.Scope();

			if (!NetworkIdentityRegistry.TryGet(TargetNetId, out var identity) || identity == null || identity.gameObject.IsNullOrDestroyed())
				return false;

			Workable workable = null;
			if (!string.IsNullOrEmpty(TargetTypeName))
			{
				var workableType = AccessTools.TypeByName(TargetTypeName);
				if (workableType == null)
					return false;

				workable = identity.gameObject.GetComponent(workableType) as Workable;
			}

			workable ??= identity.gameObject.GetComponent<Workable>();
			if (workable == null)
				return false;

			if (WorkTimeTotal > 0f && !float.IsInfinity(WorkTimeTotal) && !float.IsNaN(WorkTimeTotal))
			{
				workable.SetWorkTime(WorkTimeTotal);
				workable.WorkTimeRemaining = Mathf.Clamp(WorkTimeRemaining, 0f, WorkTimeTotal);
			}

			workable.ShowProgressBar(ShowProgressBar);
			if (ShowProgressBar)
			{
				RemoteProgressRegistry.SetProgress(TargetNetId, ProgressKind, PercentComplete, true, WorkTimeRemaining, WorkTimeTotal);
			}
			else
			{
				RemoteProgressRegistry.Clear(TargetNetId, ProgressKind, hideTarget: false);
			}

			return true;
		}

		private bool TryApplyComplexFabricatorProgress()
		{
			using var _ = Profiler.Scope();

			if (!NetworkIdentityRegistry.TryGetComponent<ComplexFabricator>(TargetNetId, out var fabricator) || fabricator == null || fabricator.gameObject.IsNullOrDestroyed())
				return false;

			fabricator.OrderProgress = Mathf.Clamp01(PercentComplete);
			fabricator.ShowProgressBar(ShowProgressBar);
			if (ShowProgressBar)
			{
				RemoteProgressRegistry.SetProgress(TargetNetId, ProgressKind, PercentComplete, true, WorkTimeRemaining, WorkTimeTotal);
			}
			else
			{
				RemoteProgressRegistry.Clear(TargetNetId, ProgressKind, hideTarget: false);
			}

			return true;
		}

		private static IEnumerator RetryApply(WorkableProgressPacket packet)
		{
			for (int attempt = 0; attempt < 12; attempt++)
			{
				yield return null;

				if (!MultiplayerSession.InSession || MultiplayerSession.IsHost)
					yield break;

				if (packet.TryApply())
					yield break;
			}

			DebugConsole.LogWarning($"[WorkableProgressPacket] Failed to resolve target {packet.TargetNetId} ({packet.TargetTypeName})");
		}
	}
}
