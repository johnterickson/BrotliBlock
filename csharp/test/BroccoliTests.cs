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

    private byte[] Compress(byte[] bytes, bool catable, bool appendable)
    {
        using var compressed = new MemoryStream();
        using (var s0 = new BrotliStream(compressed, CompressionMode.Compress, leaveOpen: true))
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
            new[]
            {
                new MemoryStream(),
                new MemoryStream(),
            },
            new MemoryStream());
    }

    [TestMethod]
    public void ConcatBlocks()
    {
        byte[] content0 = CreateRandomBytes(10, 64);
        byte[] compressed0 = Compress(content0, catable: true, appendable: false);
        CollectionAssert.AreEqual(content0, Decompress(new MemoryStream(compressed0)));
        byte[] content1 = CreateRandomBytes(10, 64);
        byte[] compressed1 = Compress(content1, catable: true, appendable: false);
        CollectionAssert.AreEqual(content1, Decompress(new MemoryStream(compressed1)));

        using var output = new MemoryStream();
        Broccoli.Concat(
            new[]
            {
                new MemoryStream(compressed0),
                new MemoryStream(compressed1),
            },
            output);
        var decompressed = output.ToArray();

        byte[] content = content0.Concat(content1).ToArray();
        CollectionAssert.AreEqual (content, decompressed);
    }
}