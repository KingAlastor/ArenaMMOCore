using UnityEngine;
using System;
using LiteNetLib;
using SharedLibrary;
using Unity.Mathematics;

namespace GameClient
{
    /// <summary>
    /// Unity-side transport bridge that polls LiteNetLib and exposes latest server snapshots to ECS systems.
    /// </summary>
    public class NetworkClientManager : MonoBehaviour, INetEventListener
    {
        // LiteNetLib manager used by the main thread Unity loop.
        private NetManager _client;

        // Cached server peer once the connection handshake completes.
        private NetPeer _serverPeer;

        // Global access point for ECS systems that cannot hold managed references.
        public static NetworkClientManager Instance;

        // Last input bitmask sent to the server for local debugging and diagnostics.
        public byte CurrentInputMask;

        // Latest authoritative world position from the server snapshot stream.
        public float3 ServerPositionUpdate;

        // Snapshot timestamp in server milliseconds for interpolation timing.
        public long ServerTimestampUpdate;

        // Handshake flag consumed by ECS systems to know when a fresh snapshot arrived.
        public bool HasUpdate;

        // Reusable outbound packet buffer to avoid per-frame allocations.
        private byte[] _sendScratchBuffer = new byte[256];

        private void Awake()
        {
            Instance = this;

            // Configure client transport and open local socket before connecting.
            _client = new NetManager(this) { AutoRecycle = true };
            _client.Start();
            _client.Connect("localhost", 5001, "MMO_Secret");
        }

        private void Update()
        {
            // Poll inbound packets and dispatch callbacks on the Unity main thread.
            _client.PollEvents();

            // Package and replicate local input every frame.
            SendInputsToServer();
        }

        private void SendInputsToServer()
        {
            if (_serverPeer == null) return;

            // Build compact input bitmask from keyboard state.
            byte mask = 0;
            if (Input.GetKey(KeyCode.W)) mask |= (byte)InputFlags.MoveUp;
            if (Input.GetKey(KeyCode.S)) mask |= (byte)InputFlags.MoveDown;
            if (Input.GetKey(KeyCode.A)) mask |= (byte)InputFlags.MoveLeft;
            if (Input.GetKey(KeyCode.D)) mask |= (byte)InputFlags.MoveRight;

            CurrentInputMask = mask;

            ClientInputPacket inputPacket = new ClientInputPacket { SequenceNumber = 1, Inputs = mask };

            // Serialize directly into the shared outbound scratch array.
            NetworkSerializer.WriteStruct(_sendScratchBuffer, ref inputPacket, out int bytesWritten);

            _serverPeer.Send(_sendScratchBuffer, 0, bytesWritten, DeliveryMethod.Unreliable);
        }


        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
        {
            ReadOnlySpan<byte> bytes = reader.GetRemainingBytesSpan();

            // Deserialize authoritative snapshot and expose it for ECS interpolation.
            var state = NetworkSerializer.ReadStruct<ServerStatePacket>(bytes);

            ServerPositionUpdate = new float3(state.PositionX, 0, state.PositionZ);
            ServerTimestampUpdate = state.Timestamp;
            HasUpdate = true;
        }

        // Store the transport peer after successful handshake.
        public void OnPeerConnected(NetPeer peer) => _serverPeer = peer;

        // Clear peer reference on disconnect to stop outbound sends.
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) => _serverPeer = null;

        // Connection requests are not used on the client side because this process initiates outbound only.
        public void OnConnectionRequest(ConnectionRequest request) {}

        // Optional transport diagnostics hooks are intentionally no-op for now.
        public void OnNetworkError(System.Net.IPEndPoint ep, System.Net.Sockets.SocketError err) {}
        public void OnNetworkLatencyUpdate(NetPeer peer, int lat) {}
        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint ep, NetPacketReader r, UnconnectedMessageType t) {}

        // Always stop sockets when Unity tears down this behaviour.
        private void OnDestroy() => _client.Stop();
    }
}
