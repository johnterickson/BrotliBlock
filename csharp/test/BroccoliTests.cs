namespace test;

using broccoli_sharp;
using System.IO.Compression;
using BrotliStream = NetFxLab.IO.Compression.BrotliStream;
using BrotliEncoderParameter = NetFxLab.IO.Compression.BrotliEncoderParameter;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using System.Net.WebSockets;
using System.Net.Sockets;

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

    private byte[] Compress(byte[] bytes, bool catable = false, bool appendable = false, bool bare = false, bool bytealign = false, byte window_size = 24)
    {
        using var compressed = new MemoryStream();
        using (var s0 = new BrotliStream(compressed, CompressionMode.Compress, leaveOpen: true, window_bits: window_size))
        {
            if (catable)
            {
                s0.SetEncoderParameter((int)BrotliEncoderParameter.BROTLI_PARAM_CATABLE, 1);
            }

            if (appendable)
            {
                s0.SetEncoderParameter((int)BrotliEncoderParameter.BROTLI_PARAM_APPENDABLE, 1);
            }

            if (bare)
            {
                s0.SetEncoderParameter((int)BrotliEncoderParameter.BROTLI_PARAM_BARE_STREAM, 1);
            }

            if (bytealign)
            {
                s0.SetEncoderParameter((int)BrotliEncoderParameter.BROTLI_PARAM_BYTE_ALIGN, 1);
            }

            s0.Write(bytes, 0, bytes.Length);
        }
        return compressed.ToArray();
    }

    private byte[] Decompress(Stream compressed)
    {
        using var decompressedBrotli = new System.IO.Compression.BrotliStream(compressed, CompressionMode.Decompress);
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
    public void BroccoliConcatEmpty()
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

    [TestMethod]
    public void BareConcatEmpty()
    {
        using var compressed = new MemoryStream();
        compressed.Write(Broccoli.GetStartBlock(11));
        compressed.Write(Broccoli.EndBlock);
        compressed.Position = 0;
        var decompressed = Decompress(compressed);
        Assert.AreEqual(0, decompressed.Length);
    }

    private void BrotliConcatBlocks(byte compress_window_bits, byte concat_window_bits)
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

    private void BareConcatBlocks(byte compress_window_bits)
    {
        byte[] content0 = CreateRandomBytes(100, 64);
        byte[] compressed0 = Compress(content0, catable: true, bare: true, bytealign: true, window_size: compress_window_bits);
        // CollectionAssert.AreEqual(content0, Decompress(new MemoryStream(compressed0)));
        byte[] content1 = CreateRandomBytes(100, 64);
        byte[] compressed1 = Compress(content1, catable: true, bare: true, bytealign: true, window_size: compress_window_bits);
        // CollectionAssert.AreEqual(content1, Decompress(new MemoryStream(compressed1)));

        using var compressed = new MemoryStream();
        compressed.Write(Broccoli.GetStartBlock(compress_window_bits));
        compressed.Write(compressed0);
        compressed.Write(compressed1);
        compressed.Write(Broccoli.EndBlock);
        compressed.Position = 0;
        var decompressed = Decompress(compressed);

        byte[] content = content0.Concat(content1).ToArray();
        CollectionAssert.AreEqual(content, decompressed);
    }

    [TestMethod]
    public void ConcatBlocksSpecificWindowSize()
    {
        BrotliConcatBlocks(11, 11);
        BareConcatBlocks(11);

        BrotliConcatBlocks(22, 22);
        BareConcatBlocks(22);
    }

    [TestMethod]
    public void ConcatBlocksUnspecifiedWindowSize()
    {
        BrotliConcatBlocks(11, 0);
    }

    [TestMethod]
    public async Task HttpServer()
    {
        using var server = new HttpListener();

        // find open port
        int port;
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port; 
        }

        var uri = $"http://127.0.0.1:{port}/";
        server.Prefixes.Add(uri);
        server.Start();

        const byte window_bits = 24;

        Task listenTask = Task.Run(async () =>
        {
            SortedDictionary<string,byte[]> blocks = new();
            byte[]? blob = null;
            byte[] buffer = new byte[4096];

            while (true)
            {
                var context = await server.GetContextAsync();
                using var response = context.Response;
                if (context.Request.HttpMethod == "POST")
                {
                    string expected_hash = context.Request.Url!.AbsolutePath.Substring(1);
                    byte[] header = Broccoli.GetStartBlock(window_bits);
                    byte[] footer = Broccoli.EndBlock;
                    byte[] compressed = new byte[context.Request.ContentLength64 + header.Length + footer.Length];
                    await context.Request.InputStream.ReadExactlyAsync(compressed);
                    BrotliStream decompressed = new(new MemoryStream(compressed), CompressionMode.Decompress);
                    using var sha256 = SHA256.Create();
                    int bytesRead;
                    while (0 != (bytesRead = decompressed.Read(buffer)))
                    {
                        sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                    }
                    byte[] hash_bytes = sha256.TransformFinalBlock(buffer, 0, 0);
                    string hash = Convert.ToHexString(hash_bytes);
                    if (hash != expected_hash)
                    {
                        throw new ArgumentException($"Expected hash {expected_hash}, got {hash}");
                    }
                    blocks[expected_hash] = compressed;
                }
                else if (context.Request.HttpMethod == "PUT")
                {
                    string expected_meta_hash = context.Request.Url!.AbsolutePath.Substring(1);
                    using StreamReader input = new(context.Request.InputStream);
                    string? line;
                    using var sha256 = SHA256.Create();
                    using var blob_stream = new MemoryStream();
                    blob_stream.Write(Broccoli.GetStartBlock(window_bits));
                    while (null != (line = await input.ReadLineAsync()))
                    {
                        if (!blocks.TryGetValue(line, out byte[]? block))
                        {
                            throw new ArgumentException($"Block {line} not found");
                        }
                        blob_stream.Write(block);
                        sha256.TransformBlock(Encoding.UTF8.GetBytes(line), 0, line.Length, null, 0);
                    }
                    string meta_hash = Convert.ToHexString(sha256.TransformFinalBlock(buffer, 0, 0));
                    if (meta_hash != expected_meta_hash)
                    {
                        throw new ArgumentException($"Expected hash {expected_meta_hash}, got {meta_hash}");
                    }
                    blob_stream.Write(Broccoli.EndBlock);
                    blob = blob_stream.ToArray();
                }
                else if (context.Request.HttpMethod == "GET")
                {
                    if (blob == null)
                    {
                        throw new ArgumentException("No blob");
                    }
                    response.ContentLength64 = blob.Length;
                    response.AddHeader("Content-Encoding", "br");
                    await response.OutputStream.WriteAsync(blob);
                }
                else
                {
                    throw new ArgumentException("Unsupported method");
                }

                await response.OutputStream.FlushAsync();
                response.Close();
            }
        });

        {
            using var client = new HttpClient();
            var original_blob = new MemoryStream();
            var hasher = SHA256.Create();
            (string hash, byte[] compressed)[] client_blocks = Enumerable.Range(0,4).Select(_ => {
                var bytes = CreateRandomBytes(100,64);
                original_blob.Write(bytes);
                hasher.TransformBlock(bytes, 0, bytes.Length, null, 0);
                var hash = Convert.ToBase64String(SHA256.HashData(bytes));
                var compressed = Compress(bytes, catable: true, bytealign:true, bare: true, window_size: window_bits);
                return (hash, compressed);
            }).ToArray();
            string meta_hash = Convert.ToBase64String(hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0));

            foreach ((string hash, byte[] compressed) in client_blocks)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, uri + hash)
                {
                    Content = new ByteArrayContent(compressed)
                };
                await client.SendAsync(request);
            }

            await client.PutAsync(uri + meta_hash, new StringContent(string.Join("\n", client_blocks.Select(x => x.hash))));
            using var response = await client.GetAsync(uri + meta_hash);
            byte[] decompressed = await response.Content.ReadAsByteArrayAsync();
            CollectionAssert.AreEqual(original_blob.ToArray(), decompressed);
        }
    }
}