using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace UrlShortener.Infrastructure.ShortCodes
{
    /// <summary>
    /// A keyed, reversible permutation of <c>[0, 62⁷)</c> (ADR-0005, LLD §6.4).
    /// <list type="bullet">
    /// <item>A 4-round Feistel network over 42 bits (two 21-bit halves). Its round function is
    /// <c>HMAC-SHA256(key, round ‖ R)</c> truncated to 21 bits.</item>
    /// <item><b>Cycle-walking</b> brings results back into range: 2⁴² ≥ 62⁷, so on average ~1.25 rounds of the
    /// network are needed.</item>
    /// </list>
    /// A Feistel network is a bijection whatever its round function, so this maps one-to-one and codes can never
    /// collide. Consecutive IDs give codes that don't look consecutive. <b>The key must never change</b> once codes
    /// exist (ADR-0005).
    /// </summary>
    public sealed class FeistelPermutation
    {
        /// <summary>Minimum passphrase length (decision D2 in TSK-07).</summary>
        public const int MinPassphraseLength = 32;

        private const int HalfBits = 21;
        private const long HalfMask = (1L << HalfBits) - 1;
        private const int Rounds = 4;

        // A bijection can't cycle outside the range for ever; this cap only guards against implementation bugs.
        private const int MaxWalkSteps = 1_000;

        private readonly byte[] _key;

        /// <summary>Creates the permutation from a 32-byte key.</summary>
        /// <param name="key">The secret key (copied).</param>
        public FeistelPermutation(byte[] key)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (key.Length != 32)
            {
                throw new ArgumentException("The Feistel key must be 32 bytes.", nameof(key));
            }

            _key = (byte[])key.Clone();
        }

        /// <summary>Derives the 32-byte key from a passphrase of at least 32 characters (SHA-256).</summary>
        /// <param name="passphrase">The configured secret. Never logged.</param>
        /// <returns>The permutation.</returns>
        public static FeistelPermutation FromPassphrase(string? passphrase)
        {
            if (passphrase == null || passphrase.Length < MinPassphraseLength)
            {
                // The message describes the rule and never contains the value.
                throw new ArgumentException("The Feistel passphrase must be at least 32 characters.", nameof(passphrase));
            }

            return new FeistelPermutation(SHA256.HashData(Encoding.UTF8.GetBytes(passphrase)));
        }

        /// <summary>Maps an ID to its scrambled value; both are in <c>[0, 62⁷)</c>.</summary>
        /// <param name="id">The ID.</param>
        /// <returns>The permuted value.</returns>
        public long Permute(long id)
        {
            EnsureInCodeSpace(id, nameof(id));
            long value = id;
            for (int step = 0; step < MaxWalkSteps; step++)
            {
                value = Forward(value);
                if (value < Base62Encoder.CodeSpaceSize)
                {
                    return value;
                }
            }

            throw new InvalidOperationException("Cycle-walking did not terminate.");
        }

        /// <summary>The inverse of <see cref="Permute"/>, used for tests and future region routing (ADR-0005).</summary>
        /// <param name="value">A permuted value.</param>
        /// <returns>The original ID.</returns>
        public long Invert(long value)
        {
            EnsureInCodeSpace(value, nameof(value));
            long id = value;
            for (int step = 0; step < MaxWalkSteps; step++)
            {
                id = Backward(id);
                if (id < Base62Encoder.CodeSpaceSize)
                {
                    return id;
                }
            }

            throw new InvalidOperationException("Cycle-walking did not terminate.");
        }

        private long Forward(long input)
        {
            long left = (input >> HalfBits) & HalfMask;
            long right = input & HalfMask;
            for (int round = 0; round < Rounds; round++)
            {
                long next = left ^ RoundFunction(round, right);
                left = right;
                right = next;
            }

            return (left << HalfBits) | right;
        }

        private long Backward(long input)
        {
            long left = (input >> HalfBits) & HalfMask;
            long right = input & HalfMask;
            for (int round = Rounds - 1; round >= 0; round--)
            {
                long previous = right ^ RoundFunction(round, left);
                right = left;
                left = previous;
            }

            return (left << HalfBits) | right;
        }

        private long RoundFunction(int round, long half)
        {
            Span<byte> message = stackalloc byte[5];
            message[0] = (byte)round;
            BinaryPrimitives.WriteInt32BigEndian(message.Slice(1), (int)half);

            Span<byte> mac = stackalloc byte[HMACSHA256.HashSizeInBytes];
            HMACSHA256.HashData(_key, message, mac);
            return BinaryPrimitives.ReadInt32BigEndian(mac) & HalfMask;
        }

        private static void EnsureInCodeSpace(long value, string name)
        {
            if (value < 0 || value >= Base62Encoder.CodeSpaceSize)
            {
                throw new ArgumentOutOfRangeException(name, "The value is outside the 7-character code space.");
            }
        }
    }
}
