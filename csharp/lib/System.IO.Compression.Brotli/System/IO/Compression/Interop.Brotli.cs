// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static partial class Interop
{
    internal static partial class Brotli
    {
        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static extern SafeBrotliDecoderHandle BrotliDecoderCreateInstance(IntPtr allocFunc, IntPtr freeFunc, IntPtr opaque);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern int BrotliDecoderDecompressStream(
            SafeBrotliDecoderHandle state, ref nuint availableIn, byte** nextIn,
            ref nuint availableOut, byte** nextOut, out nuint totalOut);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern BOOL BrotliDecoderDecompress(nuint availableInput, byte* inBytes, nuint* availableOutput, byte* outBytes);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BrotliDecoderDestroyInstance(IntPtr state);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static extern BOOL BrotliDecoderIsFinished(SafeBrotliDecoderHandle state);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static extern SafeBrotliEncoderHandle BrotliEncoderCreateInstance(IntPtr allocFunc, IntPtr freeFunc, IntPtr opaque);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static extern BOOL BrotliEncoderSetParameter(SafeBrotliEncoderHandle state, BrotliEncoderParameter parameter, uint value);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern BOOL BrotliEncoderCompressStream(
            SafeBrotliEncoderHandle state, BrotliEncoderOperation op, ref nuint availableIn,
            byte** nextIn, ref nuint availableOut, byte** nextOut, out nuint totalOut);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static extern BOOL BrotliEncoderHasMoreOutput(SafeBrotliEncoderHandle state);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BrotliEncoderDestroyInstance(IntPtr state);

        [DllImport("brotli_ffi", CallingConvention = CallingConvention.Cdecl)]
        internal static unsafe extern BOOL BrotliEncoderCompress(int quality, int window, int v, nuint availableInput, byte* inBytes, nuint* availableOutput, byte* outBytes);
    }
}
