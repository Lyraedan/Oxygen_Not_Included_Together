namespace ONI_Together.Networking.Packets.Social
{
	internal static partial class ScheduleSyncCoordinator
	{
		internal static void BeginHostMutationBatch()
		{
			if (!MultiplayerSession.IsHostInSession || IsApplyingAuthoritativeMutation ||
			    !TrackCurrentManager())
				return;
			_hostMutationBatchDepth++;
		}

		internal static void EndHostMutationBatch()
		{
			if (!MultiplayerSession.IsHostInSession || IsApplyingAuthoritativeMutation ||
			    _hostMutationBatchDepth <= 0)
				return;
			_hostMutationBatchDepth--;
			if (_hostMutationBatchDepth != 0 || !_hostMutationBatchDirty)
				return;
			_hostMutationBatchDirty = false;
			PublishNextRevision();
		}
	}
}
