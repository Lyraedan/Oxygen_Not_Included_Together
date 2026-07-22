using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared;

namespace Shared
{
    public static class MP_HASHES
    {
        public static readonly ModHashes GameServer_OnStateChanged = new("Server_OnStateChanged");
        public static readonly ModHashes GameServer_OnServerStarted = new("Server_OnStarted");

        public static readonly ModHashes OnConnected = new("MP_OnConnected");
        public static readonly ModHashes OnDisconnected = new("MP_OnDisconnected");
        public static readonly ModHashes OnInSessionChanged = new("MP_OnSessionChanged");
        
        public static readonly ModHashes GameClient_OnConnectedInGame = new("MP_OnConnectedInGame");

		public static readonly ModHashes OnMultiplayerGameSessionInitialized = new("MP_OnMultiplayerSessionStarted");

        public static readonly ModHashes OnPlayerJoined = new("OnPlayerJoined");
        public static readonly ModHashes OnPlayerLeft = new("OnPlayerLeft");

		public static readonly ModHashes OnPlayerCursorCreated = new("OnCursorCreated");
	}
}
