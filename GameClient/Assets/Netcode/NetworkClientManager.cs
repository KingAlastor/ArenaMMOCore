using UnityEngine;
using LiteNetLib;
using SharedLibrary;
using System;

public class NetworkClientManager : MonoBehaviour, INetEventListener
{
    private NetManager _netManager;
    private NetPeer _serverPeer;

    // We store the latest server state in public fields so our DOTS System can query them
    [NonSerialized] public float ServerPositionX;
    [NonSerialized] public float ServerPositionZ;
    [NonSerialized] public uint TargetNetworkId;
    [NonSerialized] public bool HasNewUpdate;

    void Start()
    {
        _netManager = new NetManager(this) { AutoRecycle = true };
        _netManager.Start();
        
        // Connect to your headless .NET Core server (assuming it's running locally)
        _serverPeer = _netManager.Connect("localhost", 5001, "MMO_Secret");
        Debug.Log("Connecting to server...");
    }

    void Update()
    {
        // Must be called every frame to pump network events
        _netManager.PollEvents();
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod delivery)
    {
        ReadOnlySpan<byte> bytes = reader.GetRemainingBytesSpan();
        
        // Read the single packed state using your SharedLibrary MemoryPack code
        ServerStatePacket state = NetworkSerializer.ReadStruct<ServerStatePacket>(bytes);

        // Store the information for the DOTS world to consume
        TargetNetworkId = state.NetworkId;
        ServerPositionX = state.PositionX;
        ServerPositionZ = state.PositionZ;
        HasNewUpdate = true;
    }

    void OnDestroy()
    {
        if (_netManager != null) _netManager.Stop();
    }

    // Required boilerplate interfaces for LiteNetLib
    public void OnPeerConnected(NetPeer peer) => Debug.Log($"Connected to Server: {peer.EndPoint}");
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) => Debug.Log($"Disconnected: {info.Reason}");
    public void OnNetworkError(System.Net.IPEndPoint ep, System.Net.Sockets.SocketError error) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnConnectionRequest(ConnectionRequest request) => request.Reject();
    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint ep, NetPacketReader reader, UnconnectedMessageType type) { }
}