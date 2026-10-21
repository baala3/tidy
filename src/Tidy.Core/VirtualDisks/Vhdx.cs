using System.Buffers.Binary;

namespace Tidy.Core.VirtualDisks;

/// <summary>How much of a VHDX file holds data, and how much compacting could give back.</summary>
public readonly record struct VhdxUsage(long FileBytes, long InUseBytes)
{
    public long ReclaimableBytes => Math.Max(0, FileBytes - InUseBytes);
}

/// <summary>
/// Reads a dynamic VHDX's block allocation table, the format Microsoft publishes as
/// MS-VHDX, to count the blocks that still hold data. The rest of the file is space a
/// compact gives back. Reads only, with sharing open, so it works while WSL has the disk.
/// </summary>
public static class Vhdx
{
    private static readonly Guid BatRegion = new("2DC27766-F623-4200-9D64-115E9BFD4A08");
    private static readonly Guid MetadataRegion = new("8B7CA206-4790-4B9A-B8FE-575F050F886E");
    private static readonly Guid FileParameters = new("CAA16737-FA36-4D43-B3B6-33F0AA44E76B");
    private static readonly Guid VirtualDiskSize = new("2FA54224-CD1B-4876-B211-5DBED83BF4B8");
    private static readonly Guid LogicalSectorSize = new("8141BF1D-A96F-4709-BA47-F233A8FAAB5F");

    private const int HeaderBytes = 4096;
    private const int RegionTableBytes = 64 * 1024;
    private const int MaxMetadataBytes = 1024 * 1024;
    /// <summary>PAYLOAD_BLOCK_FULLY_PRESENT and PAYLOAD_BLOCK_PARTIALLY_PRESENT: blocks with data in the file.</summary>
    private const ulong FullyPresent = 6, PartiallyPresent = 7;

    /// <summary>The usage of a dynamic VHDX, or null for anything else: not a VHDX, damaged, fixed, or differencing.</summary>
    public static VhdxUsage? Read(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static bool HasSignature(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> signature = stackalloc byte[8];
            return file.ReadAtLeast(signature, 8, throwOnEndOfStream: false) == 8 && signature.SequenceEqual("vhdxfile"u8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static VhdxUsage? Read(FileStream file)
    {
        Span<byte> signature = stackalloc byte[8];
        if (file.ReadAtLeast(signature, 8, throwOnEndOfStream: false) != 8 || !signature.SequenceEqual("vhdxfile"u8)) return null;

        // Two copies of the header and of the region table; the valid one with the higher sequence wins.
        var header = Current(file, 64 * 1024, 128 * 1024, "head"u8, HeaderBytes, h => BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(8)));
        var regions = Current(file, 192 * 1024, 256 * 1024, "regi"u8, RegionTableBytes, _ => 0);
        if (header is null || regions is null) return null;
        var logBytes = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(68));

        long batOffset = 0, batBytes = 0, metaOffset = 0, metaBytes = 0;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(regions.AsSpan(8));
        for (var i = 0; i < Math.Min(count, 2047); i++)
        {
            var entry = regions.AsSpan(16 + i * 32, 32);
            var id = new Guid(entry[..16]);
            var offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[24..]);
            if (id == BatRegion) (batOffset, batBytes) = (offset, length);
            else if (id == MetadataRegion) (metaOffset, metaBytes) = (offset, length);
        }
        if (batBytes == 0 || metaBytes is 0 or > MaxMetadataBytes) return null;

        var metadata = ReadAt(file, metaOffset, (int)metaBytes);
        if (!metadata.AsSpan(0, 8).SequenceEqual("metadata"u8)) return null;
        uint blockSize = 0, sectorSize = 0, flags = 0;
        ulong diskSize = 0;
        var entries = BinaryPrimitives.ReadUInt16LittleEndian(metadata.AsSpan(10));
        for (var i = 0; i < entries && 32 + (i + 1) * 32 <= metadata.Length; i++)
        {
            var entry = metadata.AsSpan(32 + i * 32, 32);
            var id = new Guid(entry[..16]);
            var at = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
            if (at < 0 || at + 8 > metadata.Length) continue;
            if (id == FileParameters)
            {
                blockSize = BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(at));
                flags = BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(at + 4));
            }
            else if (id == VirtualDiskSize) diskSize = BinaryPrimitives.ReadUInt64LittleEndian(metadata.AsSpan(at));
            else if (id == LogicalSectorSize) sectorSize = BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(at));
        }
        // LeaveBlockAllocated (fixed) and HasParent (differencing) disks don't compact this way.
        if (blockSize == 0 || sectorSize == 0 || diskSize == 0 || (flags & 3) != 0) return null;

        // After every chunk of payload blocks the table holds one sector-bitmap entry.
        var chunkRatio = (long)((1UL << 23) * sectorSize / blockSize);
        var payloadBlocks = (long)((diskSize + blockSize - 1) / blockSize);
        var bat = ReadAt(file, batOffset, (int)batBytes);
        long present = 0;
        for (long block = 0; block < payloadBlocks; block++)
        {
            var index = block + block / chunkRatio;
            if ((index + 1) * 8 > bat.Length) break;
            var state = BinaryPrimitives.ReadUInt64LittleEndian(bat.AsSpan((int)(index * 8))) & 7;
            if (state is FullyPresent or PartiallyPresent) present++;
        }
        // Everything else in a compacted file: the 1 MB header area, the log, the table and the metadata.
        var overhead = 1024L * 1024 + logBytes + batBytes + metaBytes;
        return new VhdxUsage(file.Length, Math.Min(file.Length, present * blockSize + overhead));
    }

    private static byte[]? Current(FileStream file, long first, long second, ReadOnlySpan<byte> signature, int checksummedBytes, Func<byte[], ulong> sequence)
    {
        byte[]? best = null;
        ulong bestSequence = 0;
        foreach (var offset in new[] { first, second })
        {
            var copy = ReadAt(file, offset, checksummedBytes);
            if (!copy.AsSpan(0, 4).SequenceEqual(signature) || !ChecksumMatches(copy)) continue;
            var number = sequence(copy);
            if (best is null || number > bestSequence) (best, bestSequence) = (copy, number);
        }
        return best;
    }

    /// <summary>CRC-32C over the structure with its own checksum field zeroed.</summary>
    private static bool ChecksumMatches(byte[] data)
    {
        var stored = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var copy = (byte[])data.Clone();
        copy.AsSpan(4, 4).Clear();
        var crc = 0xFFFFFFFFu;
        foreach (var b in copy)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc >> 1) ^ (0x82F63B78u & (uint)-(int)(crc & 1));
        }
        return ~crc == stored;
    }

    private static byte[] ReadAt(FileStream file, long offset, int length)
    {
        var buffer = new byte[length];
        file.Position = offset;
        file.ReadExactly(buffer);
        return buffer;
    }
}
