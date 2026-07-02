using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync
{
    public class OxySyncTestComponent : NetworkBehaviour
    {
        [SyncVar(Hook = nameof(OnCounterChanged), SendMode = 8)] // Reliable
        private int _counter;

        [SyncVar]
        private float _temperature = 25f;

        [SyncVar]
        private string _label = "Hello";

        [SyncVar]
        private bool _isActive = true;

        [SyncVar(SendMode = 0)] // Unreliable
        private float _unreliablePosition;

        public int Counter => _counter;
        public float Temperature => _temperature;
        public string Label => _label;
        public bool IsActive => _isActive;

        [Command]
        public void SetCounter(int value)
        {
            _counter = value;
            CallClientRpc(nameof(RpcOnCounterUpdated), value);
        }

        [Command]
        public void IncrementCounter()
        {
            _counter++;
            CallClientRpc(nameof(RpcOnCounterUpdated), _counter);
        }

        [Command(RequiresHost = true)]
        public void Reset()
        {
            _counter = 0;
            _temperature = 25f;
            _label = "Reset";
            _isActive = true;
        }

        [Command(SendMode = 0)] // Unreliable
        public void SetTemperature(float value)
        {
            _temperature = value;
        }

        [Command]
        public void SetLabel(string value)
        {
            _label = value;
        }

        [Command]
        public void ToggleActive()
        {
            _isActive = !_isActive;
        }

        [ClientRpc]
        private void RpcOnCounterUpdated(int newValue)
        {
            DebugConsole.Log($"[OxySyncTest] Counter updated to {newValue}");
        }

        [ClientRpc(SendMode = 9)] // ReliableImmediate
        private void RpcPlayEffect(string effectName)
        {
            DebugConsole.Log($"[OxySyncTest] Playing effect: {effectName}");
        }

        [TargetRpc(SendMode = 0)] // Unreliable
        private void SendDirectMessage(string message)
        {
            DebugConsole.Log($"[OxySyncTest] Direct message: {message}");
        }

        private void OnCounterChanged(int oldValue, int newValue)
        {
            DebugConsole.Log($"[OxySyncTest] Counter changed: {oldValue} -> {newValue}");
        }

        [Server]
        public void ServerOnlyLog()
        {
            DebugConsole.Log("[OxySyncTest] This only runs on the server");
        }

        [Client]
        public void ClientOnlyLog()
        {
            DebugConsole.Log("[OxySyncTest] This only runs on the client");
        }
    }
}
