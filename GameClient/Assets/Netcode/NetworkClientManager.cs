using UnityEngine;
using UnityEngine.InputSystem;
using System;
using LiteNetLib;
using SharedLibrary;

namespace GameClient
{
  /// <summary>
  /// Unity-side transport bridge that polls LiteNetLib and exposes latest server snapshots to ECS systems.
  /// </summary>
  public class NetworkClientManager : MonoBehaviour, INetEventListener
  {
    /// <summary>
    /// Hard client-side cap for one decoded server snapshot so packet parsing never resizes arrays in play.
    /// </summary>
    public const int MaxSnapshotEntities = 256;

    // LiteNetLib manager used by the main thread Unity loop.
    private NetManager _client;

    // Cached server peer once the connection handshake completes.
    private NetPeer _serverPeer;

    // Global access point for ECS systems that cannot hold managed references.
    public static NetworkClientManager Instance;

    // Last input bitmask sent to the server for local debugging and diagnostics.
    public byte CurrentInputMask;

    // Fixed decoded snapshot row storage reused by ECS; only entries below SnapshotEntityCount are valid.
    public readonly ServerStatePacket[] SnapshotEntities = new ServerStatePacket[MaxSnapshotEntities];

    // Number of valid entries currently stored in SnapshotEntities.
    public int SnapshotEntityCount;

    // Network id assigned by the server to this client connection.
    public uint LocalNetworkId;

    // Snapshot timestamp in server milliseconds for interpolation timing.
    public long ServerTimestampUpdate;

    // Monotonic receive sequence used by ECS systems to mark which entities were present in the newest payload.
    public int SnapshotSequence;

    // Handshake flag consumed by ECS systems to know when a fresh snapshot arrived.
    public bool HasUpdate;

    // Reusable outbound packet buffer to avoid per-frame allocations.
    private readonly byte[] _sendScratchBuffer = new byte[256];

    // Reusable packet-size probe buffer used once during Awake to validate MemoryPack framing sizes.
    private readonly byte[] _packetSizeScratchBuffer = new byte[256];

    // Cached byte count for one serialized ServerSnapshotHeader.
    private int _snapshotHeaderPacketSize;

    // Cached byte count for one serialized ServerStatePacket row.
    private int _serverStatePacketSize;

    // Tracks last sent input mask so we only log on actual input changes.
    private byte _lastSentInputMask;

    // Rolling counter of received server snapshots used to throttle console spam.
    private int _snapshotCount;

    private void Awake()
    {
      Instance = this;

      // Precompute fixed MemoryPack frame sizes once so hot receive parsing can advance by raw byte offsets.
      ServerSnapshotHeader emptyHeader = default;
      NetworkSerializer.WriteStruct(_packetSizeScratchBuffer, ref emptyHeader, out _snapshotHeaderPacketSize);

      ServerStatePacket emptyState = default;
      NetworkSerializer.WriteStruct(_packetSizeScratchBuffer, ref emptyState, out _serverStatePacketSize);

      // Configure client transport and open local socket before connecting.
      _client = new NetManager(this) { AutoRecycle = true };
      _client.Start();
      _client.Connect("localhost", 5001, "MMO_Secret");
    }

    private void Update()
    {
      // Poll inbound packets and dispatch callbacks on the Unity main thread.
      _client.PollEvents();

      // Package and replicate local input every frame.
      SendInputsToServer();
    }

    private void SendInputsToServer()
    {
      if (_serverPeer == null) return;

      // Build compact input bitmask from keyboard state via the new Input System.
      byte mask = 0;
      var kb = Keyboard.current;
      if (kb != null)
      {
        if (kb.wKey.isPressed) mask |= (byte)InputFlags.MoveUp;
        if (kb.sKey.isPressed) mask |= (byte)InputFlags.MoveDown;
        if (kb.aKey.isPressed) mask |= (byte)InputFlags.MoveLeft;
        if (kb.dKey.isPressed) mask |= (byte)InputFlags.MoveRight;
      }

      CurrentInputMask = mask;

      // Log only when the input state actually changes to avoid flooding the console.
      if (mask != _lastSentInputMask)
      {
        string maskBinary = Convert.ToString(mask, 2).PadLeft(8, '0');
        Debug.Log($"[Input] Mask changed -> 0b{maskBinary} (W={kb != null && kb.wKey.isPressed} S={kb != null && kb.sKey.isPressed} A={kb != null && kb.aKey.isPressed} D={kb != null && kb.dKey.isPressed})");
        _lastSentInputMask = mask;
      }

      ClientInputPacket inputPacket = new ClientInputPacket { SequenceNumber = 1, Inputs = mask };

      // Serialize directly into the shared outbound scratch array.
      NetworkSerializer.WriteStruct(_sendScratchBuffer, ref inputPacket, out int bytesWritten);

      _serverPeer.Send(_sendScratchBuffer, 0, bytesWritten, DeliveryMethod.Unreliable);
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
    {
      ReadOnlySpan<byte> bytes = reader.GetRemainingBytesSpan();

      // Reject malformed payloads before attempting MemoryPack deserialization.
      if (bytes.Length < _snapshotHeaderPacketSize)
      {
        return;
      }

      // Decode the fixed header from the beginning of the single payload.
      ServerSnapshotHeader header = NetworkSerializer.ReadStruct<ServerSnapshotHeader>(bytes.Slice(0, _snapshotHeaderPacketSize));

      // Reject negative or structurally impossible row counts before offset math.
      if (header.EntityCount < 0)
      {
        return;
      }

      long requiredBytes = _snapshotHeaderPacketSize + ((long)header.EntityCount * _serverStatePacketSize);
      if (bytes.Length < requiredBytes)
      {
        return;
      }

      // Decode up to the fixed client capacity; excess rows stay ignored rather than resizing memory.
      int decodedEntityCount = header.EntityCount;
      if (decodedEntityCount > SnapshotEntities.Length)
      {
        decodedEntityCount = SnapshotEntities.Length;
      }

      int readOffset = _snapshotHeaderPacketSize;
      for (int i = 0; i < decodedEntityCount; i++)
      {
        SnapshotEntities[i] = NetworkSerializer.ReadStruct<ServerStatePacket>(bytes.Slice(readOffset, _serverStatePacketSize));
        readOffset += _serverStatePacketSize;
      }

      SnapshotEntityCount = decodedEntityCount;
      LocalNetworkId = header.ObserverNetworkId;
      ServerTimestampUpdate = header.Timestamp;
      SnapshotSequence++;
      HasUpdate = true;

      // Log one in every 60 snapshots (~1/sec at 60fps) to confirm aggregate data is flowing.
      _snapshotCount++;
      if (_snapshotCount % 60 == 0)
      {
        Debug.Log($"[Snapshot #{_snapshotCount}] entities={decodedEntityCount}/{header.EntityCount} self={LocalNetworkId} t={header.Timestamp}ms");
      }
    }

    // Store the transport peer after successful handshake.
    public void OnPeerConnected(NetPeer peer)
    {
      _serverPeer = peer;
      Debug.Log($"[Network] Connected to server: {peer.Address}");
    }

    // Clear peer reference on disconnect to stop outbound sends.
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
      _serverPeer = null;
      HasUpdate = false;
      SnapshotEntityCount = 0;
      Debug.LogWarning($"[Network] Disconnected from server. Reason: {info.Reason}");
    }

    // Connection requests are not used on the client side because this process initiates outbound only.
    public void OnConnectionRequest(ConnectionRequest request) { }

    // Optional transport diagnostics hooks are intentionally no-op for now.
    public void OnNetworkError(System.Net.IPEndPoint ep, System.Net.Sockets.SocketError err) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int lat) { }
    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint ep, NetPacketReader r, UnconnectedMessageType t) { }

    // Always stop sockets when Unity tears down this behaviour.
    private void OnDestroy()
    {
      if (Instance == this)
      {
        Instance = null;
      }

      _client?.Stop();
    }
  }
}
