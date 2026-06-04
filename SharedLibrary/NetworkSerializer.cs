using System;
using System.Buffers;
using MemoryPack;

namespace SharedLibrary
{
    /// <summary>
    /// Zero-allocation helpers for serializing blittable packets to reusable network buffers.
    /// </summary>
    public static class NetworkSerializer
    {
        // One writer per thread avoids cross-thread races while still reusing the same backing array.
        [ThreadStatic]
        private static ArrayBufferWriter<byte>? _perThreadBufferWriter;

        private static ArrayBufferWriter<byte> GetBufferWriter()
        {
            // Lazily create the per-thread writer once, then reuse it for all future packets on that thread.
            return _perThreadBufferWriter ??= new ArrayBufferWriter<byte>(1024);
        }

        /// <summary>
        /// Serializes a packet struct into a caller-owned scratch buffer.
        /// </summary>
        /// <typeparam name="T">Blittable packet struct type.</typeparam>
        /// <param name="targetBuffer">Destination byte array reused by transport code.</param>
        /// <param name="packet">Packet value to serialize.</param>
        /// <param name="bytesWritten">Exact serialized payload length.</param>
        public static void WriteStruct<T>(byte[] targetBuffer, ref T packet, out int bytesWritten) where T : struct
        {
            var writer = GetBufferWriter();
            // Reset the write cursor while preserving allocated capacity.
            writer.Clear();

            // MemoryPack writes the binary payload straight into the reusable writer buffer.
            MemoryPackSerializer.Serialize(writer, packet);

            ReadOnlySpan<byte> serializedOutput = writer.WrittenSpan;
            bytesWritten = serializedOutput.Length;

            if (targetBuffer.Length < bytesWritten)
                throw new ArgumentException($"Target destination buffer is too small! Needs {bytesWritten} bytes.");

            // Copy the exact payload into the caller's transport scratch array.
            serializedOutput.CopyTo(targetBuffer);
        }

        /// <summary>
        /// Deserializes a packet struct from a raw network byte span.
        /// </summary>
        /// <typeparam name="T">Expected packet struct type.</typeparam>
        /// <param name="sourceBuffer">Source payload bytes.</param>
        /// <returns>Hydrated packet value.</returns>
        public static T ReadStruct<T>(ReadOnlySpan<byte> sourceBuffer) where T : struct
        {
            return MemoryPackSerializer.Deserialize<T>(sourceBuffer);
        }
    }
}
