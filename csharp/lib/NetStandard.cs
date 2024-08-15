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

internal readonly struct Span<T>
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

internal readonly struct ReadOnlySpan<T>
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