using System;
using System.Buffers;
using MemoryPack;

namespace SharedLibrary
{
    public static class NetworkSerializer
    {
        // Thread-safe reusable buffer writer to prevent concurrent modification issues
        [ThreadStatic]
        private static ArrayBufferWriter<byte>? _perThreadBufferWriter;

        private static ArrayBufferWriter<byte> GetBufferWriter()
        {
            // Initializes the zero-allocation array buffer once per execution thread
            return _perThreadBufferWriter ??= new ArrayBufferWriter<byte>(1024);
        }

        // Serializes a struct cleanly into a reusable byte array destination
        public static void WriteStruct<T>(byte[] targetBuffer, ref T packet, out int bytesWritten) where T : struct
        {
            var writer = GetBufferWriter();
            writer.Clear(); // Completely reset the writer's write head marker (Zero Heap Allocations)

            // MemoryPack serializes directly into our native buffer layout
            MemoryPackSerializer.Serialize(writer, packet);

            ReadOnlySpan<byte> serializedOutput = writer.WrittenSpan;
            bytesWritten = serializedOutput.Length;

            if (targetBuffer.Length < bytesWritten)
                throw new ArgumentException($"Target destination buffer is too small! Needs {bytesWritten} bytes.");

            // Blit the memory data directly into your network scratch buffer array
            serializedOutput.CopyTo(targetBuffer);
        }

        // Reads data back out of a raw, read-only memory stream with zero allocation friction
        public static T ReadStruct<T>(ReadOnlySpan<byte> sourceBuffer) where T : struct
        {
            return MemoryPackSerializer.Deserialize<T>(sourceBuffer);
        }
    }
}
