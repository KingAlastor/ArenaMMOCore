using UnityEngine;
using System;
using LiteNetLib;
using SharedLibrary;
using Unity.Mathematics;

namespace GameClient
{
    public class NetworkClientManager : MonoBehaviour, INetEventListener
    {
        private NetManager _client;
        private NetPeer _serverPeer;
        public static NetworkClientManager Instance;

        public byte CurrentInputMask; 
        public float3 ServerPositionUpdate;
        public bool HasUpdate;

        private byte[] _sendScratchBuffer = new byte[256];

        private void Awake()
        {
            Instance = this;
            _client = new NetManager(this) { AutoRecycle = true };
            _client.Start();
            _client.Connect("localhost", 5001, "MMO_Secret");
        }

        private void Update()
        {
            _client.PollEvents();
            SendInputsToServer();
        }

        private void SendInputsToServer()
        {
        if (_serverPeer == null) return;

        byte mask = 0;
        if (Input.GetKey(KeyCode.W)) mask |= (byte)InputFlags.MoveUp;
        if (Input.GetKey(KeyCode.S)) mask |= (byte)InputFlags.MoveDown;
        if (Input.GetKey(KeyCode.A)) mask |= (byte)InputFlags.MoveLeft;
        if (Input.GetKey(KeyCode.D)) mask |= (byte)InputFlags.MoveRight;

        CurrentInputMask = mask;

        ClientInputPacket inputPacket = new ClientInputPacket { SequenceNumber = 1, Inputs = mask };

        // Pass your pre-allocated _sendScratchBuffer array straight down the pipe
        NetworkSerializer.WriteStruct(_sendScratchBuffer, ref inputPacket, out int bytesWritten);

        _serverPeer.Send(_sendScratchBuffer, 0, bytesWritten, DeliveryMethod.Unreliable);
        }


        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
        {
            ReadOnlySpan<byte> bytes = reader.GetRemainingBytesSpan();
            
            // Fast MemoryPack read directly inside the UDP listener thread
            var state = NetworkSerializer.ReadStruct<ServerStatePacket>(bytes);

            ServerPositionUpdate = new float3(state.PositionX, 0, state.PositionZ);
            HasUpdate = true;
        }

        public void OnPeerConnected(NetPeer peer) => _serverPeer = peer;
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) => _serverPeer = null;
        public void OnConnectionRequest(ConnectionRequest request) {}
        public void OnNetworkError(System.Net.IPEndPoint ep, System.Net.Sockets.SocketError err) {}
        public void OnNetworkLatencyUpdate(NetPeer peer, int lat) {}
        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint ep, NetPacketReader r, UnconnectedMessageType t) {}
        private void OnDestroy() => _client.Stop();
    }
}
