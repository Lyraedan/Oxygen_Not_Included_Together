using Shared.Profiling;

namespace ONI_Together.Networking.Components
{
	using ONI_Together.DebugTools;
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using UnityEngine;

	public class MainThreadExecutor : MonoBehaviour
	{

		public static MainThreadExecutor dispatcher;
		private List<Action> events = new List<Action>();

		private void Awake()
		{
			using var _ = Profiler.Scope();

			if (dispatcher == null)
				dispatcher = this;
			else
				Destroy(this);
		}

		private void Start()
		{
			using var _ = Profiler.Scope();

			StartCoroutine(Execute());
		}

		public void QueueEvent(bool condition, Action action) => events.Add(() => StartCoroutine(WaitAndExecute(condition, action)));

		public void QueueEvent(Action action) => events.Add(action);

		IEnumerator WaitAndExecute(bool condition, Action action)
		{
			using var _ = Profiler.Scope();

			// Wait for condition to be true
			yield return new WaitUntil(() => condition);
			action?.Invoke();
		}

		// I know that this is terrible... Too bad
		IEnumerator Execute()
		{
			using var _ = Profiler.Scope();

			yield return new WaitUntil(() => events.Count > 0);
			events[0]?.Invoke();
			DebugConsole.Log("[Main/Thread] Executor executing next event @ " + DateTime.Now.ToString("hh:mm:ss"));
			yield return new WaitForSecondsRealtime(0.5f);
			events.RemoveAt(0);
			StartCoroutine(Execute());
		}
	}
}
