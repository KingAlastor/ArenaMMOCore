using System;
using System.Collections.Generic;
using SharedLibrary;

namespace GameServer
{
    /// <summary>
    /// Authoritative movement math executed against in-memory player state each tick.
    /// </summary>
    internal static class SimulationEngine
    {
        private const float MoveSpeed = 5.0f;
        private static float _deltaTime;

        // Tick counter used to throttle position logs to roughly once per second.
        private static int _tickCounter;

        /// <summary>
        /// Configures the fixed simulation timestep derived from the server tick rate.
        /// </summary>
        public static void Initialize(float deltaTime)
        {
            _deltaTime = deltaTime;
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
    }
}
