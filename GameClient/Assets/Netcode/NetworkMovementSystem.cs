using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

namespace GameClient
{
    // This system runs on the main DOTS update loop
    public partial struct NetworkMovementSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // Exit early if the network manager hasn't booted or received an update yet
            if (NetworkClientManager.Instance == null || !NetworkClientManager.Instance.HasUpdate)
                return;

            // Pull the latest authoritative tick data safely out of the MonoBridge
            float3 targetPosition = NetworkClientManager.Instance.ServerPositionUpdate;
            
            // Mark the current network packet frame as processed
            NetworkClientManager.Instance.HasUpdate = false;

            // Ultra-fast query loop: Find player entity and immediately write the new location
            foreach (var transform in SystemAPI.Query<RefRW<LocalTransform>>().WithAll<NetworkUserComponent, LocalPlayerTag>())
            {
                transform.ValueRW.Position = targetPosition;
            }
        }
    }
}
