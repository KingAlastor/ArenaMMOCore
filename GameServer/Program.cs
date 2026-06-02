using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteNetLib;
using SharedLibrary;

namespace GameServer
{
    class Program : INetEventListener
    {
        private static NetManager _netManager = null!; 
        private static Dictionary<int, ServerPlayer> _players = new();
        private const float DeltaTime = 1f / 30f; // 30Hz Ticks
        private const float MoveSpeed = 5.0f;

        static void Main(string[] args)
        {
            Program server = new Program();
            _netManager = new NetManager(server) { AutoRecycle = true };
            _netManager.Start(5001); 

            Stopwatch stopwatch = Stopwatch.StartNew();
            long previousTicks = stopwatch.ElapsedTicks;
            long ticksPerFrame = (long)(Stopwatch.Frequency * DeltaTime);
            long accumulator = 0;

            Console.WriteLine("Optimized 30Hz MemoryPack Server Running...");

            // Pre-allocate a safe, recycled byte buffer for network operations outside the main loop
            byte[] scratchBuffer = new byte[1024];

            while (accumulator >= ticksPerFrame)
{
    _netManager.PollEvents();
    
    // Simulate Server-Authoritative Movements
    foreach (var player in _players.Values)
    {
        float moveX = 0;
        float moveZ = 0;

        if ((player.LastInput & (byte)InputFlags.MoveUp) != 0) moveZ += 1;
        if ((player.LastInput & (byte)InputFlags.MoveDown) != 0) moveZ -= 1;
        if ((player.LastInput & (byte)InputFlags.MoveLeft) != 0) moveX -= 1;
        if ((player.LastInput & (byte)InputFlags.MoveRight) != 0) moveX += 1;

        player.PositionX += moveX * MoveSpeed * DeltaTime;
        player.PositionZ += moveZ * MoveSpeed * DeltaTime;

        ServerStatePacket state = new ServerStatePacket
        {
            NetworkId = player.Id,
            PositionX = player.PositionX,
            PositionZ = player.PositionZ,
            Timestamp = stopwatch.ElapsedMilliseconds
        };

        // Pass our pre-allocated scratchBuffer array cleanly into MemoryPack
        NetworkSerializer.WriteStruct(scratchBuffer, ref state, out int bytesWritten);
        
        // Broadcast the exact serialized segment over LiteNetLib
        player.Peer.Send(scratchBuffer, 0, bytesWritten, DeliveryMethod.Unreliable);
    }

    accumulator -= ticksPerFrame;
}

        }

        public void OnConnectionRequest(ConnectionRequest request) => request.AcceptIfKey("MMO_Secret");
        
        public void OnPeerConnected(NetPeer peer)
        {
            Console.WriteLine($"Client connected: ID {peer.Id}");
            _players.Add(peer.Id, new ServerPlayer { Id = (uint)peer.Id, Peer = peer });
        }
        
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            Console.WriteLine($"Client disconnected: ID {peer.Id}");
            _players.Remove(peer.Id);
        }
        
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
        {
            ReadOnlySpan<byte> bytes = reader.GetRemainingBytesSpan();
            
            // MemoryPack reads the raw byte span at blistering speed
            var inputPacket = NetworkSerializer.ReadStruct<ClientInputPacket>(bytes);
            
            if (_players.TryGetValue(peer.Id, out var player))
            {
                player.LastInput = inputPacket.Inputs;
            }
        }
        
        public void OnNetworkError(System.Net.IPEndPoint ep, System.Net.Sockets.SocketError error) {}
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {}
        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint ep, NetPacketReader reader, UnconnectedMessageType type) {}
    }

    public class ServerPlayer
    {
        public uint Id;
        public required NetPeer Peer; 
        public float PositionX, PositionZ;
        public byte LastInput;
    }
}
