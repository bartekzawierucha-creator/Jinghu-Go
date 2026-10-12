using System.Buffers.Binary;
using System.Security.Cryptography;
using JianghuGo.Domain;

namespace JianghuGo.Infrastructure;

public sealed class CsprngSeedSource : IRandomSeedSource
{
    public ulong NextSeed()
    {
        Span<byte> b = stackalloc byte[8];
        RandomNumberGenerator.Fill(b);
        return BinaryPrimitives.ReadUInt64LittleEndian(b);
    }
}
