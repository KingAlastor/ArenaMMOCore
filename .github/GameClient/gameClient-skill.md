# GameClient Unity 6 DOTS Architecture Invariants

### Engine Constraints
*   **Target Engine Target**: Unity 6 (`com.unity.entities: 6.4.0`).
*   **Burst Compilation Architecture**: All movement systems, buffer managers, and snapshot interpolation layers must implement `ISystem` and `IJobEntity`. Reference components must use value types that optimize cache lines and eliminate pointer tracking overhead.
*   **No MonoBehaviour Trapping**: Traditional managed components are prohibited from entering high-frequency loops. Use GameObjects solely for authoring parameters via structural `Baker<T>` routines.

### Network Ingestion & Memory Pipelines
*   **Bridge Ingestion Threads**: Incoming UDP network events processed via LiteNetLib are handed off immediately to unmanaged dynamic entity buffer elements (`IBufferElementData`) to protect the main main-thread pipeline.
*   **Snapshot Interpolation Invariants**: Entities must store a local historic stream using structural array buffers. Visually position elements by lerping between coordinates behind an intentional 100ms jitter delay frame head to counteract runtime routing instability.
