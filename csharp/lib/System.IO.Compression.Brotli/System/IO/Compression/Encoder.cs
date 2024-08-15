// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace NetFxLab.IO.Compression
{
    internal sealed class Encoder
    {
        internal IntPtr State { get; private set; }
        private const int MinWindowBits = 10;
        private const int MaxWindowBits = 24;
        private const int MinQuality = 0;
        private const int MaxQuality = 11;
        private bool _isDisposed = false;

        internal Encoder()
        {
            _isDisposed = false;
            InitializeEncoder();
        }

        public void SetQuality(uint quality)
        {
            if (quality < MinQuality || quality > MaxQuality)
            {
                throw new ArgumentException("Incorrect value of quality");
            }
            BrotliNative.BrotliEncoderSetParameter(State, BrotliEncoderParameter.Quality, quality);
        }

        public void SetQuality()
        {
            SetQuality(MaxQuality);
        }

        public void SetWindow(uint window)
        {
            if (window < MinWindowBits || window > MaxWindowBits)
            {
                throw new ArgumentException("Incorrect value of WindowSize");
            }
            BrotliNative.BrotliEncoderSetParameter(State, BrotliEncoderParameter.LGWin, window);
        }

        public void SetWindow()
        {
            SetWindow(MaxWindowBits);
        }

        private void InitializeEncoder()
        {
            State = BrotliNative.BrotliEncoderCreateInstance();
            if (State == IntPtr.Zero)
            {
                throw new System.IO.IOException("Encoder instance create fail");
            }
        }

        internal void Dispose()
        {
            if (!_isDisposed && State != IntPtr.Zero)
            {
                BrotliNative.BrotliEncoderDestroyInstance(State);
            }
            _isDisposed = true;
        }
    }
}
