using ONI_Together.DebugTools;
using ONI_Together.Misc;
using Shared.Profiling;
using Steamworks;
using UnityEngine;

namespace ONI_Together.Networking.Transport.Steamworks
{
	/// <summary>
	/// Keeps Steam rich presence in sync with the same information Discord shows:
	/// colony, cycle, Duplicant count, role/transport and the current asteroid.
	/// </summary>
	public class SteamPresenceUpdater : MonoBehaviour
	{
		private const float UPDATE_INTERVAL = 5f;

		private float _nextUpdate;
		private bool _asteroidCached;
		private string _asteroidName = "";
		private Game _subscribedGame;
		private int _activeWorldChangedHandle = -1;

		private void Start()
		{
			using var _ = Profiler.Scope();

			SubscribeToActiveWorldChanged();
			App.OnPostLoadScene += OnSceneLoaded;
			Refresh(true);
		}

		private void OnDestroy()
		{
			App.OnPostLoadScene -= OnSceneLoaded;

			if (_subscribedGame != null && _activeWorldChangedHandle != -1)
			{
				_subscribedGame.Unsubscribe(ref _activeWorldChangedHandle);
				_activeWorldChangedHandle = -1;
				_subscribedGame = null;
			}

			SteamRichPresence.Clear();
		}

		private void Update()
		{
			if (Time.unscaledTime < _nextUpdate)
				return;

			_nextUpdate = Time.unscaledTime + UPDATE_INTERVAL;
			Refresh(false);
		}

		private void OnSceneLoaded()
		{
			SubscribeToActiveWorldChanged();
			_asteroidCached = false;
			Refresh(true);
		}

		private void OnActiveWorldChanged(object data)
		{
			_asteroidCached = false;
			Refresh(true);
		}

		private void SubscribeToActiveWorldChanged()
		{
			Game game = Game.Instance;
			if (game == null || game == _subscribedGame)
				return;

			if (_subscribedGame != null && _activeWorldChangedHandle != -1)
				_subscribedGame.Unsubscribe(ref _activeWorldChangedHandle);

			_subscribedGame = game;
			_activeWorldChangedHandle = game.Subscribe((int)GameHashes.ActiveWorldChanged, OnActiveWorldChanged);
		}

		private void Refresh(bool force)
		{
			using var _ = Profiler.Scope();

			if (!SteamManager.Initialized)
				return;

			SteamRichPresence.SetAsteroid(GetAsteroidName());
			SteamRichPresence.Refresh(force);
		}

		private string GetAsteroidName()
		{
			if (_asteroidCached)
				return _asteroidName;

			_asteroidCached = true;
			_asteroidName = "";

			if (!Utils.IsInGame())
				return _asteroidName;

			try
			{
				ClusterIcons.Entry? asteroid = ClusterIcons.ResolveCurrent();
				if (asteroid.HasValue)
					_asteroidName = asteroid.Value.Name ?? "";
				else
					_asteroidCached = false; // world may not be ready yet; retry on the next tick
			}
			catch (System.Exception ex)
			{
				_asteroidCached = false;
				DebugConsole.LogWarning($"[SteamPresenceUpdater] Asteroid name lookup failed: {ex.Message}");
			}

			return _asteroidName;
		}
	}
}
