// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace System.Buffers
{
    internal abstract class Transformation
    {
        public abstract TransformationStatus Transform(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesConsumed, out int bytesWritten);
    }

    internal enum TransformationStatus
    {
        Done,
        DestinationTooSmall,
        NeedMoreSourceData,
        InvalidData, // TODO: how do we communicate details of the error
        ReadBareStartBlock,
        ReadBareEndBlock,
    }
}
