using UnityEngine;
using Unity.Entities;

namespace GameClient
{
    public class PlayerAuthoring : MonoBehaviour
    {
        public bool IsLocalPlayer = true; // Check this for your local test cube

        // The Baker converts this GameObject into a DOTS entity at runtime
        public class PlayerBaker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                
                // Add the network identifier component
                AddComponent(entity, new NetworkUserComponent { NetworkId = 0 });

                AddBuffer<SnapshotElement>(entity);
                AddComponent<InterpolationStateComponent>(entity);

                // If this is local character, flag it so the system reads keyboard inputs
                if (authoring.IsLocalPlayer)
                {
                    AddComponent<LocalPlayerTag>(entity);
                }
            }
        }
    }
}
