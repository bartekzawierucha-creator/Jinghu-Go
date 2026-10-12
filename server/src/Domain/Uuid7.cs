using System.Buffers.Binary;
using System.Security.Cryptography;

namespace JianghuGo.Domain;

/// <summary>
/// Monotonic UUIDv7 (RFC 9562, method 1: 12-bit counter in rand_a). Unlike <see cref="Uuid7.New()"/>,
/// IDs created within the same millisecond keep creation order, so "ORDER BY id" is a reliable time order
/// in .NET (<see cref="Guid.CompareTo(Guid)"/>) and in PostgreSQL (bytewise uuid comparison) alike.
/// </summary>
public static class Uuid7
{
    private static readonly Lock Gate = new();
    private static long _lastMs;
    private static int _counter;

    public static Guid New()
    {
        long ms;
        int counter;
        lock (Gate)
        {
            ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (ms <= _lastMs)
            {
                ms = _lastMs;
                if (++_counter > 0xFFF)
                {
                    ms = ++_lastMs; // counter exhausted: borrow the next millisecond
                    _counter = 0;
                }
            }
            else
            {
                _lastMs = ms;
                _counter = 0;
            }

            counter = _counter;
        }

        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b[8..]);
        BinaryPrimitives.WriteInt64BigEndian(b, ms << 16);           // 48-bit timestamp in bytes 0-5
        b[6] = (byte)(0x70 | ((counter >> 8) & 0x0F));               // version 7 + counter high nibble
        b[7] = (byte)(counter & 0xFF);
        b[8] = (byte)(0x80 | (b[8] & 0x3F));                         // RFC 4122/9562 variant
        return new Guid(b, bigEndian: true);
    }
}
