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
        private static ServerRuntimeConfig _config = null!;

        // Dedicated per-thread packet chunk buffer to avoid allocations inside the serialization loops
        [ThreadStatic]
        private static byte[]? _packetChunkScratchBuffer;

        private static byte[] GetPacketChunkBuffer()
        {
            return _packetChunkScratchBuffer ??= new byte[256];
        }

        /// <summary>
        /// Boots networking and runs the fixed-step simulation loop.
        /// </summary>
        private static void Main(string[] args)
        {
            _config = LoadRuntimeConfig(args);
            int tickRate = ResolveTickRate(_config.ServerMode);
            _deltaTime = 1f / tickRate;

            _applyInterestGrid = _config.ServerMode == ServerMode.MMO && _config.EnableInterestGrid;

            Program server = new Program();
            _netManager = new NetManager(server) { AutoRecycle = true };
            _netManager.Start(5001);

            Stopwatch stopwatch = Stopwatch.StartNew();
            long previousTicks = stopwatch.ElapsedTicks;
            long ticksPerFrame = (long)(Stopwatch.Frequency * _deltaTime);
            long accumulator = 0;

            // Expanded scratch buffer capacity to safely accommodate complete combined world snapshots
            byte[] scratchBuffer = new byte[65535];

            Console.WriteLine($"MemoryPack server running in {_config.ServerMode} mode at {tickRate}Hz.");
            Console.WriteLine($"Interest grid enabled: {_applyInterestGrid} | CellSize={_config.GridCellSize} | MaxVisible={_config.MaxVisibleEntitiesPerClient}");

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

                // Normalize vectors to prevent rapid diagonal movement speeds
                if (moveX != 0f || moveZ != 0f)
                {
                    float length = MathF.Sqrt((moveX * moveX) + (moveZ * moveZ));
                    moveX /= length;
                    moveZ /= length;

                    player.PositionX += moveX * MoveSpeed * _deltaTime;
                    player.PositionZ += moveZ * MoveSpeed * _deltaTime;
                }
            }

            // Grab our pooled, non-allocating single packet serialization workspace
            byte[] chunkBuffer = GetPacketChunkBuffer();

            // 2. Zero-Allocation Per-Peer Loop (Fills and fires sequential customized states)
            for (int o = 0; o < playerCount; o++)
            {
                ServerPlayer observer = _activePlayerIterationList[o];
                int bytesWrittenThisPeer = 0;
                int visibleEntitiesCount = 0;

                for (int t = 0; t < playerCount; t++)
                {
                    ServerPlayer target = _activePlayerIterationList[t];

                    // Check upper limit bounds set by config file to safeguard low-spec clients
                    if (visibleEntitiesCount >= _config.MaxVisibleEntitiesPerClient) break;

                    // MMO MODE Grid distance checking filtering rule
                    if (_applyInterestGrid)
                    {
                        float dx = target.PositionX - observer.PositionX;
                        float dz = target.PositionZ - observer.PositionZ;
                        float distanceSq = (dx * dx) + (dz * dz);
                        float maxRange = _config.GridCellSize;

                        // Skip entities outside visibility boundaries
                        if (distanceSq > (maxRange * maxRange)) continue;
                    }

                    // Map state data directly to temporary stack frame memory (0 allocations)
                    ServerStatePacket statePacket = new ServerStatePacket
                    {
                        NetworkId = target.Id,
                        PositionX = target.PositionX,
                        PositionZ = target.PositionZ,
                        Timestamp = elapsedMilliseconds
                    };

                    // FACTION/TEAM SECURITY FILTER EXTENSION HOOK:
                    // if (target.Faction != observer.Faction) { statePacket.ActiveBuffs = 0; }

                    // Safety bounds check to ensure payload fits inside remaining scratchBuffer space
                    if (bytesWrittenThisPeer + 64 > scratchBuffer.Length) break;

                    // Serialize isolated packet reference safely meeting the generic 'where T : struct' rules
                    NetworkSerializer.WriteStruct(chunkBuffer, ref statePacket, out int bytesWrittenThisChunk);

                    // Blast memory block directly down onto our output stream window
                    Array.Copy(chunkBuffer, 0, scratchBuffer, bytesWrittenThisPeer, bytesWrittenThisChunk);
                    
                    bytesWrittenThisPeer += bytesWrittenThisChunk;
                    visibleEntitiesCount++;
                }

                // Send the custom packed payload directly out to this individual connection channel
                if (bytesWrittenThisPeer > 0)
                {
                    observer.Peer.Send(scratchBuffer, 0, bytesWrittenThisPeer, DeliveryMethod.Unreliable);
                }
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