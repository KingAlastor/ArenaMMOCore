// File Path: GameClient/Assets/Netcode/NetworkMovementSystem.cs

using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

namespace GameClient
{
  /// <summary>
  /// Consumes per-entity server snapshot buffers and interpolates entity transforms at render frame rate.
  /// </summary>
  [BurstCompile]
  public partial struct NetworkMovementSystem : ISystem
  {
    // Render 100ms behind server time so minor packet jitter does not cause visible snapping.
    private const double InterpolationDelayMs = 100.0;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
      // Convert frame delta to milliseconds to match packet timestamp units.
      float deltaTimeMs = SystemAPI.Time.DeltaTime * 1000f;

      // Interpolate between two authoritative snapshots around current render time.
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
