using System.IO.Compression;

namespace BrotliBlockLib;

public enum BlockPosition
{
    First,
    Middle,
    Last,
    Single
}

public static class BrotliBlock
{
    public static byte[] CompressBlock(Stream bytes, BlockPosition position, byte window_size = 22)
    {
        using var compressed = new MemoryStream();

        if (position == BlockPosition.First || position == BlockPosition.Single)
        {
            compressed.Write(BrotliBlockStream.GetStartBlock(window_size));
        }

        using (var s0 = new BrotliBlockStream(compressed, new BrotliCompressionOptions()
        {
            WindowBits = window_size,
            Bare = true,
            Catable = true,
            ByteAlign = true,
            Appendable = true,
            MagicNumber = true
        },
            leaveOpen: true))
        {
            bytes.CopyTo(s0);
        }

        if (position == BlockPosition.Last || position == BlockPosition.Single)
        {
            compressed.Write(BrotliBlockStream.EndBlock);
        }

        return compressed.ToArray();
    }

    public static async Task<byte[]> CompressBlockAsync(Stream bytes, BlockPosition position, byte window_size = 22)
    {
        using var compressed = new MemoryStream();

        if (position == BlockPosition.First || position == BlockPosition.Single)
        {
            compressed.Write(BrotliBlockStream.GetStartBlock(window_size));
        }

        using (var s0 = new BrotliBlockStream(compressed, new BrotliCompressionOptions()
        {
            WindowBits = window_size,
            Bare = true,
            Catable = true,
            ByteAlign = true,
            Appendable = true,
            MagicNumber = true
        },
            leaveOpen: true))
        {
            await bytes.CopyToAsync(s0);
        }

        if (position == BlockPosition.Last || position == BlockPosition.Single)
        {
            compressed.Write(BrotliBlockStream.EndBlock);
        }

        return compressed.ToArray();
    }

    public static byte[] CompressBlock(ReadOnlySpan<byte> bytes, BlockPosition position, byte window_size = 22)
    {
        using var compressed = new MemoryStream();

        if (position == BlockPosition.First || position == BlockPosition.Single)
        {
            compressed.Write(BrotliBlockStream.GetStartBlock(window_size));
        }

        using (var s0 = new BrotliBlockStream(compressed, new BrotliCompressionOptions()
        {
            WindowBits = window_size,
            Bare = true,
            Catable = true,
            ByteAlign = true,
            Appendable = true,
            MagicNumber = true
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

    public static byte[] Compress(ReadOnlySpan<byte> bytes, bool bare = false, byte window_size = 22)
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

    public static byte[] DecompressBlock(Stream compressed, BlockPosition position, byte window_size = 22)
    {
        using Stream decompressedBrotli = BrotliBlockStream.CreateBlockDecompressionStream(compressed, position, window_size: window_size);
        using var decompressedStream = new MemoryStream();
        decompressedBrotli.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }
}