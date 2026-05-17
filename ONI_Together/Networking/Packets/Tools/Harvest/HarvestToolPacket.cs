using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Tools.Harvest;

public class HarvestToolPacket : DragToolPacket
{
    public HarvestToolPacket()
    {
        using var _ = Profiler.Scope();

        ToolInstance = HarvestTool.Instance;
        ToolMode     = DragToolMode.OnDragTool;
    }
}