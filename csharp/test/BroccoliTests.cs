namespace test;

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

    private byte[] Compress(byte[] bytes, bool bare = false, byte window_size = 22)
    {
        using var compressed = new MemoryStream();
        using (var s0 = new BrotliStream(compressed, CompressionMode.Compress, window_bits: window_size,
            bare: bare, catable: bare, byte_align: bare, appendable: bare, magic: bare, leaveOpen: true))
        {
            s0.Write(bytes);
        }
        return compressed.ToArray();
    }

    private byte[] Decompress(Stream compressed, bool bare)
    {
        using Stream decompressedBrotli = bare
            ? new BrotliStream(compressed, CompressionMode.Decompress, bare: bare)
            : new System.IO.Compression.BrotliStream(compressed, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();
        decompressedBrotli.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }

    [TestMethod]
    public void ByteRoundTrip()
    {
        byte[] content = CreateRandomBytes(100, 64);
        using var compressed = new MemoryStream(Compress(content, bare: false));
        byte[] decompressed = Decompress(compressed, bare: false);
        CollectionAssert.AreEqual(content, decompressed);
    }

    [TestMethod]
    public void BareRoundTrip()
    {
        byte[] content = CreateRandomBytes(100, 64);
        var compressed_bare = new MemoryStream(Compress(content, bare: true, window_size: 24));
        {
            compressed_bare.Position = 0;

            var compressed = new MemoryStream();
            compressed.Write(BrotliStream.GetStartBlock(24));
            compressed_bare.CopyTo(compressed);
            compressed.Write(BrotliStream.EndBlock);
            compressed.Position = 0;

            var decompressed = Decompress(compressed, bare: false);
            CollectionAssert.AreEqual(content, decompressed);
        }
        {
            compressed_bare.Position = 0;
            var decompressed = Decompress(compressed_bare, bare: true);
            CollectionAssert.AreEqual(content, decompressed);
        }
    }

    private void BrotliConcatBlocks(byte compress_window_bits, byte concat_window_bits)
    {
        byte[] content0 = CreateRandomBytes(100, 64);
        byte[] compressed0 = Compress(content0, bare: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content0, Decompress(new MemoryStream(compressed0), bare: true));
        byte[] content1 = CreateRandomBytes(100, 64);
        byte[] compressed1 = Compress(content1, bare: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content1, Decompress(new MemoryStream(compressed1), bare: true));

        using var compressed = new MemoryStream();
        compressed.Write(compressed0);
        compressed.Write(compressed1);
        compressed.Position = 0;
        var decompressed = Decompress(compressed, bare: true);

        byte[] content = content0.Concat(content1).ToArray();
        CollectionAssert.AreEqual (content, decompressed);
    }

    private void BareConcatBlocks(byte compress_window_bits)
    {
        byte[] content0 = CreateRandomBytes(100, 64);
        byte[] compressed0 = Compress(content0, bare: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content0, Decompress(new MemoryStream(compressed0), bare: true));
        byte[] content1 = CreateRandomBytes(100, 64);
        byte[] compressed1 = Compress(content1, bare: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content1, Decompress(new MemoryStream(compressed1), bare: true));

        using var compressed = new MemoryStream();
        compressed.Write(BrotliStream.GetStartBlock(compress_window_bits));
        compressed.Write(compressed0);
        compressed.Write(compressed1);
        compressed.Write(BrotliStream.EndBlock);
        compressed.Position = 0;
        var decompressed = Decompress(compressed, bare: false);

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
                Task _ = Task.Run(async () =>
                {
                    using var response = context.Response;
                    if (context.Request.HttpMethod == "POST")
                    {
                        string expected_hash = context.Request.Url!.AbsolutePath.Substring(1);
                        byte[] compressed = new byte[context.Request.ContentLength64];
                        await context.Request.InputStream.ReadExactlyAsync(compressed);
                        BrotliStream decompressed = new(new MemoryStream(compressed), CompressionMode.Decompress, bare: true);
                        using var sha256 = SHA256.Create();
                        byte[] hash_bytes = sha256.ComputeHash(decompressed);
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
                        blob_stream.Write(BrotliStream.GetStartBlock(window_bits));
                        while (null != (line = await input.ReadLineAsync()))
                        {
                            if (!blocks.TryGetValue(line, out byte[]? block))
                            {
                                throw new ArgumentException($"Block {line} not found");
                            }
                            blob_stream.Write(block);
                            byte[] hex = Convert.FromHexString(line);
                            sha256.TransformBlock(hex, 0, hex.Length, null, 0);
                        }
                        sha256.TransformFinalBlock(buffer, 0, 0);
                        string meta_hash = Convert.ToHexString(sha256.Hash!);
                        if (meta_hash != expected_meta_hash)
                        {
                            throw new ArgumentException($"Expected hash {expected_meta_hash}, got {meta_hash}");
                        }
                        blob_stream.Write(BrotliStream.EndBlock);
                        await blob_stream.FlushAsync();
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
                });
            }
        });

        {
            using var client = new HttpClient();
            var original_blob = new MemoryStream();
            var meta_hasher = SHA256.Create();
            (string block_hash, byte[] compressed)[] client_blocks = Enumerable.Range(0,4).Select(_ => {
                var bytes = CreateRandomBytes(100,64);
                original_blob.Write(bytes);
                byte[] block_hash = SHA256.HashData(bytes);
                meta_hasher.TransformBlock(block_hash, 0, block_hash.Length, null, 0);
                var compressed = Compress(bytes, bare: true, window_size: window_bits);
                return (Convert.ToHexString(block_hash), compressed);
            }).ToArray();
            meta_hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            string meta_hash = Convert.ToHexString(meta_hasher.Hash!);

            foreach ((string block_hash, byte[] compressed) in client_blocks)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, uri + block_hash)
                {
                    Content = new ByteArrayContent(compressed)
                };
                (await client.SendAsync(request)).EnsureSuccessStatusCode();
            }

            await client.PutAsync(uri + meta_hash, new StringContent(string.Join("\n", client_blocks.Select(x => x.block_hash))));

            using var response = await client.GetAsync(uri + meta_hash);
            response.EnsureSuccessStatusCode();
            byte[] decompressed = Decompress(await response.Content.ReadAsStreamAsync(), bare: false);
            CollectionAssert.AreEqual(original_blob.ToArray(), decompressed);
        }
    }
}