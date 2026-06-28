using SharedLibrary;

namespace GameServer.GameEngine
{
    /// <summary>
    /// Authoritative movement math executed against in-memory player state each tick.
    /// </summary>
    internal static class SimulationEngine
    {
        private const float MoveSpeed = 5.0f;
        private const float PlayerRadius = 0.5f;
        private const float PlayerDiameter = PlayerRadius * 2f;
        private const float PlayerDiameterSq = PlayerDiameter * PlayerDiameter;
        private static float _deltaTime;
        private static float _collisionCellSize = PlayerDiameter;

        // TODO: Add pruning/pooling for stale collision grid cells if long-running MMO sessions cause
        // this dictionary to retain too many previously visited empty cells.
        private static readonly Dictionary<long, List<int>> CollisionGrid = new();

        // Tick counter used to throttle position logs to roughly once per second.
        private static int _tickCounter;

        /// <summary>
        /// Configures the fixed simulation timestep derived from the server tick rate.
        /// </summary>
        public static void Initialize(float deltaTime, float collisionCellSize)
        {
            _deltaTime = deltaTime;
            _collisionCellSize = MathF.Max(collisionCellSize, PlayerDiameter);
        }

        /// <summary>
        /// Advances authoritative positions for every active player without heap allocations.
        /// </summary>
        public static void SimulateTick(List<ServerPlayer> players)
        {
            int playerCount = players.Count;

            for (int i = 0; i < playerCount; i++)
            {
                ServerPlayer player = players[i];
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

            ResolveCollisionsWithGrid(players, playerCount);

            // Log all player positions once per second (every tickRate ticks) to confirm movement.
            _tickCounter++;
            if (_tickCounter % 128 == 0)
            {
                for (int i = 0; i < playerCount; i++)
                {
                    ServerPlayer p = players[i];
                    string maskBinary = Convert.ToString(p.LastInput, 2).PadLeft(8, '0');
                    Console.WriteLine($"[Sim tick={_tickCounter}] Player {p.Id}: pos=({p.PositionX:F2}, {p.PositionZ:F2}) input=0b{maskBinary}");
                }
            }
        }

        /// <summary>
        /// Uses a spatial grid broad-phase so each player only checks collision candidates in nearby cells.
        /// </summary>
        private static void ResolveCollisionsWithGrid(List<ServerPlayer> players, int playerCount)
        {
            ClearCollisionGrid();

            for (int i = 0; i < playerCount; i++)
            {
                ServerPlayer player = players[i];
                int cellX = WorldToCell(player.PositionX);
                int cellZ = WorldToCell(player.PositionZ);
                long cellKey = PackCellKey(cellX, cellZ);

                if (!CollisionGrid.TryGetValue(cellKey, out List<int>? cellPlayers))
                {
                    cellPlayers = new List<int>();
                    CollisionGrid[cellKey] = cellPlayers;
                }

                cellPlayers.Add(i);
            }

            for (int i = 0; i < playerCount; i++)
            {
                ServerPlayer p1 = players[i];
                int originCellX = WorldToCell(p1.PositionX);
                int originCellZ = WorldToCell(p1.PositionZ);

                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                    {
                        long neighborCellKey = PackCellKey(originCellX + offsetX, originCellZ + offsetZ);

                        if (!CollisionGrid.TryGetValue(neighborCellKey, out List<int>? candidateIndices))
                        {
                            continue;
                        }

                        for (int c = 0; c < candidateIndices.Count; c++)
                        {
                            int j = candidateIndices[c];

                            // Prevent duplicate pair resolution and self-collision.
                            if (j <= i) continue;

                            ResolvePlayerPairCollision(p1, players[j]);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Clears existing cell contents while retaining allocated lists for reuse across ticks.
        /// </summary>
        private static void ClearCollisionGrid()
        {
            foreach (List<int> cellPlayers in CollisionGrid.Values)
            {
                cellPlayers.Clear();
            }
        }

        /// <summary>
        /// Resolves exact circle-vs-circle collision between two players.
        /// </summary>
        private static void ResolvePlayerPairCollision(ServerPlayer p1, ServerPlayer p2)
        {
            float dx = p2.PositionX - p1.PositionX;
            float dz = p2.PositionZ - p1.PositionZ;
            float distSq = dx * dx + dz * dz;

            if (distSq >= PlayerDiameterSq || distSq <= 0.0001f)
            {
                return;
            }

            float dist = MathF.Sqrt(distSq);
            float overlap = (PlayerDiameter - dist) * 0.5f;
            float nx = dx / dist;
            float nz = dz / dist;

            p1.PositionX -= nx * overlap;
            p1.PositionZ -= nz * overlap;
            p2.PositionX += nx * overlap;
            p2.PositionZ += nz * overlap;
        }

        private static int WorldToCell(float value)
        {
            return (int)MathF.Floor(value / _collisionCellSize);
        }

        private static long PackCellKey(int cellX, int cellZ)
        {
            return ((long)cellX << 32) ^ (uint)cellZ;
        }
    }
}