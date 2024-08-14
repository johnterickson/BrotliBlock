using System.IO.Compression;
using NetFxLab.IO.Compression;

namespace BrotliBlock;

public enum BlockPosition
{
    First,
    Middle,
    Last
}

public static class BrotliBlock
{
    public static byte[] CompressBlock(byte[] bytes, BlockPosition position, byte window_size = 22)
    {
        using var compressed = new MemoryStream();
        
        if (position == BlockPosition.First)
        {
            compressed.Write(BrotliBlockStream.GetStartBlock(window_size));
        }

        using (var s0 = BrotliBlockStream.CreateCompressionStream(compressed, window_bits: window_size,
            bare: true, catable: true, byte_align: true, appendable: true, magic: true, leaveOpen: true))
        {
            s0.Write(bytes);
        }

        if (position == BlockPosition.Last)
        {
            compressed.Write(BrotliBlockStream.EndBlock);
        }

        return compressed.ToArray();
    }

    public static byte[] Compress(byte[] bytes, bool bare = false, byte window_size = 22)
    {
        using var compressed = new MemoryStream();
        using (var s0 = BrotliBlockStream.CreateCompressionStream(compressed, window_bits: window_size,
            bare: bare, catable: bare, byte_align: bare, appendable: bare, magic: bare, leaveOpen: true))
        {
            s0.Write(bytes);
        }
        return compressed.ToArray();
    }

    public static byte[] Decompress(Stream compressed, BlockPosition position, byte window_size = 22) => 
        Decompress(compressed, needs_start_block: position != BlockPosition.First, needs_end_block: position != BlockPosition.Last);

    public static byte[] Decompress(Stream compressed, bool needs_start_block = false, bool needs_end_block = false, byte window_size = 22)
    {
        using Stream decompressedBrotli = (needs_start_block || needs_end_block)
            ? BrotliBlockStream.CreateDecompressionStream(compressed, needs_start_block: needs_start_block, needs_end_block: needs_end_block)
            : new BrotliStream(compressed, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();
        decompressedBrotli.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }
}