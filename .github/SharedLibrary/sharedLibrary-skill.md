# SharedLibrary Architecture Invariants

### Core Guidelines
*   **Blittable Value Types Only**: Every structural definition must be flat, sequential `struct` configurations containing primitive metrics (`float`, `int`, `uint`, `byte`, `long`).
*   **Memory Attributes**: Utilize `[StructLayout(LayoutKind.Sequential, Pack = 1)]` to guarantee deterministic memory packaging footprint patterns across compiled platforms.
*   **No Reference Tracing**: Do not include classes, interfaces, managed strings (`string`), or heap collections (`List<T>`). Character string indices must be converted to flat fixed byte arrays or integer lookups.
*   **Zero-Encoding Drivers**: Harness `[MemoryPackable] public partial struct` layers. Ensure fields map straight to binary structures without runtime type tag allocations.

### Network API Invariants
```csharp
// Layout Pattern for Zero-Allocation Value Streaming
public static class NetworkSerializer
{
    // Write target uses pre-allocated byte arrays or System.Buffers.ArrayBufferWriter<byte>
    public static void WriteStruct<T>(byte[] targetBuffer, ref T packet, out int bytesWritten) where T : struct;
    public static T ReadStruct<T>(ReadOnlySpan<byte> sourceBuffer) where T : struct;
}
```
