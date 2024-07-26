using System.Runtime.InteropServices;

namespace broccoli_sharp;

// rust-brotli\c\target\debug\brotli_ffi.dll

public unsafe class Broccoli
{
    public static void Concat(byte window_size, IEnumerable<Stream> inStreams, Stream outputStream)
    {
        Concat(window_size, inStreams, (ArraySegment<byte> segment) => outputStream.Write(segment.Array!, segment.Offset, segment.Count));   
    }

    // transcribed from rust-brotli\src\ffi\broccoli.rs
    public static void Concat(byte window_size, IEnumerable<Stream> inStreams, Action<ArraySegment<byte>> outputCallback)
    {
        BroccoliState state = BroccoliCreateInstanceWithWindowSize(window_size);
        byte[] inBuffer = new byte[4096];
        byte[] outBuffer = new byte[4096];
        int bytesRead;
        fixed (byte* inPtrBase = inBuffer)
        fixed (byte* outPtrBase = outBuffer)
        {
            byte *outPtr = outPtrBase;
            ulong availableOut = (ulong)outBuffer.Length;

            // iterate over inputs
            foreach(Stream inStream in inStreams)
            {
                BroccoliNewBrotliFile(&state);
                
                while (true)
                {
                    bytesRead = inStream.Read(inBuffer, 0, inBuffer.Length);
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
