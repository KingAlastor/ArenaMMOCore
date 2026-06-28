using System.Collections.Generic;
using LiteNetLib;
using SharedLibrary;

using GameServer.GameEngine;

namespace GameServer.NetworkEngine
{
    /// <summary>
    /// Zero-allocation per-peer snapshot serialization and delivery pipeline.
    /// </summary>
    internal static class SnapshotBroadcaster
    {
        private static ServerRuntimeConfig _config = null!;
        private static bool _applyInterestGrid;

        // Dedicated per-thread packet chunk buffer to avoid allocations inside the serialization loops
        [ThreadStatic]
        private static byte[]? _packetChunkScratchBuffer;

        private static byte[] GetPacketChunkBuffer()
        {
            return _packetChunkScratchBuffer ??= new byte[256];
        }

        /// <summary>
        /// Stores runtime visibility settings used during snapshot assembly.
        /// </summary>
        public static void Initialize(ServerRuntimeConfig config, bool applyInterestGrid)
        {
            _config = config;
            _applyInterestGrid = applyInterestGrid;
        }

        /// <summary>
        /// Builds and sends customized world snapshots to every connected peer.
        /// </summary>
        public static void Broadcast(List<ServerPlayer> players, long elapsedMilliseconds, byte[] scratchBuffer)
        {
            int playerCount = players.Count;
            byte[] chunkBuffer = GetPacketChunkBuffer();

            for (int o = 0; o < playerCount; o++)
            {
                ServerPlayer observer = players[o];
                int bytesWrittenThisPeer = 0;
                int visibleEntitiesCount = 0;

                for (int t = 0; t < playerCount; t++)
                {
                    ServerPlayer target = players[t];

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
    }
}
