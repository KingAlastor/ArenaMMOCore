using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

namespace GameClient
{
    public partial struct NetworkMovementSystem : ISystem
    {
        private const double InterpolationDelayMs = 100.0; // 100ms buffer to safely survive network spikes

        public void OnUpdate(ref SystemState state)
        {
            var instance = NetworkClientManager.Instance;
            if (instance == null) return;

            float deltaTimeMs = SystemAPI.Time.DeltaTime * 1000f;

            // Phase A: Consume incoming network frames and append them straight into our native entity buffers
            if (instance.HasUpdate)
            {
                float3 position = instance.ServerPositionUpdate;
                long timestamp = instance.ServerTimestampUpdate;
                instance.HasUpdate = false;

                foreach (var buffer in SystemAPI.Query<DynamicBuffer<SnapshotElement>>())
                {
                    buffer.Add(new SnapshotElement { Position = position, Timestamp = timestamp });
                    
                    // Keep the buffer clean: remove old data points if they pile past 20 snapshots
                    if (buffer.Length > 20)
                    {
                        buffer.RemoveAt(0);
                    }
                }
            }

            // Phase B: Interpolation Engine
            // This runs natively at whatever frame rate your monitor handles (60 FPS, 144 FPS, etc.)
            foreach (var (transform, buffer, interpState) in 
                    SystemAPI.Query<RefRW<LocalTransform>, DynamicBuffer<SnapshotElement>, RefRW<InterpolationStateComponent>>())
            {
                if (buffer.Length < 2) continue; // Need at least 2 frames to calculate structural vectors

                // Initialize the structural playback clock head matching our delay requirements
                if (!interpState.ValueRO.IsInitialized)
                {
                    interpState.ValueRW.InterpolationTime = buffer[0].Timestamp;
                    interpState.ValueRW.IsInitialized = true;
                }

                // Advance our visualization clock head
                interpState.ValueRW.InterpolationTime += deltaTimeMs;
                double renderTime = interpState.ValueRO.InterpolationTime - InterpolationDelayMs;

                // Find the two snapshots that surround our render timeline position
                SnapshotElement targetA = buffer[0];
                SnapshotElement targetB = buffer[1];
                bool foundWindow = false;

                for (int i = 0; i < buffer.Length - 1; i++)
                {
                    if (buffer[i].Timestamp <= renderTime && buffer[i + 1].Timestamp >= renderTime)
                    {
                        targetA = buffer[i];
                        targetB = buffer[i + 1];
                        foundWindow = true;
                        break;
                    }
                }

                if (foundWindow)
                {
                    // Calculate exactly how far along we are between target frame A and target frame B
                    long timeDifference = targetB.Timestamp - targetA.Timestamp;
                    if (timeDifference > 0)
                    {
                        float lerpFactor = (float)((renderTime - targetA.Timestamp) / timeDifference);
                        
                        // Smoothly calculate position using CPU-optimized math logic
                        transform.ValueRW.Position = math.lerp(targetA.Position, targetB.Position, lerpFactor);
                    }
                }
                else if (renderTime > buffer[buffer.Length - 1].Timestamp)
                {
                    // If network packets drop entirely, fallback smoothly to our latest valid coordinate position
                    transform.ValueRW.Position = buffer[buffer.Length - 1].Position;
                }
            }
        }
    }
}
