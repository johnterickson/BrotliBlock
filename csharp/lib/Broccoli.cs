using System.IO.Compression;
using System.Runtime.InteropServices;
using BrotliStream = NetFxLab.IO.Compression.BrotliStream;
using BrotliEncoderParameter = NetFxLab.IO.Compression.BrotliEncoderParameter;
using Brotli = NetFxLab.IO.Compression.Brotli;

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


public unsafe class Broccoli
{
    public static void Concat(byte window_size, IEnumerable<Stream> inStreams, Stream outputStream)
    {
        Concat(
            window_size, 
            inStreams.Select<Stream,Func<byte[],int>>((Stream s) => ((byte[] buffer) => s.Read(buffer, 0, buffer.Length))),
            (ArraySegment<byte> segment) => outputStream.Write(segment.Array!, segment.Offset, segment.Count));   
    }

    // transcribed from rust-brotli\src\ffi\broccoli.rs
    public static void Concat(byte window_size, IEnumerable<Func<byte[],int>> inStreams, Action<ArraySegment<byte>> outputCallback)
    {
        BroccoliState state = window_size == 0
            ? BroccoliCreateInstance()
            : BroccoliCreateInstanceWithWindowSize(window_size);
        byte[] inBuffer = new byte[4096];
        byte[] outBuffer = new byte[4096];
        int bytesRead;
        fixed (byte* inPtrBase = inBuffer)
        fixed (byte* outPtrBase = outBuffer)
        {
            byte *outPtr = outPtrBase;
            ulong availableOut = (ulong)outBuffer.Length;

            // iterate over inputs
            foreach(Func<byte[], int> inStream in inStreams)
            {
                BroccoliNewBrotliFile(&state);
                
                while (true)
                {
                    bytesRead = inStream(inBuffer);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    byte *inPtr = inPtrBase;
                    ulong availableIn = (ulong)bytesRead;
                    while (true)
                    {
                        BroccoliResult result = BroccoliConcatStream(
                            &state,
                            ref availableIn, ref inPtr,
                            ref availableOut, ref outPtr);
                        if (result == BroccoliResult.BroccoliNeedsMoreInput)
                        {
                            break;
                        }
                        if (result == BroccoliResult.BroccoliNeedsMoreOutput)
                        {
                            outputCallback(new ArraySegment<byte>(outBuffer, 0, (int)(outPtr - outPtrBase)));
                            outPtr = outPtrBase;
                            availableOut = (ulong)outBuffer.Length;
                            continue;
                        }
                        throw new Exception($"BroccoliConcatStream failed: {result}");
                    }
                }
            }

            // finalize
            while (true)
            {
                BroccoliResult result = BroccoliConcatFinish(
                    &state,
                    ref availableOut, ref outPtr);

                if (result == BroccoliResult.BroccoliNeedsMoreOutput || result == BroccoliResult.BroccoliSuccess)
                {
                    outputCallback(new ArraySegment<byte>(outBuffer, 0, (int)(outPtr - outPtrBase)));
                    outPtr = outPtrBase;
                    availableOut = (ulong)outBuffer.Length;
                    if (result == BroccoliResult.BroccoliNeedsMoreOutput)
                    {
                        continue;
                    }
                    else
                    {
                        return;
                    }
                }

                throw new Exception($"BroccoliConcatFinish failed: {result}");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BroccoliState
    {
        void* unused;
        fixed byte data[248];
    }

    private enum BroccoliResult
    {
        BroccoliSuccess = 0,
        BroccoliNeedsMoreInput = 1,
        BroccoliNeedsMoreOutput = 2,
        BroccoliBrotliFileNotCraftedForAppend = 124,
        BroccoliInvalidWindowSize = 125,
        BroccoliWindowSizeLargerThanPreviousFile = 126,
        BroccoliBrotliFileNotCraftedForConcatenation = 127,
    };

    // BroccoliState BroccoliCreateInstance();
    [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
    private static extern BroccoliState BroccoliCreateInstance();

    [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
    private static extern BroccoliState BroccoliCreateInstanceWithWindowSize(byte window_size);

    // void BroccoliNewBrotliFile(BroccoliState *state);
    [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
    private static extern void BroccoliNewBrotliFile(
        BroccoliState* state);

    // BroccoliResult BroccoliConcatStream(
    //     BroccoliState *state,
    //     size_t *available_in,
    //     const uint8_t **input_buf_ptr,
    //     size_t *available_out,
    //     uint8_t **output_buf_ptr);
    [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
    private static extern BroccoliResult BroccoliConcatStream(
        BroccoliState* state,
        ref ulong available_in,
        ref byte* input_buf_ptr,
        ref ulong available_out,
        ref byte* output_buf_ptr);

    // BroccoliResult BroccoliConcatFinish(BroccoliState * state,
    //     size_t *available_out,
    //     uint8_t**output_buf);
    [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
    private static extern BroccoliResult BroccoliConcatFinish(
        BroccoliState* state,
        ref ulong available_out,
        ref byte* output_buf_ptr);
}
