using System.Collections.Generic;
using ONI_Together.Networking.OxySync.Packets;
using Steamworks;

namespace ONI_Together.Networking.OxySync
{
    public static class InterestGroupManager
    {
        private static readonly Dictionary<ulong, HashSet<int>> _playerGroups = new();

        public static void AddPlayerToGroup(ulong playerId, int groupId)
        {
            if (!_playerGroups.TryGetValue(playerId, out var groups))
            {
                groups = new HashSet<int>();
                _playerGroups[playerId] = groups;
            }
            groups.Add(groupId);
        }

        public static void RemovePlayerFromGroup(ulong playerId, int groupId)
        {
            if (_playerGroups.TryGetValue(playerId, out var groups))
            {
                groups.Remove(groupId);
                if (groups.Count == 0)
                    _playerGroups.Remove(playerId);
            }
        }

        public static void ClearPlayer(ulong playerId)
        {
            _playerGroups.Remove(playerId);
        }

        public static bool IsPlayerInGroup(ulong playerId, int groupId)
        {
            if (groupId == -1) return true;
            return _playerGroups.TryGetValue(playerId, out var groups) && groups.Contains(groupId);
        }

        public static IEnumerable<ulong> GetGroupMemberIds(int groupId)
        {
            if (groupId == -1)
            {
                foreach (var id in MultiplayerSession.ConnectedPlayers.Keys)
                    yield return id;
                yield break;
            }

            foreach (var kvp in _playerGroups)
            {
                if (kvp.Value.Contains(groupId))
                    yield return kvp.Key;
            }
        }

        public static List<ulong> GetPlayersInGroup(int groupId)
        {
            var result = new List<ulong>();
            if (groupId == -1)
            {
                result.AddRange(MultiplayerSession.ConnectedPlayers.Keys);
                return result;
            }

            foreach (var kvp in _playerGroups)
            {
                if (kvp.Value.Contains(groupId))
                    result.Add(kvp.Key);
            }
            return result;
        }

        public static List<int> GetGroupsPlayerIsIn(ulong playerId)
        {
            if (_playerGroups.TryGetValue(playerId, out var groups))
                return new List<int>(groups);
            return new List<int>();
        }

        public static void SubscribeToGroup(int groupId)
        {
            AddPlayerToGroup(MultiplayerSession.LocalUserID, groupId);
            if (MultiplayerSession.InSession && !MultiplayerSession.IsHost)
            {
                PacketSender.SendToHost(new InterestGroupSubscribePacket
                {
                    SenderId = MultiplayerSession.LocalUserID,
                    GroupId = groupId,
                    Subscribe = true,
                });
            }
        }

        public static void UnsubscribeFromGroup(int groupId)
        {
            RemovePlayerFromGroup(MultiplayerSession.LocalUserID, groupId);
            if (MultiplayerSession.InSession && !MultiplayerSession.IsHost)
            {
                PacketSender.SendToHost(new InterestGroupSubscribePacket
                {
                    SenderId = MultiplayerSession.LocalUserID,
                    GroupId = groupId,
                    Subscribe = false,
                });
            }
        }
    }
}
