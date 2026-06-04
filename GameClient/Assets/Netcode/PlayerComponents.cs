using Unity.Entities;
using Unity.Mathematics;

namespace GameClient
{
    /// <summary>
    /// Identifies an entity replicated from the server and stores its authoritative id.
    /// </summary>
    public struct NetworkUserComponent : IComponentData
    {
        /// <summary>
        /// Transport-level actor id used to match snapshots to this entity.
        /// </summary>
        public uint NetworkId;
    }

    /// <summary>
    /// Tag component for the local player entity that captures keyboard input.
    /// </summary>
    public struct LocalPlayerTag : IComponentData { }

    /// <summary>
    /// One server snapshot sample stored in a dynamic buffer for interpolation.
    /// </summary>
    public struct SnapshotElement : IBufferElementData
    {
        /// <summary>
        /// Authoritative world position sampled from server state.
        /// </summary>
        public float3 Position;

        /// <summary>
        /// Server-side timestamp in milliseconds for interpolation timeline alignment.
        /// </summary>
        public long Timestamp;
    }

    /// <summary>
    /// Per-entity interpolation playback cursor state.
    /// </summary>
    public struct InterpolationStateComponent : IComponentData
    {
        /// <summary>
        /// Local interpolation playback timestamp in milliseconds.
        /// </summary>
        public double InterpolationTime;

        /// <summary>
        /// Indicates whether the interpolation clock has been seeded from snapshot history.
        /// </summary>
        public bool IsInitialized;
    }
}
