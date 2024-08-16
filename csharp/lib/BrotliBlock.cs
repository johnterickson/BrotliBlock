using System.IO.Compression;

namespace BrotliBlock;

public enum BlockPosition
{
    First,
    Middle,
    Last,
    Single
}

public static class BrotliBlock
{
    public static byte[] CompressBlock(byte[] bytes, BlockPosition position, byte window_size = 22)
    {
        using var compressed = new MemoryStream();
        
        if (position == BlockPosition.First || position == BlockPosition.Single)
        {
            compressed.Write(BrotliBlockStream.GetStartBlock(window_size));
        }

        using (var s0 = new BrotliBlockStream(compressed, new BrotliCompressionOptions()
            {
                WindowBits = window_size, Bare = true, Catable = true, ByteAlign = true, Appendable = true, MagicNumber = true
            },
            leaveOpen: true))
        {
            s0.Write(bytes);
        }

        if (position == BlockPosition.Last || position == BlockPosition.Single)
        {
            compressed.Write(BrotliBlockStream.EndBlock);
        }

        return compressed.ToArray();
    }

    public static byte[] Compress(byte[] bytes, bool bare = false, byte window_size = 22)
    {
        using var compressed = new MemoryStream();

        using (var s0 = new BrotliBlockStream(compressed, new BrotliCompressionOptions()
        {
            WindowBits = window_size,
            Bare = bare,
            Catable = bare,
            ByteAlign = bare,
            Appendable = bare,
            MagicNumber = bare
        },
            leaveOpen: true))
        {
            s0.Write(bytes);
        }

        return compressed.ToArray();
    }

    public static byte[] Decompress(Stream compressed, BlockPosition position, byte window_size = 22)
    {
#if NETSTANDARD
        using Stream decompressedBrotli = BrotliBlockStream.CreateDecompressionStream(compressed, position, window_bits: window_size);
#else
        using Stream decompressedBrotli = position != BlockPosition.Single
            ? new BrotliBlockStream(compressed, position)
            : new BrotliStream(compressed, CompressionMode.Decompress);
#endif
        using var decompressedStream = new MemoryStream();
        decompressedBrotli.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }
}