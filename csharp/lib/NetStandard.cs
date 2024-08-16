#if NETSTANDARD

using System.Reflection;

[assembly:AssemblyKeyFile("..\\strong_name.snk")]

public static class Extensions
{
    public static void Write(this Stream stream, byte[] buffer)
    {
        stream.Write(buffer, 0, buffer.Length);
    }
}

namespace System.Buffers
{
    //
    // Summary:
    //     Defines the values that can be returned from span-based operations that support
    //     processing of input contained in multiple discontiguous buffers.
    public enum OperationStatus
    {
        //
        // Summary:
        //     The entire input buffer has been processed and the operation is complete.
        Done = 0,
        //
        // Summary:
        //     The input is partially processed, up to what could fit into the destination buffer.
        //     The caller can enlarge the destination buffer, slice the buffers appropriately,
        //     and retry.
        DestinationTooSmall = 1,
        //
        // Summary:
        //     The input is partially processed, up to the last valid chunk of the input that
        //     could be consumed. The caller can stitch the remaining unprocessed input with
        //     more data, slice the buffers appropriately, and retry.
        NeedMoreData = 2,
        //
        // Summary:
        //     The input contained invalid bytes which could not be processed. If the input
        //     is partially processed, the destination contains the partial result. This guarantees
        //     that no additional data appended to the input will make the invalid sequence
        //     valid.
        InvalidData = 3
    }
}

public readonly struct Span<T>
{
    internal readonly T[] _array;
    internal readonly int _start;
    internal readonly int _length;

    public Span(T[] array)
    {
        _array = array;
        _start = 0;
        _length = array.Length;
    }

    public Span(T[] array, int start)
    {
        _array = array;
        _start = start;
        _length = array.Length - start;
    }

    public Span(T[] array, int start, int length)
    {
        _array = array;
        _start = start;
        _length = length;
    }

    public int Length => _length;

    public ref T this[int index] => ref _array[_start + index];

    public static implicit operator Span<T>(T[] array) => new Span<T>(array);
}

public readonly struct ReadOnlySpan<T>
{
    internal readonly T[] _array;
    internal readonly int _start;
    internal readonly int _length;

    public ReadOnlySpan(T[] array)
    {
        _array = array;
        _start = 0;
        _length = array.Length;
    }

    public ReadOnlySpan(T[] array, int start)
    {
        _array = array;
        _start = start;
        _length = array.Length - start;
    }

    public ReadOnlySpan(T[] array, int start, int length)
    {
        _array = array;
        _start = start;
        _length = length;
    }

    public int Length => _length;

    public ref readonly T this[int index] => ref _array[_start + index];

    public static implicit operator ReadOnlySpan<T>(T[] array) => new ReadOnlySpan<T>(array);
    public static implicit operator ReadOnlySpan<T>(Span<T> span) => new ReadOnlySpan<T>(span._array, span._start, span._length);
}

#endif