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
}
