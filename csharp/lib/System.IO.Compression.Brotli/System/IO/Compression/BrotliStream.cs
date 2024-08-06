// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using NetFxLab.IO.Compression.Resources;

#if BIT64
    using nuint = System.UInt64;
#else
    using nuint = System.UInt32;
#endif 

namespace NetFxLab.IO.Compression
{
    public partial class BrotliStream : Stream
    {
        private const int DefaultBufferSize = (1 << 16) - 16; //65520
        private int _bufferSize;
        private Stream _stream;
        private CompressionMode _mode;
        private int _availableOutput;
        private int _availableInput;
        private byte[] _buffer;
        private bool _leaveOpen;
        private int totalWrote;
        private Brotli.State _state;
        private TransformationStatus _transformationResult;
        private readonly bool _bare;
        private readonly uint _window_bits;

        public override bool CanTimeout => true;

        public override int ReadTimeout { get; set; }

        public override int WriteTimeout { get; set; }

        public static readonly byte[] EndBlock = new byte[1] { 0x03 };

        private static readonly Lazy<byte[]>[] StartBlocks = Enumerable.Range(0, Brotli.MaxWindowBits + 1)
            .Select((int window_bits) => new Lazy<byte[]>(() => CreateStartBlock((byte)window_bits), LazyThreadSafetyMode.PublicationOnly))
            .ToArray(); 

        private static byte[] CreateStartBlock(byte window_size)
        {
            using var compressed = new MemoryStream();
            using (var s0 = new BrotliStream(compressed, CompressionMode.Compress, window_bits: window_size, 
                appendable: true, byte_align: true, bare: true, leaveOpen: true))
            {
                // empty
            }
                
            return compressed.ToArray();
        }

        public static byte[] GetStartBlock(byte window_size) => StartBlocks[window_size].Value;

        public BrotliStream(Stream baseStream, CompressionMode mode, bool leaveOpen, int bufferSize, CompressionLevel quality) : this(baseStream, mode, leaveOpen, bufferSize)
        {
            if (_mode == CompressionMode.Compress)
            {
                _state.SetQuality((uint)Brotli.GetQualityFromCompressionLevel(quality));
            }
        }

        public BrotliStream(Stream baseStream, CompressionMode mode, bool leaveOpen, int bufferSize, CompressionLevel quality, uint windowSize) : this(baseStream, mode, leaveOpen, bufferSize)
        {
            if (_mode == CompressionMode.Compress)
            {
                _state.SetQuality((uint)Brotli.GetQualityFromCompressionLevel(quality));
                _state.SetWindow(windowSize);
            }
        }

        public BrotliStream(Stream baseStream, CompressionMode mode, bool leaveOpen = false, int bufferSize = DefaultBufferSize,
            uint quality = 11, uint window_bits = 24, bool bare = false, bool catable = false, bool appendable = false, bool byte_align = false,
            bool magic = false)
        {
            if (baseStream == null)
            {
                throw new ArgumentNullException("baseStream");
            }
            _window_bits = window_bits;
            _bufferSize = bufferSize;
            _mode = mode;
            _stream = baseStream;
            _leaveOpen = leaveOpen;
            _bare = bare;
            _state = new Brotli.State();
            _buffer = new byte[_bufferSize];
            _transformationResult = TransformationStatus.NeedMoreSourceData;
            _availableOutput = _bufferSize;
            if (_mode == CompressionMode.Compress)
            {
                _state.SetQuality(quality);
                _state.SetWindow(window_bits);
                WriteTimeout = 0;

                if (catable)
                {
                    _state.SetEncoderParameter(BrotliEncoderParameter.BROTLI_PARAM_CATABLE, 1);
                }
                
                if (appendable)
                {
                    _state.SetEncoderParameter(BrotliEncoderParameter.BROTLI_PARAM_APPENDABLE, 1);
                }

                if (byte_align)
                {
                    _state.SetEncoderParameter(BrotliEncoderParameter.BROTLI_PARAM_BYTE_ALIGN, 1);
                }

                if (_bare)
                {
                    _state.SetEncoderParameter(BrotliEncoderParameter.BROTLI_PARAM_BARE_STREAM, 1);
                }

                if (magic)
                {
                    _state.SetEncoderParameter(BrotliEncoderParameter.BROTLI_PARAM_MAGIC_NUMBER, 1);
                }
            }
            else
            {
                ReadTimeout = 0;

                if (_bare)
                {
                    _transformationResult = TransformationStatus.ReadBareStartBlock;
                }
            }
        }

        public override bool CanRead
        {
            get
            {
                if (_stream == null)
                {
                    return false;
                }
                return (_mode == CompressionMode.Decompress && _stream.CanRead);
            }
        }

        public override bool CanWrite
        {
            get
            {
                if (_stream == null)
                {
                    return false;
                }
                return (_mode == CompressionMode.Compress && _stream.CanWrite);
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override bool CanSeek => false;

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        protected virtual void FlushEncoder(bool finished)
        {
            if (_state.BrotliNativeState == IntPtr.Zero) return;
            if (BrotliNative.BrotliEncoderIsFinished(_state.BrotliNativeState)) return;
            TransformationStatus flushStatus = TransformationStatus.DestinationTooSmall;
            while (flushStatus == TransformationStatus.DestinationTooSmall)
            {
                flushStatus = Brotli.FlushEncoder(Array.Empty<byte>(), _buffer, out _availableInput, out _availableOutput, ref _state, finished);
                _stream.Write(_buffer, 0, _availableOutput);
                _availableOutput = _bufferSize;

                if (BrotliNative.BrotliEncoderIsFinished(_state.BrotliNativeState))
                {
                    break;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _stream != null && _mode == CompressionMode.Compress)
            {
                FlushEncoder(true);
            }
            try
            {
                if (disposing && !_leaveOpen) _stream?.Dispose();
            }
            finally
            {
                _stream = null;
                _state.Dispose();
                base.Dispose(disposing);
            }
        }

        public override void Flush()
        {
            EnsureNotDisposed();
            if (_mode == CompressionMode.Compress)
            {
                FlushEncoder(false);
            }
        }

        private void ValidateParameters(byte[] array, int offset, int count)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (array.Length - offset < count)
                throw new ArgumentOutOfRangeException("Offset and Count aren't consistent", BrotliEx.InvalidArgument);
        }

        private void EnsureDecompressionMode()
        {
            if (_mode != CompressionMode.Decompress)
                throw new System.InvalidOperationException(BrotliEx.WrongModeDecompress);
        }

        private void EnsureNotDisposed()
        {
            if (_stream == null)
                throw new ObjectDisposedException(BrotliEx.StreamDisposed);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            EnsureDecompressionMode();
            ValidateParameters(buffer, offset, count);
            EnsureNotDisposed();
            DateTime begin = DateTime.Now;
            _availableOutput = 0;
            TimeSpan ExecutionTime = DateTime.Now - begin;
            if (ReadTimeout > 0 && ExecutionTime.TotalMilliseconds >= ReadTimeout)
            {
                throw new TimeoutException(BrotliEx.TimeoutRead);
            }
            while (true)
            {
                if (_transformationResult == TransformationStatus.ReadBareStartBlock)
                {
                    byte[] startBlock = StartBlocks[_window_bits].Value;
                    startBlock.CopyTo(_buffer, 0);
                    _availableInput = startBlock.Length;
                }
                else if (_transformationResult == TransformationStatus.NeedMoreSourceData)
                {
                    _availableInput = _stream.Read(_buffer, 0, _bufferSize);
                    if (_availableInput <= 0)
                    {
                        if (_bare)
                        {
                            _transformationResult = TransformationStatus.ReadBareEndBlock;
                            EndBlock.CopyTo(_buffer, 0);
                            _availableInput = EndBlock.Length;
                        }
                        else 
                        {
                            break;
                        }
                    }
                }
                else if (_transformationResult != TransformationStatus.DestinationTooSmall)
                {
                    break;
                }
                _transformationResult = Brotli.Decompress(new ReadOnlySpan<byte>(_buffer, 0, _availableInput), buffer, out _availableInput, out _availableOutput, ref _state);
                if (_availableOutput != 0)
                {
                    return _availableOutput;
                }
            }
            return 0;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        private void EnsureCompressionMode()
        {
            if (_mode != CompressionMode.Compress)
                throw new System.InvalidOperationException(BrotliEx.WrongModeCompress);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCompressionMode();
            ValidateParameters(buffer, offset, count);
            EnsureNotDisposed();
            if (_mode != CompressionMode.Compress)
                totalWrote += count;
            DateTime begin = DateTime.Now;
            int bytesRemain = count;
            int currentOffset = offset;
            int copyLen;
            while (bytesRemain > 0)
            {
                TimeSpan ExecutionTime = DateTime.Now - begin;
                if (WriteTimeout > 0 && ExecutionTime.TotalMilliseconds >= WriteTimeout)
                {
                    throw new TimeoutException(BrotliEx.TimeoutWrite);
                }
                copyLen = bytesRemain > _bufferSize ? _bufferSize : bytesRemain;
                Span<byte> bufferInput = new Span<byte>(buffer, currentOffset, copyLen);
                _transformationResult = Brotli.Compress(bufferInput, _buffer, out _availableInput, out _availableOutput, ref _state);
                if (_transformationResult == TransformationStatus.InvalidData)
                {
                    throw new System.Exception(BrotliEx.unableEncode);
                }
                if (_transformationResult == TransformationStatus.DestinationTooSmall)
                {
                    _stream.Write(_buffer, 0, _availableOutput);
                }
                bytesRemain -= copyLen;
                currentOffset += copyLen;
            }
        }
    }

}
