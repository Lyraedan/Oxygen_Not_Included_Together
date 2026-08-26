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

            if (Game.Instance != null)
            {
                try
                {
                    var m = Game.Instance.GetType().GetMethod("SetSandboxModeActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (m != null) m.Invoke(Game.Instance, new object[] { enabled });
                    else
                    {
                        var prop = Game.Instance.GetType().GetProperty("SandboxModeActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (prop != null && prop.CanWrite) prop.SetValue(Game.Instance, enabled);
                        else
                        {
                            var f = Game.Instance.GetType().GetField("sandboxModeActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                            if (f != null) f.SetValue(Game.Instance, enabled);
                        }
                    }
                } catch { }
            }

            if (SandboxToolParameterMenu.instance != null)
            {
                SandboxToolParameterMenu.instance.gameObject.SetActive(enabled);
            }

            // Fix client sandbox button not appearing - TopLeftControlScreen holds the actual toggle
            try
            {
                var topLeft = UnityEngine.Object.FindFirstObjectByType<TopLeftControlScreen>();
                if (topLeft != null && topLeft.sandboxToggle != null)
                {
                    topLeft.sandboxToggle.gameObject.SetActive(enabled);
                    try
                    {
                        var m = topLeft.GetType().GetMethod("RefreshTitleBar", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (m != null) m.Invoke(topLeft, null);
                    } catch { }
                }
                else if (topLeft != null)
                {
                    var t = topLeft.transform.Find("SandboxToggle") ?? topLeft.transform.Find("sandboxToggle");
                    if (t != null) t.gameObject.SetActive(enabled);
                }
            } catch { }

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

            // Keep WorldStateSyncer shadow in sync to avoid echo on client
            try
            {
                if (ONI_Together.Networking.Components.WorldStateSyncer.Instance != null)
                    ONI_Together.Networking.Components.WorldStateSyncer.Instance.NotifySandboxModeApplied(enabled);
            } catch { }
        }
    }
}
