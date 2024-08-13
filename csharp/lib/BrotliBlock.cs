using System.IO.Compression;
using NetFxLab.IO.Compression;

namespace broccoli_sharp;

/*
https://github.com/dropbox/rust-brotli/pull/49#issuecomment-1804633560

Ok this is really cool! We have a scenario that is similar to https://dropbox.tech/infrastructure/-broccoli--syncing-faster-by-syncing-less where we our customers upload to a content-addressable-store.

Create a header block:
touch empty.bin
 --appendable --bytealign --bare -c empty.bin start.br
Individually compress the actual data blocks like this:
 --catable --bytealign --bare -c block001 block001.br
Then fake a ISLASTEMPTY metablock:
printf "\x03" > ~/end.br

Then you can both

Decompress individual blocks by a) prepending the start block and b) appending the end block: cat start.br block001.br end.br | brotli -d
A standard decompressor (e.g. curl --compressed) can recreate the whole file from the concatenation of all the compressed blocks: e.g. cat start.br block*.br end.br | brotli -d
If you need to, rearrange the compressed blocks order to rearrange the output order

*/

public enum BlockPosition
{
    First,
    Middle,
    Last
}

public static class BrotliBlocks
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