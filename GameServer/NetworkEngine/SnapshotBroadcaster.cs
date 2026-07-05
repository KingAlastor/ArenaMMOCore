// File Path: GameServer/NetworkEngine/SnapshotBroadcaster.cs

using System;
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

    // Dedicated per-thread packet row buffer to avoid allocations inside serialization loops.
    [ThreadStatic]
    private static byte[]? _packetChunkScratchBuffer;

    // Dedicated per-thread header buffer so the row buffer can be reused independently.
    [ThreadStatic]
    private static byte[]? _packetHeaderScratchBuffer;

    private static byte[] GetPacketChunkBuffer()
    {
      return _packetChunkScratchBuffer ??= new byte[256];
    }

    private static byte[] GetPacketHeaderBuffer()
    {
      return _packetHeaderScratchBuffer ??= new byte[256];
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
      byte[] headerBuffer = GetPacketHeaderBuffer();

      for (int o = 0; o < playerCount; o++)
      {
        ServerPlayer observer = players[o];

        // Write a temporary header first so all rows can be appended after its exact MemoryPack length.
        ServerSnapshotHeader snapshotHeader = new ServerSnapshotHeader
        {
          EntityCount = 0,
          ObserverNetworkId = observer.Id,
          Timestamp = elapsedMilliseconds
        };
        NetworkSerializer.WriteStruct(headerBuffer, ref snapshotHeader, out int headerBytesWritten);

        // If the caller ever supplies a smaller scratch buffer, fail safely before touching the row loop.
        if (headerBytesWritten > scratchBuffer.Length)
        {
          continue;
        }

        Array.Copy(headerBuffer, 0, scratchBuffer, 0, headerBytesWritten);

        int bytesWrittenThisPeer = headerBytesWritten;
        int visibleEntitiesCount = 0;

        for (int t = 0; t < playerCount; t++)
        {
          ServerPlayer target = players[t];

          // Check upper limit bounds set by config file to safeguard low-spec clients.
          if (visibleEntitiesCount >= _config.MaxVisibleEntitiesPerClient) break;

          // MMO MODE Grid distance checking filtering rule.
          if (_applyInterestGrid)
          {
            float dx = target.PositionX - observer.PositionX;
            float dz = target.PositionZ - observer.PositionZ;
            float distanceSq = (dx * dx) + (dz * dz);
            float maxRange = _config.GridCellSize;

            // Skip entities outside visibility boundaries.
            if (distanceSq > (maxRange * maxRange)) continue;
          }

          // Map authoritative actor state directly to temporary stack frame memory.
          ServerStatePacket statePacket = new ServerStatePacket
          {
            NetworkId = target.Id,
            PositionX = target.PositionX,
            PositionZ = target.PositionZ,
            Timestamp = elapsedMilliseconds
          };

          // FACTION/TEAM SECURITY FILTER EXTENSION HOOK:
          // if (target.Faction != observer.Faction) { statePacket.ActiveBuffs = 0; }

          // Serialize the fixed row before copying so the exact MemoryPack row size is known.
          NetworkSerializer.WriteStruct(chunkBuffer, ref statePacket, out int bytesWrittenThisChunk);

          // Safety bounds check to ensure the full row fits inside remaining scratch buffer space.
          if (bytesWrittenThisPeer + bytesWrittenThisChunk > scratchBuffer.Length) break;

          // Blast row bytes directly onto the peer-specific output payload window.
          Array.Copy(chunkBuffer, 0, scratchBuffer, bytesWrittenThisPeer, bytesWrittenThisChunk);

          bytesWrittenThisPeer += bytesWrittenThisChunk;
          visibleEntitiesCount++;
        }

        // Send nothing if the observer has no visible rows after filtering or capacity limits.
        if (visibleEntitiesCount <= 0)
        {
          continue;
        }

        // Rewrite the header at the front with the final entity count after the row loop completes.
        snapshotHeader.EntityCount = visibleEntitiesCount;
        NetworkSerializer.WriteStruct(headerBuffer, ref snapshotHeader, out int finalHeaderBytesWritten);

        // Fixed primitive MemoryPack structs should keep a stable byte count; skip instead of corrupting framing.
        if (finalHeaderBytesWritten != headerBytesWritten)
        {
          continue;
        }

        Array.Copy(headerBuffer, 0, scratchBuffer, 0, finalHeaderBytesWritten);

        // Send one packed payload containing positions for all visible players for this peer.
        observer.Peer.Send(scratchBuffer, 0, bytesWrittenThisPeer, DeliveryMethod.Unreliable);
      }
    }
  }
}
