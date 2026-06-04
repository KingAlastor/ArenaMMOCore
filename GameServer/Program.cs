using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using LiteNetLib;
using SharedLibrary;

namespace GameServer
{
    /// <summary>
    /// Selects the simulation profile used by the game server.
    /// </summary>
    public enum ServerMode
    {
        /// <summary>
        /// Fast-tick arena profile optimized for smaller sessions.
        /// </summary>
        Arena,

        /// <summary>
        /// MMO profile tuned for larger worlds and lower tick frequency.
        /// </summary>
        MMO
    }

    /// <summary>
    /// Runtime switches loaded from configuration and command-line arguments.
    /// </summary>
    public sealed class ServerRuntimeConfig
    {
        /// <summary>
        /// Active server mode used to select simulation defaults.
        /// </summary>
        public ServerMode ServerMode { get; set; } = ServerMode.MMO;

        /// <summary>
        /// Enables interest-based visibility filtering in MMO mode.
        /// </summary>
        public bool EnableInterestGrid { get; init; } = false;

        /// <summary>
        /// Logical size of each interest-grid cell.
        /// </summary>
        public float GridCellSize { get; init; } = 32f;

        /// <summary>
        /// Upper bound for entities included in a single client snapshot.
        /// </summary>
        public int MaxVisibleEntitiesPerClient { get; init; } = 128;
    }

    /// <summary>
    /// Entry point and authoritative simulation loop for the game server.
    /// </summary>
    internal sealed class Program : INetEventListener
    {
        // We use a backing list for iteration to decouple the simulation loop from 
        // network threads modifying the dictionary during connection/disconnection events.
        private static readonly Dictionary<int, ServerPlayer> _playersDict = new();
        private static readonly List<ServerPlayer> _activePlayerIterationList = new();
        
        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly Dictionary<ServerMode, int> _tickRateByMode = new()
        {
            [ServerMode.Arena] = 128,
            [ServerMode.MMO] = 30
        };

        private const float MoveSpeed = 5.0f;
        private static NetManager _netManager = null!;
        private static float _deltaTime;
        private static bool _applyInterestGrid;

        /// <summary>
        /// Boots networking and runs the fixed-step simulation loop.
        /// </summary>
        private static void Main(string[] args)
        {
            ServerRuntimeConfig runtimeConfig = LoadRuntimeConfig(args);
            int tickRate = ResolveTickRate(runtimeConfig.ServerMode);
            _deltaTime = 1f / tickRate;

            _applyInterestGrid = runtimeConfig.ServerMode == ServerMode.MMO && runtimeConfig.EnableInterestGrid;

            Program server = new Program();
            _netManager = new NetManager(server) { AutoRecycle = true };
            _netManager.Start(5001);

            Stopwatch stopwatch = Stopwatch.StartNew();
            long previousTicks = stopwatch.ElapsedTicks;
            long ticksPerFrame = (long)(Stopwatch.Frequency * _deltaTime);
            long accumulator = 0;

            // Expanded scratch buffer capacity to safely accommodate complete combined world snapshots
            byte[] scratchBuffer = new byte[65535];

            Console.WriteLine($"MemoryPack server running in {runtimeConfig.ServerMode} mode at {tickRate}Hz.");
            Console.WriteLine($"Interest grid enabled: {_applyInterestGrid} | CellSize={runtimeConfig.GridCellSize} | MaxVisible={runtimeConfig.MaxVisibleEntitiesPerClient}");

            while (true)
            {
                long currentTicks = stopwatch.ElapsedTicks;
                accumulator += currentTicks - previousTicks;
                previousTicks = currentTicks;

                while (accumulator >= ticksPerFrame)
                {
                    SimulateTick(stopwatch.ElapsedMilliseconds, scratchBuffer);
                    accumulator -= ticksPerFrame;
                }

                Thread.SpinWait(1);
            }
        }

        /// <summary>
        /// Loads runtime configuration from appsettings.json and command-line overrides.
        /// </summary>
        private static ServerRuntimeConfig LoadRuntimeConfig(string[] args)
        {
            ServerRuntimeConfig config = new ServerRuntimeConfig();
            const string appSettingsPath = "appsettings.json";

            if (File.Exists(appSettingsPath))
            {
                string json = File.ReadAllText(appSettingsPath);
                ServerRuntimeConfig? loaded = JsonSerializer.Deserialize<ServerRuntimeConfig>(json, _jsonOptions);
                if (loaded != null) config = loaded;
            }

            foreach (string arg in args)
            {
                if (!arg.StartsWith("--serverMode=", StringComparison.OrdinalIgnoreCase)) continue;
                string modeValue = arg.Substring("--serverMode=".Length);
                if (Enum.TryParse(modeValue, true, out ServerMode parsedMode))
                {
                    config.ServerMode = parsedMode;
                }
            }

            return config;
        }

        /// <summary>
        /// Resolves tick rate for the selected server mode.
        /// </summary>
        private static int ResolveTickRate(ServerMode mode)
        {
            return _tickRateByMode.TryGetValue(mode, out int configuredTickRate) ? configuredTickRate : _tickRateByMode[ServerMode.MMO];
        }

        /// <summary>
        /// Advances one simulation tick and sends a state snapshot to peers.
        /// </summary>
        private static void SimulateTick(long elapsedMilliseconds, byte[] scratchBuffer)
        {
            // Process incoming client packets and update connection maps safely before running frame math
            _netManager.PollEvents();

            int playerCount = _activePlayerIterationList.Count;
            if (playerCount == 0) return;

            // 1. Authoritative Movement Simulation
            for (int i = 0; i < playerCount; i++)
            {
                ServerPlayer player = _activePlayerIterationList[i];
                float moveX = 0f;
                float moveZ = 0f;

                if ((player.LastInput & (byte)InputFlags.MoveUp) != 0) moveZ += 1f;
                if ((player.LastInput & (byte)InputFlags.MoveDown) != 0) moveZ -= 1f;
                if ((player.LastInput & (byte)InputFlags.MoveLeft) != 0) moveX -= 1f;
                if ((player.LastInput & (byte)InputFlags.MoveRight) != 0) moveX += 1f;

                // FIX: Normalize vectors to prevent rapid diagonal movement speeds
                if (moveX != 0f || moveZ != 0f)
                {
                    float length = MathF.Sqrt((moveX * moveX) + (moveZ * moveZ));
                    moveX /= length;
                    moveZ /= length;

                    player.PositionX += moveX * MoveSpeed * _deltaTime;
                    player.PositionZ += moveZ * MoveSpeed * _deltaTime;
                }
            }

            // 2. Optimized Serialization and State Batching
            if (!_applyInterestGrid)
            {
                // ARENA MODE: Bundle ALL players into one single array and broadcast it once
                // This maximizes MTU layout efficiency and avoids generating thousands of loose packets
                ServerStatePacket[] globalSnapshot = new ServerStatePacket[playerCount];
                for (int i = 0; i < playerCount; i++)
                {
                    ServerPlayer p = _activePlayerIterationList[i];
                    globalSnapshot[i] = new ServerStatePacket
                    {
                        NetworkId = p.Id,
                        PositionX = p.PositionX,
                        PositionZ = p.PositionZ,
                        Timestamp = elapsedMilliseconds
                    };
                }

                // Serialize the entire collection array into the scratch memory buffer
                NetworkSerializer.WriteStruct(scratchBuffer, ref globalSnapshot, out int bytesWritten);
                BroadcastToAllPeers(scratchBuffer, bytesWritten);
            }
            else
            {
                // MMO MODE: Temporary fallback until your Grid of Interest systems are integrated.
                // Will evaluate each player's local quadrant boundaries individually.
                ServerStatePacket[] globalSnapshot = new ServerStatePacket[playerCount];
                for (int i = 0; i < playerCount; i++)
                {
                    ServerPlayer p = _activePlayerIterationList[i];
                    globalSnapshot[i] = new ServerStatePacket { NetworkId = p.Id, PositionX = p.PositionX, PositionZ = p.PositionZ, Timestamp = elapsedMilliseconds };
                }
                NetworkSerializer.WriteStruct(scratchBuffer, ref globalSnapshot, out int bytesWritten);
                BroadcastToAllPeers(scratchBuffer, bytesWritten);
            }
        }

        /// <summary>
        /// Sends the prepared payload to every currently connected peer.
        /// </summary>
        private static void BroadcastToAllPeers(byte[] payload, int bytesWritten)
        {
            int count = _activePlayerIterationList.Count;
            for (int i = 0; i < count; i++)
            {
                _activePlayerIterationList[i].Peer.Send(payload, 0, bytesWritten, DeliveryMethod.Unreliable);
            }
        }

        /// <summary>
        /// Accepts incoming connection requests that provide the configured key.
        /// </summary>
        public void OnConnectionRequest(ConnectionRequest request) => request.AcceptIfKey("MMO_Secret");

        /// <summary>
        /// Registers a connected peer into the active player collections.
        /// </summary>
        public void OnPeerConnected(NetPeer peer)
        {
            Console.WriteLine($"Client connected: ID {peer.Id}");
            var newPlayer = new ServerPlayer { Id = (uint)peer.Id, Peer = peer };
            
            _playersDict.Add(peer.Id, newPlayer);
            _activePlayerIterationList.Add(newPlayer);
        }

        /// <summary>
        /// Removes a disconnected peer from all active player collections.
        /// </summary>
        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            Console.WriteLine($"Client disconnected: ID {peer.Id}");
            if (_playersDict.Remove(peer.Id, out ServerPlayer? removedPlayer))
            {
                _activePlayerIterationList.Remove(removedPlayer);
            }
        }

        /// <summary>
        /// Applies the latest input packet to the associated authoritative player state.
        /// </summary>
        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
        {
            ReadOnlySpan<byte> bytes = reader.GetRemainingBytesSpan();
            ClientInputPacket inputPacket = NetworkSerializer.ReadStruct<ClientInputPacket>(bytes);

            if (_playersDict.TryGetValue(peer.Id, out ServerPlayer? player))
            {
                player.LastInput = inputPacket.Inputs;
            }
        }

        public void OnNetworkError(System.Net.IPEndPoint ep, System.Net.Sockets.SocketError error) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint ep, NetPacketReader reader, UnconnectedMessageType type) { }
    }

    /// <summary>
    /// Authoritative per-connection state tracked by the game server.
    /// </summary>
    public sealed class ServerPlayer
    {
        /// <summary>
        /// Stable network identifier assigned from the peer id.
        /// </summary>
        public uint Id;

        /// <summary>
        /// Active LiteNetLib peer representing this player connection.
        /// </summary>
        public required NetPeer Peer;

        /// <summary>
        /// Authoritative world X coordinate.
        /// </summary>
        public float PositionX;

        /// <summary>
        /// Authoritative world Z coordinate.
        /// </summary>
        public float PositionZ;

        /// <summary>
        /// Most recent input bitmask received from the client.
        /// </summary>
        public byte LastInput;
    }
}