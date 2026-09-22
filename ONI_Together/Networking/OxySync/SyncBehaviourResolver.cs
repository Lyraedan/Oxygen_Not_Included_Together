using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;

namespace ONI_Together.Networking.OxySync
{
    /// <summary>
    /// Resolves a behaviour for an inbound OxySync packet, checking the manager registry first
    /// (native + API-bridged) and falling back to the entity's NetworkIdentity components.
    /// </summary>
    internal static class SyncBehaviourResolver
    {
        public static bool TryResolve(int netId, int behaviourId, out ISyncBehaviour behaviour)
        {
            if (OxySyncManager.TryGetSyncBehaviour(netId, behaviourId, out behaviour) && behaviour != null)
                return true;

            // Native fallback: behaviour may not have registered (e.g. spawned before manager).
            if (NetworkIdentityRegistry.TryGetComponent<Shared.OxySync.NetworkBehaviour>(netId, out var native)
                && native != null)
            {
                behaviour = new NativeSyncBehaviour(native);
                return true;
            }

            // API-bridge fallback: behaviour lives in the ONI_Together_API assembly.
            if (OxySync_API_Helper.TryGetForeignBehaviour(netId, behaviourId, out behaviour) && behaviour != null)
                return true;

            behaviour = null;
            return false;
        }
    }
}
