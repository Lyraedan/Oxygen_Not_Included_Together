using Shared.Profiling;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ONI_Together.Networking.Overlay
{
	public class NetIdActivityTracker : MonoBehaviour
	{
		public struct ActivityData
		{
			public float bytesPerSecond;
			public float lastActivityTime;
			public int totalBytes;
		}

		public static NetIdActivityTracker Instance { get; private set; }

		private readonly Dictionary<int, ActivityData> _activities = new Dictionary<int, ActivityData>();
		private readonly List<KeyValuePair<int, ActivityData>> _decayScratch = new List<KeyValuePair<int, ActivityData>>();
		private static readonly Dictionary<System.Type, FieldInfo> _netIdFieldCache = new Dictionary<System.Type, FieldInfo>();

		private const float DECAY_ALPHA = 0.9f;
		private const float ACTIVITY_TIMEOUT = 5f;
		private const float DECAY_INTERVAL = 1f;
		private float _lastDecayTime;

		private void Awake()
		{
			Instance = this;
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			float now = Time.unscaledTime;
			if (now - _lastDecayTime < DECAY_INTERVAL)
				return;
			_lastDecayTime = now;

			if (_activities.Count == 0)
				return;

			_decayScratch.Clear();
			foreach (var kvp in _activities)
			{
				float elapsed = now - kvp.Value.lastActivityTime;
				if (elapsed > ACTIVITY_TIMEOUT && kvp.Value.bytesPerSecond > 0.01f)
				{
					var updated = kvp.Value;
					updated.bytesPerSecond *= Mathf.Pow(DECAY_ALPHA, elapsed);
					if (updated.bytesPerSecond < 0.01f)
						updated.bytesPerSecond = 0f;
					_decayScratch.Add(new KeyValuePair<int, ActivityData>(kvp.Key, updated));
				}
			}
			for (int i = 0; i < _decayScratch.Count; i++)
			{
				var pair = _decayScratch[i];
				_activities[pair.Key] = pair.Value;
			}
		}

		public void RecordActivity(int netId, int bytes)
		{
			using var _ = Profiler.Scope();

			float now = Time.unscaledTime;
			if (_activities.TryGetValue(netId, out var data))
			{
				float dt = Mathf.Max(now - data.lastActivityTime, 0.016f);
				float instantBps = bytes / dt;
				data.bytesPerSecond = DECAY_ALPHA * data.bytesPerSecond + (1f - DECAY_ALPHA) * instantBps;
				data.lastActivityTime = now;
				data.totalBytes += bytes;
			}
			else
			{
				data = new ActivityData
				{
					bytesPerSecond = 0f,
					lastActivityTime = now,
					totalBytes = bytes
				};
			}
			_activities[netId] = data;
		}

		public float GetBytesPerSecond(int netId)
		{
			if (_activities.TryGetValue(netId, out var data))
				return data.bytesPerSecond;
			return 0f;
		}

		public float GetLastActivityTime(int netId)
		{
			if (_activities.TryGetValue(netId, out var data))
				return data.lastActivityTime;
			return 0f;
		}

		public int GetTotalBytes(int netId)
		{
			if (_activities.TryGetValue(netId, out var data))
				return data.totalBytes;
			return 0;
		}

		public bool HasActivity(int netId)
		{
			return _activities.ContainsKey(netId);
		}

		public static int GetNetIdFromPacket(object packet)
		{
			if (packet == null) return 0;
			var type = packet.GetType();

			if (!_netIdFieldCache.TryGetValue(type, out var field))
			{
				field = type.GetField("NetId", BindingFlags.Public | BindingFlags.Instance);
				if (field == null || field.FieldType != typeof(int))
				{
					field = type.GetField("NetID", BindingFlags.Public | BindingFlags.Instance);
					if (field != null && field.FieldType != typeof(int))
						field = null;
				}
				_netIdFieldCache[type] = field;
			}

			return field != null ? (int)field.GetValue(packet) : 0;
		}

		public void Clear()
		{
			_activities.Clear();
		}

		public Dictionary<int, ActivityData> GetAllActivities()
		{
			return _activities;
		}

		public int ActiveCount
		{
			get
			{
				int count = 0;
				float now = Time.unscaledTime;
				foreach (var kvp in _activities)
				{
					if (now - kvp.Value.lastActivityTime < ACTIVITY_TIMEOUT)
						count++;
				}
				return count;
			}
		}

		public int TotalCount => _activities.Count;
	}
}
