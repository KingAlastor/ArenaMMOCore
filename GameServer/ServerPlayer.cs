using LiteNetLib;

namespace GameServer
{
    /// <summary>
    /// Authoritative per-connection state tracked by the game server.
    /// </summary>
    public sealed class ServerPlayer
    {
        /// <summary>
        /// Stable network identifier assigned from the peer id.
        /// </summary>
        public uint Id;

        /// <summary>
        /// Active LiteNetLib peer representing this player connection.
        /// </summary>
        public required NetPeer Peer;

        /// <summary>
        /// Authoritative world X coordinate.
        /// </summary>
        public float PositionX;

        /// <summary>
        /// Authoritative world Z coordinate.
        /// </summary>
        public float PositionZ;

        /// <summary>
        /// Most recent input bitmask received from the client.
        /// </summary>
        public byte LastInput;

        /// <summary>
        /// Rolling count of received input packets; used to throttle server-side receive logs.
        /// </summary>
        public int PacketCount;
    }
}
