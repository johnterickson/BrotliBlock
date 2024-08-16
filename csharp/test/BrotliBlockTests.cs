namespace test;

using NetFxLab.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Net.Sockets;
using BrotliBlock;

[TestClass]
public class BrotliBlockTests
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

    [TestMethod]
    public void ByteRoundTrip()
    {
        byte[] content = CreateRandomBytes(100, 64);
        byte[] compressed = BrotliBlock.Compress(content, bare: false);
        byte[] decompressed = BrotliBlock.Decompress(new MemoryStream(compressed), BlockPosition.Single);
        CollectionAssert.AreEqual(content, decompressed);
    }

    [TestMethod]
    public void PositionRoundTrip()
    {
        byte[] content = CreateRandomBytes(100, 64);

        foreach (BlockPosition position in Enum.GetValues(typeof(BlockPosition)))
        {
            using var compressed = new MemoryStream(BrotliBlock.CompressBlock(content, position, window_size: 24));
            byte[] decompressed = BrotliBlock.Decompress(compressed, position, window_size: 24);
            CollectionAssert.AreEqual(content, decompressed);
        }
    }

    [TestMethod]
    public void BareRoundTrip()
    {
        byte[] content = CreateRandomBytes(100, 64);
        var compressed_bare = new MemoryStream(BrotliBlock.Compress(content, bare: true, window_size: 24));
        {
            compressed_bare.Position = 0;

            var compressed = new MemoryStream();
            compressed.Write(BrotliBlockStream.GetStartBlock(24));
            compressed_bare.CopyTo(compressed);
            compressed.Write(BrotliBlockStream.EndBlock);
            compressed.Position = 0;

            var decompressed = BrotliBlock.Decompress(compressed, BlockPosition.Single);
            CollectionAssert.AreEqual(content, decompressed);
        }
        {
            compressed_bare.Position = 0;
            var decompressed = BrotliBlock.Decompress(compressed_bare, BlockPosition.Middle);
            CollectionAssert.AreEqual(content, decompressed);
        }
    }

    private void BrotliConcatBlocks(byte compress_window_bits, byte concat_window_bits)
    {
        byte[] content0 = CreateRandomBytes(100, 64);
        byte[] compressed0 = BrotliBlock.Compress(content0, bare: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content0, BrotliBlock.Decompress(new MemoryStream(compressed0), BlockPosition.Middle));
        byte[] content1 = CreateRandomBytes(100, 64);
        byte[] compressed1 = BrotliBlock.Compress(content1, bare: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content1, BrotliBlock.Decompress(new MemoryStream(compressed1), BlockPosition.Middle));

        using var compressed = new MemoryStream();
        compressed.Write(compressed0);
        compressed.Write(compressed1);
        compressed.Position = 0;
        var decompressed = BrotliBlock.Decompress(compressed, BlockPosition.Middle);

        byte[] content = content0.Concat(content1).ToArray();
        CollectionAssert.AreEqual (content, decompressed);
    }

    private void BareConcatBlocks(byte compress_window_bits)
    {
        byte[] content0 = CreateRandomBytes(100, 64);
        byte[] compressed0 = BrotliBlock.Compress(content0, bare: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content0, BrotliBlock.Decompress(new MemoryStream(compressed0), BlockPosition.Middle));
        byte[] content1 = CreateRandomBytes(100, 64);
        byte[] compressed1 = BrotliBlock.Compress(content1, bare: true, window_size: compress_window_bits);
        CollectionAssert.AreEqual(content1, BrotliBlock.Decompress(new MemoryStream(compressed1), BlockPosition.Middle));

        using var compressed = new MemoryStream();
        compressed.Write(BrotliBlockStream.GetStartBlock(compress_window_bits));
        compressed.Write(compressed0);
        compressed.Write(compressed1);
        compressed.Write(BrotliBlockStream.EndBlock);
        compressed.Position = 0;
        var decompressed = BrotliBlock.Decompress(compressed, BlockPosition.Single);

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

#if NETCORE
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
            SortedDictionary<(string,BlockPosition),byte[]> blocks = new();
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
                        string[] path = context.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        string expected_hash = path[0];
                        BlockPosition position = Enum.Parse<BlockPosition>(path[1]);
                        byte[] compressed = new byte[context.Request.ContentLength64];
                        await context.Request.InputStream.ReadExactlyAsync(compressed);
                        byte[] decompressed = BrotliBlock.Decompress(new MemoryStream(compressed), position, window_bits);
                        using var sha256 = SHA256.Create();
                        byte[] hash_bytes = sha256.ComputeHash(decompressed);
                        string hash = Convert.ToHexString(hash_bytes);
                        if (hash != expected_hash)
                        {
                            throw new ArgumentException($"Expected hash {expected_hash}, got {hash}");
                        }
                        blocks[(expected_hash,position)] = compressed;
                    }
                    else if (context.Request.HttpMethod == "PUT")
                    {
                        string expected_meta_hash = context.Request.Url!.AbsolutePath.Substring(1);
                        using StreamReader input = new(context.Request.InputStream);
                        
                        string? line;
                        using var sha256 = SHA256.Create();
                        List<string> block_hashes = new();
                        while (null != (line = await input.ReadLineAsync()))
                        {
                            block_hashes.Add(line);
                            byte[] hex = Convert.FromHexString(line);
                            sha256.TransformBlock(hex, 0, hex.Length, null, 0);
                        }
                        sha256.TransformFinalBlock(buffer, 0, 0);
                        string meta_hash = Convert.ToHexString(sha256.Hash!);
                        if (meta_hash != expected_meta_hash)
                        {
                            throw new ArgumentException($"Expected hash {expected_meta_hash}, got {meta_hash}");
                        }

                        using var blob_stream = new MemoryStream();
                        for (int i = 0; i < block_hashes.Count; i++)
                        {
                            BlockPosition position ;
                            if (i == 0)
                            {
                                position = BlockPosition.First;
                            }
                            else if (i == block_hashes.Count - 1)
                            {
                                position = BlockPosition.Last;
                            }
                            else
                            {
                                position = BlockPosition.Middle;
                            }
                            string block_hash = block_hashes[i];
                            if (!blocks.TryGetValue((block_hash, position), out byte[]? block))
                            {
                                throw new ArgumentException($"Block {block_hash} not found");
                            }
                            blob_stream.Write(block);
                            Assert.AreEqual(
                                block_hash,
                                Convert.ToHexString(SHA256.Create().ComputeHash(BrotliBlock.Decompress(new MemoryStream(block), position, window_bits))));
                        }
                        
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
            var compressed_blocks_concat = new MemoryStream();
            var original_blob = new MemoryStream();
            var meta_hasher = SHA256.Create();
            int block_count = 4;
            (string block_hash, BlockPosition position, byte[] compressed)[] client_blocks = Enumerable.Range(0,4).Select(i => {
                var bytes = CreateRandomBytes(100,64);
                original_blob.Write(bytes);
                byte[] block_hash = SHA256.HashData(bytes);
                meta_hasher.TransformBlock(block_hash, 0, block_hash.Length, null, 0);

                BlockPosition position;
                if (i == 0)
                {
                    position = BlockPosition.First;
                }
                else if (i == block_count - 1)
                {
                    position = BlockPosition.Last;
                }
                else
                {
                    position = BlockPosition.Middle;
                }
                byte[] compressed = BrotliBlock.CompressBlock(bytes, position, window_size: window_bits);
    
                byte[] decompressed = BrotliBlock.Decompress(new MemoryStream(compressed), position, window_bits);
                CollectionAssert.AreEqual(bytes.ToArray(), decompressed);

                compressed_blocks_concat.Write(compressed);
                return (Convert.ToHexString(block_hash), position, compressed);
            }).ToArray();
            meta_hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            string meta_hash = Convert.ToHexString(meta_hasher.Hash!);

            compressed_blocks_concat.Position = 0;
            byte[] decompressed_concat = BrotliBlock.Decompress(compressed_blocks_concat);
            CollectionAssert.AreEqual(original_blob.ToArray(), decompressed_concat);

            foreach ((string block_hash, BlockPosition position, byte[] compressed) in client_blocks)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, uri + block_hash + "/" + position)
                {
                    Content = new ByteArrayContent(compressed)
                };
                (await client.SendAsync(request)).EnsureSuccessStatusCode();
            }

            await client.PutAsync(uri + meta_hash, new StringContent(string.Join("\n", client_blocks.Select(x => x.block_hash))));

            using var response = await client.GetAsync(uri + meta_hash);
            response.EnsureSuccessStatusCode();
            byte[] decompressed = BrotliBlock.Decompress(await response.Content.ReadAsStreamAsync());
            CollectionAssert.AreEqual(original_blob.ToArray(), decompressed);
        }
    }
#endif
}