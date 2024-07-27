namespace test;

using broccoli_sharp;
using System.IO.Compression;
using BrotliStream = NetFxLab.IO.Compression.BrotliStream;

//using System.IO.Compression;

[TestClass]
public class BroccoliTests
{
    private readonly Random random = new Random();

    private byte[] CreateRandomBytes(int size, byte maxChar=255)
    {
        var bytes = new byte[size];
        lock(random)
        {
            random.NextBytes(bytes);
        }
        for (int i = 0; i < size; i++)
        {
            bytes[i] = (byte)(bytes[i] % (maxChar + 1));
        }
        return bytes;
    }

    private byte[] Compress(byte[] bytes, bool catable = false, bool appendable = false, byte window_size = 24)
    {
        using var compressed = new MemoryStream();
        using (var s0 = new BrotliStream(compressed, CompressionMode.Compress, leaveOpen: true, window_bits: window_size))
        {
            if (catable)
            {
                s0.SetEncoderParameter(167, 1); // BROTLI_PARAM_CATABLE
            }

            if (appendable)
            {
                s0.SetEncoderParameter(168, 1); // BROTLI_PARAM_APPENDABLE
            }

            s0.Write(bytes, 0, bytes.Length);
        }
        return compressed.ToArray();
    }

    private byte[] Decompress(Stream compressed)
    {
        using var decompressedBrotli = new BrotliStream(compressed, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();
        decompressedBrotli.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }

    [TestMethod]
    public void ByteRoundTrip()
    {
        byte[] content = CreateRandomBytes(100, 64);
        using var compressed = new MemoryStream(Compress(content, catable: false, appendable: false));

        byte[] decompressed = Decompress(compressed);
        CollectionAssert.AreEqual(content, decompressed);
    }

    [TestMethod]
    public void ConcatEmpty()
    {
        Broccoli.Concat(
            window_size: 11,
            new[]
            {
                new MemoryStream(),
                new MemoryStream(),
            },
            new MemoryStream());
    }

    private void ConcatBlocks(byte compress_window_bits, byte concat_window_bits)
    {
        byte[] content0 = CreateRandomBytes(100, 64);
        byte[] compressed0 = Compress(content0, catable: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content0, Decompress(new MemoryStream(compressed0)));
        byte[] content1 = CreateRandomBytes(100, 64);
        byte[] compressed1 = Compress(content1, catable: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content1, Decompress(new MemoryStream(compressed1)));

        using var compressed = new MemoryStream();
        Broccoli.Concat(
            concat_window_bits,
            new[]
            {
                new MemoryStream(compressed0),
                new MemoryStream(compressed1),
            },
            compressed);
        compressed.Position = 0;
        var decompressed = Decompress(compressed);

        byte[] content = content0.Concat(content1).ToArray();
        CollectionAssert.AreEqual (content, decompressed);
    }

    [TestMethod]
    public void ConcatBlocksSpecificWindowSize()
    {
        ConcatBlocks(11, 11);
    }

    [TestMethod]
    public void ConcatBlocksUnspecifiedWindowSize()
    {
        ConcatBlocks(11, 0);
    }
}