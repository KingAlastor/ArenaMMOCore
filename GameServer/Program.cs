using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace GameServer
{
    /// <summary>
    /// Bootstrapper and high-precision heartbeat engine for the authoritative game server.
    /// </summary>
    internal sealed class Program
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        private static readonly Dictionary<ServerMode, int> _tickRateByMode = new()
        {
            [ServerMode.Arena] = 128,
            [ServerMode.MMO] = 30
        };

        /// <summary>
        /// Boots sub-engines and runs the fixed-step simulation loop.
        /// </summary>
        private static void Main(string[] args)
        {
            ServerRuntimeConfig config = LoadRuntimeConfig(args);
            int tickRate = ResolveTickRate(config.ServerMode);
            float deltaTime = 1f / tickRate;
            bool applyInterestGrid = config.ServerMode == ServerMode.MMO && config.EnableInterestGrid;

            NetworkEngine networkEngine = new NetworkEngine();
            networkEngine.Start(5001);

            SimulationEngine.Initialize(deltaTime);
            SnapshotBroadcaster.Initialize(config, applyInterestGrid);

            Stopwatch stopwatch = Stopwatch.StartNew();
            long previousTicks = stopwatch.ElapsedTicks;
            long ticksPerFrame = (long)(Stopwatch.Frequency * deltaTime);
            long accumulator = 0;

            // Expanded scratch buffer capacity to safely accommodate complete combined world snapshots
            byte[] scratchBuffer = new byte[65535];

            Console.WriteLine($"MemoryPack server running in {config.ServerMode} mode at {tickRate}Hz.");
            Console.WriteLine($"Interest grid enabled: {applyInterestGrid} | CellSize={config.GridCellSize} | MaxVisible={config.MaxVisibleEntitiesPerClient}");

            while (true)
            {
                long currentTicks = stopwatch.ElapsedTicks;
                accumulator += currentTicks - previousTicks;
                previousTicks = currentTicks;

                while (accumulator >= ticksPerFrame)
                {
                    RunSimulationTick(networkEngine, stopwatch.ElapsedMilliseconds, scratchBuffer);
                    accumulator -= ticksPerFrame;
                }

                Thread.SpinWait(1);
            }
        }

        /// <summary>
        /// Orchestrates one authoritative tick across network, simulation, and broadcast engines.
        /// </summary>
        private static void RunSimulationTick(NetworkEngine networkEngine, long elapsedMilliseconds, byte[] scratchBuffer)
        {
            networkEngine.PollEvents();

            List<ServerPlayer> players = NetworkEngine.ActivePlayers;
            int playerCount = players.Count;
            if (playerCount == 0) return;

            SimulationEngine.SimulateTick(players);
            SnapshotBroadcaster.Broadcast(players, elapsedMilliseconds, scratchBuffer);
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
    }
}
