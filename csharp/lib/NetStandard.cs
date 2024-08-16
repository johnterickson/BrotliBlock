
#if NETSTANDARD
using System.Reflection;
[assembly: AssemblyKeyFile("..\\strong_name.snk")]
#endif

internal static class NetstandardCompat
{
    public static void ThrowIfLessThan(int input, int limit, string name)
    {
        if (input < limit)
        {
            throw new ArgumentOutOfRangeException($"{name} {input} is below limit {limit}.");
        }
    }

    public static void ThrowIfGreaterThan(int input, int limit, string name)
    {
        if (input > limit)
        {
            throw new ArgumentOutOfRangeException($"{name} {input} is above limit {limit}.");
        }
    }

    public static void ThrowIfNegative(int input, string name)
    {
        if (input < 0)
        {
            throw new ArgumentOutOfRangeException($"{name} {input} is negative.");
        }
    }

    public static void ThrowIfNull(object input, string name)
    {
        if (input is null)
        {
            throw new ArgumentNullException($"{name} is null.");
        }
    }

    public static void ValidateBufferArguments<T>(T[] array, int offset, int count)
    {
        ThrowIfNull(array, nameof(array));
        ThrowIfNegative(offset, nameof(offset));
        ThrowIfNegative(count, nameof(count));
        ThrowIfGreaterThan(offset + count, array.Length, "offset + count");
    }
}


#if NETSTANDARD
internal static class Extensions
{
    public static void Write(this Stream stream, byte[] buffer)
    {
        stream.Write(buffer, 0, buffer.Length);
    }

    public static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
    {
        // TODO: perf
        stream.Write(buffer.ToArray());
    }

    public static Task WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        // TODO: perf
        byte[] bytes = buffer.ToArray();
        return stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
    }
}
#endif