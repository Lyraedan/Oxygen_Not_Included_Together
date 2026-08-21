using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Sandbox
{
    public class SandboxModePacket : IPacket
    {
        public bool Enabled;

        public SandboxModePacket() { }

        public SandboxModePacket(bool enabled)
        {
            Enabled = enabled;
        }

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
            writer.Write(Enabled);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
            Enabled = reader.ReadBoolean();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            ApplySandboxMode(Enabled);
            DebugConsole.Log($"[SandboxModePacket] Sandbox mode synchronized: Enabled={Enabled}");

            if (MultiplayerSession.IsHost)
            {
                PacketSender.SendToAllClients(this);
            }
        }

        public static void ApplySandboxMode(bool enabled)
        {
            if (SaveGame.Instance != null)
            {
                SaveGame.Instance.sandboxEnabled = enabled;
            }

            if (SandboxToolParameterMenu.instance != null)
            {
                SandboxToolParameterMenu.instance.gameObject.SetActive(enabled);
            }

            if (PlanScreen.Instance != null)
            {
                PlanScreen.Instance.Refresh();
            }

            if (BuildMenu.Instance != null)
            {
                BuildMenu.Instance.Refresh();
            }

            if (ManagementMenu.Instance != null)
            {
                ManagementMenu.Instance.Refresh();
            }
        }
    }
}
