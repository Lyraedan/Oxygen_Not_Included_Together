using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using UnityEngine;
using static STRINGS.INPUT_BINDINGS;

namespace ONI_Together.Networking.Packets.Tools
{
	public abstract class DragToolPacket : IPacket, IBulkablePacket, IClientRelayable
	{
		// Per-cell OnDragTool fires once per frame during a drag. Batching
		// coalesces the fan-out leg (host -> N clients) and the host-receive
		// side so a 60-cell drag becomes ~1 bulk message instead of 60.
		public int MaxPackSize => 64;
		public uint IntervalMs => 100;

		/// <summary>
		/// Gets a value indicating whether incoming messages are currently being processed.
		/// Use in patches to prevent recursion when applying tool changes.
		/// </summary>
		public static bool ProcessingIncoming { get; private set; } = false;

		private static long _restoredCount;

		public enum DragToolMode
		{
			Invalid = -1,
			OnDragTool = 0,
			OnDragComplete = 1
		}

		///set these two in the derived tool packet
		protected DragToolMode ToolMode = DragToolMode.Invalid;
		protected DragTool ToolInstance;

		HashSet<string> currentFilterTargets = [];
		public Vector3 downPos, upPos;
		public int cell, distFromOrigin;
		private PrioritySetting Priority;

		public virtual void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			if (ToolMenu.Instance?.PriorityScreen != null)
				Priority = ToolMenu.Instance.PriorityScreen.GetLastSelectedPriority();

			if(ToolInstance is FilteredDragTool filteredToolInstance)
				StoreFilterData(filteredToolInstance);

			if (ToolInstance is FilteredDragTool)
			{
				writer.Write(currentFilterTargets.Count);
				foreach (var target in currentFilterTargets)
				{
					writer.Write(target);
				}
			}

			switch (ToolMode)
			{
				case DragToolMode.OnDragTool:
					writer.Write(cell);
					writer.Write(distFromOrigin);
					break;
				case DragToolMode.OnDragComplete:
					writer.Write(downPos.x); writer.Write(downPos.y); writer.Write(downPos.z);
					writer.Write(upPos.x); writer.Write(upPos.y); writer.Write(upPos.z);
					break;
			}

			writer.Write((int)Priority.priority_class);
			writer.Write(Priority.priority_value);
		}

		public virtual void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			if (ToolInstance is FilteredDragTool)
			{
				var count = reader.ReadInt32();
				currentFilterTargets = new HashSet<string>(count);
				for (int i = 0; i < count; i++)
				{
					currentFilterTargets.Add(reader.ReadString());
				}
			}

			switch (ToolMode)
			{
				case DragToolMode.OnDragTool:
					cell = reader.ReadInt32();
					distFromOrigin = reader.ReadInt32();
					break;
				case DragToolMode.OnDragComplete:
					downPos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					upPos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					break;
			}

			Priority = new PrioritySetting((PriorityScreen.PriorityClass)reader.ReadInt32(), reader.ReadInt32());
		}

		public virtual void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (ToolInstance == null)
			{
				DebugConsole.LogWarning("[FilteredDragToolPacket] ToolInstance is null in OnDispatched");
				return;
			}

			FilteredDragTool filteredToolInstance = ToolInstance as FilteredDragTool;
			bool             isFilteredTool       = filteredToolInstance != null;
			HashSet<string>  cachedFilters        = [];
			if (isFilteredTool)
			{
				cachedFilters = filteredToolInstance.currentFilterTargets?.Keys.ToHashSet();
				ApplyFilterData(filteredToolInstance, currentFilterTargets);
			}

			var priorityScreen = ToolMenu.Instance?.PriorityScreen;
			Traverse lastSelectedPriority = null;
			PrioritySetting prioritySetting = default;
			bool hasPriorityScreen = priorityScreen != null;
			if (hasPriorityScreen)
			{
				lastSelectedPriority = Traverse.Create(priorityScreen).Field("lastSelectedPriority");
				prioritySetting = lastSelectedPriority.GetValue<PrioritySetting>();
				lastSelectedPriority.SetValue(Priority);
			}
			else
			{
				DebugConsole.LogWarning("[FilteredDragToolPacket] PriorityScreen is null in OnDispatched; applying tool without overriding priority");
			}

			Vector3 cachedDownPos = ToolInstance.downPos;
			ProcessingIncoming = true;
			bool completed = false;
			try
			{
				switch (ToolMode)
				{
					case DragToolMode.OnDragTool:
						DebugConsole.Log($"[FilteredDragToolPacket] OnDispatched OnDragTool - cell: {cell}, distFromOrigin: {distFromOrigin}");
						ToolInstance.OnDragTool(cell, distFromOrigin);
						break;
					case DragToolMode.OnDragComplete:
						ToolInstance.downPos = downPos;
						DebugConsole.Log($"[FilteredDragToolPacket] OnDispatched OnDragComplete - startPos: {downPos}, endPos: {upPos}");
						ToolInstance.OnDragComplete(downPos, upPos);
						break;
					default:
						DebugConsole.LogWarning("[FilteredDragToolPacket] OnDispatched called with invalid ToolMode");
						break;
				}
				completed = true;
			}
			finally
			{
				ToolInstance.downPos = cachedDownPos;
				// Always restore; otherwise a throw inside the tool's OnDragTool leaves the
				// receiver-side guard stuck at true and every subsequent drag is silently dropped.
				ProcessingIncoming = false;
				if (!completed)
				{
					long n = System.Threading.Interlocked.Increment(ref _restoredCount);
					if (n <= 5 || n % 100 == 0)
						DebugConsole.LogWarning($"[DragTool] ProcessingIncoming restored after exception #{n}");
				}
				if (hasPriorityScreen)
					lastSelectedPriority.SetValue(prioritySetting);

				if (isFilteredTool)
					ApplyFilterData(filteredToolInstance, cachedFilters);
			}
		}
		public void ApplyFilterData(FilteredDragTool tool, HashSet<string> targets)
		{
			using var _ = Profiler.Scope();

			var currentFilterKeys = tool.currentFilterTargets.Keys.ToList();

			foreach (var target in currentFilterKeys)
			{
				tool.currentFilterTargets[target] = ToolParameterMenu.ToggleState.Off;
			}
			foreach(var target in targets)
			{
				tool.currentFilterTargets[target] = ToolParameterMenu.ToggleState.On;
			}
		}

		public void StoreFilterData(FilteredDragTool tool)
		{
			using var _ = Profiler.Scope();

			foreach (var target in tool.currentFilterTargets)
			{
				if (target.Value == ToolParameterMenu.ToggleState.On)
					currentFilterTargets.Add(target.Key);
			}
		}
	}
}
