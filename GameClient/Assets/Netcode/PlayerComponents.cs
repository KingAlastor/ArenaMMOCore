using Unity.Entities;
using Unity.Mathematics;

namespace GameClient
{
    // Marks this entity as a player controlled by the network
    public struct NetworkUserComponent : IComponentData
    {
        public uint NetworkId;
    }

    // Marks this specific entity as the local player running on this PC
    public struct LocalPlayerTag : IComponentData { }

    //Stores a single historical network point in memory
    public struct SnapshotElement : IBufferElementData
    {
        public float3 Position;
        public long Timestamp;
    }

    //Controls the local interpolation clock state for this specific entity
    public struct InterpolationStateComponent : IComponentData
    {
        public double InterpolationTime; // The current reading "playback head" time
        public bool IsInitialized;
    }
}
