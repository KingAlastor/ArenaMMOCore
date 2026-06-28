using System.Diagnostics;
namespace GameServer.GameEngine
{
    using NetworkEngine = GameServer.NetworkEngine.NetworkEngine;
    using SnapshotBroadcaster = NetworkEngine.SnapshotBroadcaster;

    internal static partial class MainEngine
    {
        
        /// <summary>
        /// Boots sub-engines and runs the fixed-step simulation loop.
        /// </summary>
        private static void Main(string[] args)
        {
            ServerRuntimeConfig config = ServerRuntimeConfig.Load(args);
            int tickRate = config.TickRate;
            float deltaTime = 1f / tickRate;
            bool applyInterestGrid = config.EnableInterestGrid;

            NetworkEngine networkEngine = new NetworkEngine();
            networkEngine.Start(5001);

            SimulationEngine.Initialize(deltaTime, config.GridCellSize);
            SnapshotBroadcaster.Initialize(config, applyInterestGrid);

            Stopwatch stopwatch = Stopwatch.StartNew();
            long previousTicks = stopwatch.ElapsedTicks;
            long ticksPerFrame = (long)(Stopwatch.Frequency * deltaTime);
            long accumulator = 0;

            // Expanded scratch buffer capacity to safely accommodate complete combined world snapshots
            byte[] scratchBuffer = new byte[65535];

            Console.WriteLine($"MemoryPack server running at {tickRate}Hz.");
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

    }
}
