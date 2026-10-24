using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace UrlShortener.Core.Analytics
{
    /// <summary>
    /// UUID version 7 (RFC 9562): a 48-bit Unix-millisecond timestamp followed by random bits, so event IDs sort by
    /// time. .NET 8 has no <c>Guid.CreateVersion7</c> (it arrives in .NET 9).
    /// </summary>
    public static class UuidV7
    {
        /// <summary>Creates a UUIDv7 for the given time.</summary>
        /// <param name="timestamp">The event time.</param>
        /// <returns>The UUID.</returns>
        public static Guid Create(DateTimeOffset timestamp)
        {
            Span<byte> bytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(bytes);

            long milliseconds = timestamp.ToUnixTimeMilliseconds();
            Span<byte> time = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(time, milliseconds);
            time.Slice(2, 6).CopyTo(bytes); // the low 48 bits, big-endian

            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70); // version 7
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 9562 variant
            return new Guid(bytes, bigEndian: true);
        }
    }
}
