// File Path: GameServer/NetworkEngine/NetworkEngine.cs

using System;
using System.Collections.Generic;
using LiteNetLib;
using SharedLibrary;

using GameServer.GameEngine;

namespace GameServer.NetworkEngine
{
  /// <summary>
  /// Transport layer wrapper that ingests raw packets and maintains active player collections.
  /// </summary>
  internal sealed class NetworkEngine : INetEventListener
  {
    // We use a backing list for iteration to decouple the simulation loop from
    // network threads modifying the dictionary during connection/disconnection events.
    private static readonly Dictionary<int, ServerPlayer> _playersDict = new();
    private static readonly List<ServerPlayer> _activePlayerIterationList = new();

    private NetManager _netManager = null!;

    /// <summary>
    /// Fixed visual spacing applied only when a peer joins so multiple freshly connected
    /// clients do not occupy the exact same server coordinate and hide inside one mesh.
    /// </summary>
    private const float InitialSpawnSpacingMeters = 2.0f;

    /// <summary>
    /// Stable iteration list used by simulation and snapshot engines each tick.
    /// </summary>
    public static List<ServerPlayer> ActivePlayers => _activePlayerIterationList;

    /// <summary>
    /// Starts the LiteNetLib listener on the configured port.
    /// </summary>
    public void Start(int port)
    {
      _netManager = new NetManager(this) { AutoRecycle = true };
      _netManager.Start(port);
    }

    /// <summary>
    /// Drains pending network events before the simulation thread reads player state.
    /// </summary>
    public void PollEvents()
    {
      _netManager.PollEvents();
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
      Console.WriteLine($"[Connection] Client connected: ID {peer.Id} from {peer.Port}");

      // Connection handling is outside the 60Hz simulation hot path, but the spawn
      // coordinates remain primitive-only so the first replicated snapshot is stable,
      // deterministic, and immediately visible in low-spec client test scenes.
      int spawnIndex = _activePlayerIterationList.Count;
      ServerPlayer newPlayer = new ServerPlayer
      {
        Id = (uint)peer.Id,
        Peer = peer,
        PositionX = spawnIndex * InitialSpawnSpacingMeters,
        PositionZ = 0f
      };

      _playersDict.Add(peer.Id, newPlayer);
      _activePlayerIterationList.Add(newPlayer);
    }

    /// <summary>
    /// Removes a disconnected peer from all active player collections.
    /// </summary>
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
      Console.WriteLine($"[Connection] Client disconnected: ID {peer.Id}, Reason: {info.Reason}");
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
        player.PacketCount++;

        // Log every 128 packets (~1/sec at 128Hz) to confirm inputs are reaching the server.
        if (player.PacketCount % 128 == 0)
        {
          string maskBinary = Convert.ToString(player.LastInput, 2).PadLeft(8, '0');
          Console.WriteLine($"[NetRecv] Player {player.Id} pkt#{player.PacketCount}: input=0b{maskBinary}");
        }
      }
    }

    public void OnNetworkError(System.Net.IPEndPoint ep, System.Net.Sockets.SocketError error) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint ep, NetPacketReader reader, UnconnectedMessageType type) { }
  }
}
