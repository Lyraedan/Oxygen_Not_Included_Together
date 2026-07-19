using System.IO;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using Steamworks;
using UnityEngine;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.Tools.Attack;

public class AttackToolPacket : IPacket, IClientRelayable, ISenderBoundRelay
{
    private ulong        SenderId = MultiplayerSession.LocalUserID;
    private Vector2         Min;
    private Vector2         Max;
    private PrioritySetting Priority;
	public ulong RelaySenderId => SenderId;

    public AttackToolPacket()
    {
    }

    public AttackToolPacket(Vector2 min, Vector2 max)
    {
        using var _ = Profiler.Scope();

        Min = min;
        Max = max;
    }

    public void Serialize(BinaryWriter writer)
    {
        using var _ = Profiler.Scope();

        if (ToolMenu.Instance?.PriorityScreen != null)
            Priority = ToolMenu.Instance.PriorityScreen.GetLastSelectedPriority();

        writer.Write(SenderId);
        writer.Write(Min);
        writer.Write(Max);
        writer.Write((int)Priority.priority_class);
        writer.Write(Priority.priority_value);
    }

    public void Deserialize(BinaryReader reader)
    {
        using var _ = Profiler.Scope();

        SenderId = reader.ReadUInt64();
        Min      = reader.ReadVector2();
        Max      = reader.ReadVector2();
        Priority = new PrioritySetting((PriorityScreen.PriorityClass)reader.ReadInt32(), reader.ReadInt32());
    }

    public void OnDispatched()
    {
        using var _ = Profiler.Scope();

        var priorityScreen = ToolMenu.Instance?.PriorityScreen;
        if (priorityScreen == null)
        {
            DebugConsole.LogWarning("[AttackToolPacket] PriorityScreen is null in OnDispatched; applying attack without overriding priority");
            AttackTool.MarkForAttack(Min, Max, true);
            return;
        }

        Traverse        lastSelectedPriority = Traverse.Create(priorityScreen).Field("lastSelectedPriority");
        PrioritySetting prioritySetting      = lastSelectedPriority.GetValue<PrioritySetting>();

        lastSelectedPriority.SetValue(Priority);
        try
        {
            AttackTool.MarkForAttack(Min, Max, true);
        }
        finally
        {
            lastSelectedPriority.SetValue(prioritySetting);
        }
    }
}
