using System;
using MemoryPack;

namespace SharedLibrary
{
    [Flags]
    public enum InputFlags : byte
    {
        None = 0,
        MoveUp = 1 << 0,
        MoveDown = 1 << 1,
        MoveLeft = 1 << 2,
        MoveRight = 1 << 3
    }

    [MemoryPackable]
    public partial struct ClientInputPacket
    {
        public uint SequenceNumber;
        public byte Inputs; 
    }

    [MemoryPackable]
    public partial struct ServerStatePacket
    {
        public uint NetworkId;
        public float PositionX;
        public float PositionZ;
    }
}
