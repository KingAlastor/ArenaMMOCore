using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;

namespace GameClient
{
    /// <summary>
    /// Consumes server snapshots and interpolates entity transforms at render frame rate.
    /// </summary>
    public partial struct NetworkMovementSystem : ISystem
    {
        // Render 100ms behind server time so minor packet jitter does not cause visible snapping.
        private const double InterpolationDelayMs = 100.0;

        // Frame counter used to throttle console output to roughly once per second at 60fps.
        private int _logFrameCounter;

        public void OnUpdate(ref SystemState state)
        {
            var instance = NetworkClientManager.Instance;
            if (instance == null) return;

            // Convert frame delta to milliseconds to match packet timestamp units.
            float deltaTimeMs = SystemAPI.Time.DeltaTime * 1000f;

            // Phase A: append newest server snapshot into each entity history buffer.
            if (instance.HasUpdate)
            {
                float3 position = instance.ServerPositionUpdate;
                long timestamp = instance.ServerTimestampUpdate;
                instance.HasUpdate = false;

                foreach (var buffer in SystemAPI.Query<DynamicBuffer<SnapshotElement>>())
                {
                    buffer.Add(new SnapshotElement { Position = position, Timestamp = timestamp });

                    // Keep snapshot history bounded to stable memory usage per entity.
                    if (buffer.Length > 20)
                    {
                        buffer.RemoveAt(0);
                    }
                }
            }

            // Phase B: interpolate between two snapshots around current render time.
            foreach (var (transform, buffer, interpState) in 
                    SystemAPI.Query<RefRW<LocalTransform>, DynamicBuffer<SnapshotElement>, RefRW<InterpolationStateComponent>>())
            {
                // Need at least two snapshots to build an interpolation window.
                if (buffer.Length < 2) continue;

                // Initialize playback clock from first snapshot to preserve continuity.
                if (!interpState.ValueRO.IsInitialized)
                {
                    interpState.ValueRW.InterpolationTime = buffer[0].Timestamp;
                    interpState.ValueRW.IsInitialized = true;
                }

                // Move playback time forward using local frame delta.
                interpState.ValueRW.InterpolationTime += deltaTimeMs;
                double renderTime = interpState.ValueRO.InterpolationTime - InterpolationDelayMs;

                // Find two snapshots that bracket current render time.
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
                    // Compute normalized interpolation factor between window endpoints.
                    long timeDifference = targetB.Timestamp - targetA.Timestamp;
                    if (timeDifference > 0)
                    {
                        float lerpFactor = (float)((renderTime - targetA.Timestamp) / timeDifference);

                        // Blend positions for smooth visual motion.
                        transform.ValueRW.Position = math.lerp(targetA.Position, targetB.Position, lerpFactor);

                        // Log interpolated position once per second to confirm movement is live.
                        _logFrameCounter++;
                        if (_logFrameCounter % 60 == 0)
                            Debug.Log($"[Movement] Interpolated pos=({transform.ValueRO.Position.x:F2}, {transform.ValueRO.Position.z:F2}) lerp={lerpFactor:F3} renderT={renderTime:F0}ms");
                    }
                }
                else if (renderTime > buffer[buffer.Length - 1].Timestamp)
                {
                    // If we're beyond known history, clamp to latest authoritative sample.
                    transform.ValueRW.Position = buffer[buffer.Length - 1].Position;
                }
            }
        }
    }
}
