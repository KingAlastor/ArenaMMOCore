using UnityEngine;
using Unity.Entities;

namespace GameClient
{
    /// <summary>
    /// Authoring bridge that converts a scene GameObject into network-ready ECS components.
    /// </summary>
    public class PlayerAuthoring : MonoBehaviour
    {
        // Marks this authored entity as the local input authority during baking.
        public bool IsLocalPlayer = true;

        /// <summary>
        /// Converts authoring data into DOTS components at bake time.
        /// </summary>
        public class PlayerBaker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                // Request a dynamic transform because netcode updates position every frame.
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);

                // Reserve network identity component for server-assigned id.
                AddComponent(entity, new NetworkUserComponent { NetworkId = 0 });

                // Add snapshot history and interpolation clock state used by movement system.
                AddBuffer<SnapshotElement>(entity);
                AddComponent<InterpolationStateComponent>(entity);

                // Add local tag only for the player that drives outbound input packets.
                if (authoring.IsLocalPlayer)
                {
                    AddComponent<LocalPlayerTag>(entity);
                }
            }
        }
    }
}
