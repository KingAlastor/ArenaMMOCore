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
    /// High-level runtime profile used to tune server simulation cadence.
    /// </summary>
    public enum ServerMode
    {
        Arena,
        MMO
    }

    /// <summary>
    /// Configuration values loaded from appsettings and optional CLI overrides.
    /// </summary>
    public sealed class ServerRuntimeConfig
    {
        public ServerMode ServerMode { get; set; } = ServerMode.MMO;

        /// <summary>
        /// Enables area-of-interest culling for MMO mode when implemented.
        /// </summary>
        public bool EnableInterestGrid { get; init; } = false;

        /// <summary>
        /// World-space side length for each interest-grid cell.
        /// </summary>
        public float GridCellSize { get; init; } = 32f;

        /// <summary>
        /// Per-client safety cap used by future interest-grid visibility queries.
        /// </summary>
        public int MaxVisibleEntitiesPerClient { get; init; } = 128;
    }

    /// <summary>
    /// Dedicated headless authoritative simulation server.
    /// </summary>
    internal sealed class Program : INetEventListener
    {
        private static readonly Dictionary<int, ServerPlayer> _players = new();
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

        private static void Main(string[] args)
        {
            ServerRuntimeConfig runtimeConfig = LoadRuntimeConfig(args);
            int tickRate = ResolveTickRate(runtimeConfig.ServerMode);
            _deltaTime = 1f / tickRate;

            // Arena mode always broadcasts globally; MMO can opt into interest grid once integrated.
            _applyInterestGrid = runtimeConfig.ServerMode == ServerMode.MMO && runtimeConfig.EnableInterestGrid;

            Program server = new Program();
            _netManager = new NetManager(server) { AutoRecycle = true };
            _netManager.Start(5001);

            Stopwatch stopwatch = Stopwatch.StartNew();
            long previousTicks = stopwatch.ElapsedTicks;
            long ticksPerFrame = (long)(Stopwatch.Frequency * _deltaTime);
            long accumulator = 0;

            // A single reusable scratch buffer is used for all state snapshots to avoid per-tick allocations.
            byte[] scratchBuffer = new byte[1024];

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

        private static ServerRuntimeConfig LoadRuntimeConfig(string[] args)
        {
            ServerRuntimeConfig config = new ServerRuntimeConfig();
            const string appSettingsPath = "appsettings.json";

            if (File.Exists(appSettingsPath))
            {
                string json = File.ReadAllText(appSettingsPath);
                ServerRuntimeConfig? loaded = JsonSerializer.Deserialize<ServerRuntimeConfig>(json, _jsonOptions);
                if (loaded != null)
                {
                    config = loaded;
                }
            }

            // CLI override: --serverMode=Arena|MMO
            foreach (string arg in args)
            {
                if (!arg.StartsWith("--serverMode=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string modeValue = arg.Substring("--serverMode=".Length);
                if (Enum.TryParse(modeValue, true, out ServerMode parsedMode))
                {
                    config.ServerMode = parsedMode;
                }
            }

            return config;
        }

        private static int ResolveTickRate(ServerMode mode)
        {
            if (_tickRateByMode.TryGetValue(mode, out int configuredTickRate))
            {
                return configuredTickRate;
            }

            return _tickRateByMode[ServerMode.MMO];
        }

        private static void SimulateTick(long elapsedMilliseconds, byte[] scratchBuffer)
        {
            _netManager.PollEvents();

            foreach (ServerPlayer player in _players.Values)
            {
                float moveX = 0f;
                float moveZ = 0f;

                if ((player.LastInput & (byte)InputFlags.MoveUp) != 0) moveZ += 1f;
                if ((player.LastInput & (byte)InputFlags.MoveDown) != 0) moveZ -= 1f;
                if ((player.LastInput & (byte)InputFlags.MoveLeft) != 0) moveX -= 1f;
                if ((player.LastInput & (byte)InputFlags.MoveRight) != 0) moveX += 1f;

                player.PositionX += moveX * MoveSpeed * _deltaTime;
                player.PositionZ += moveZ * MoveSpeed * _deltaTime;

                ServerStatePacket state = new ServerStatePacket
                {
                    NetworkId = player.Id,
                    PositionX = player.PositionX,
                    PositionZ = player.PositionZ,
                    Timestamp = elapsedMilliseconds
                };

                NetworkSerializer.WriteStruct(scratchBuffer, ref state, out int bytesWritten);

                if (_applyInterestGrid)
                {
                    // Interest-grid selection is planned for MMO mode; current fallback broadcasts globally.
                    BroadcastToAllPeers(scratchBuffer, bytesWritten);
                }
                else
                {
                    // Arena mode always bypasses interest culling and broadcasts to everyone.
                    BroadcastToAllPeers(scratchBuffer, bytesWritten);
                }
            }
        }

        private static void BroadcastToAllPeers(byte[] payload, int bytesWritten)
        {
            foreach (ServerPlayer recipient in _players.Values)
            {
                recipient.Peer.Send(payload, 0, bytesWritten, DeliveryMethod.Unreliable);
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
            ClientInputPacket inputPacket = NetworkSerializer.ReadStruct<ClientInputPacket>(bytes);

            if (_players.TryGetValue(peer.Id, out ServerPlayer? player))
            {
                player.LastInput = inputPacket.Inputs;
            }
        }

        public void OnNetworkError(System.Net.IPEndPoint ep, System.Net.Sockets.SocketError error) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint ep, NetPacketReader reader, UnconnectedMessageType type) { }
    }

    /// <summary>
    /// Per-client server-side state used by the authoritative simulation.
    /// </summary>
    public sealed class ServerPlayer
    {
        public uint Id;
        public required NetPeer Peer;
        public float PositionX;
        public float PositionZ;
        public byte LastInput;
    }
}
