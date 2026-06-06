using System;
using MemoryPack;

namespace SharedLibrary
{
    /// <summary>
    /// Bitmask for compact input replication where each bit toggles one movement intent.
    /// </summary>
    [Flags]
    public enum InputFlags : byte
    {
        /// <summary>No movement intent for this input frame.</summary>
        None = 0,
        /// <summary>Moves forward on the server's Z axis.</summary>
        MoveUp = 1 << 0,
        /// <summary>Moves backward on the server's Z axis.</summary>
        MoveDown = 1 << 1,
        /// <summary>Moves left on the server's X axis.</summary>
        MoveLeft = 1 << 2,
        /// <summary>Moves right on the server's X axis.</summary>
        MoveRight = 1 << 3
    }

    /// <summary>
    /// Client-to-server input frame serialized through MemoryPack without heap allocations.
    /// First entry step from Unity
    /// </summary>
    [MemoryPackable]
    public partial struct ClientInputPacket
    {
        /// <summary>
        /// Client-owned sequencing value used to correlate packet ordering.
        /// </summary>
        public uint SequenceNumber;

        /// <summary>
        /// Packed <see cref="InputFlags"/> values for this simulation frame.
        /// </summary>
        public byte Inputs;
    }

    /// <summary>
    /// Server-authoritative world state for a single networked actor.
    /// </summary>
    [MemoryPackable]
    public partial struct ServerStatePacket
    {
        /// <summary>
        /// Unique actor identifier assigned by the server transport layer.
        /// </summary>
        public uint NetworkId;

        /// <summary>
        /// Authoritative world-space X coordinate.
        /// </summary>
        public float PositionX;

        /// <summary>
        /// Authoritative world-space Z coordinate.
        /// </summary>
        public float PositionZ;

        /// <summary>
        /// Server monotonic timestamp in milliseconds when this snapshot was produced.
        /// </summary>
        public long Timestamp;
    }
}
