using System;
using UnityEngine;

namespace Shared.Helpers
{
    public static class NetIdentityHelper
    {
        public static Func<GameObject, int, int>? SetIdentity;
        public static Func<GameObject, int, int>? OverrideIdentity;
        public static Func<GameObject, int>? AddIdentity;
        public static Func<GameObject, int>? GetIdentity;

        public static int AddOrGetNetId(GameObject go, int preferredId = 0) => SetIdentity?.Invoke(go, preferredId) ?? 0;

        public static int AddNetId(GameObject go) => AddIdentity?.Invoke(go) ?? 0;

        public static int GetNetId(GameObject go) => GetIdentity?.Invoke(go) ?? 0;

        public static int OverrideNetId(GameObject go, int netId) => OverrideIdentity?.Invoke(go, netId) ?? 0;
    }
}
