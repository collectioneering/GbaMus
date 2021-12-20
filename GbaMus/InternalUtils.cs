using System.Buffers;
using System.Buffers.Binary;

namespace GbaMus;

internal static class InternalUtils
{
    private static byte[] Buf8 => _buf8 ??= new byte[8];
    [ThreadStatic] private static byte[]? _buf8;

    public static byte ReadUInt8LittleEndian(this Stream stream)
    {
        stream.ForceRead(Buf8, 0, 1);
        return Buf8[0];
    }

    public static sbyte ReadInt8LittleEndian(this Stream stream)
    {
        stream.ForceRead(Buf8, 0, 1);
        return (sbyte)Buf8[0];
    }

    public static ushort ReadUInt16LittleEndian(this Stream stream)
    {
        stream.ForceRead(Buf8, 0, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(Buf8);
    }

    public static short ReadInt16LittleEndian(this Stream stream)
    {
        stream.ForceRead(Buf8, 0, 2);
        return BinaryPrimitives.ReadInt16LittleEndian(Buf8);
    }

    public static uint ReadUInt32LittleEndian(this Stream stream)
    {
        stream.ForceRead(Buf8, 0, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(Buf8);
    }

    public static int ReadInt32LittleEndian(this Stream stream)
    {
        stream.ForceRead(Buf8, 0, 4);
        return BinaryPrimitives.ReadInt32LittleEndian(Buf8);
    }

    public static ulong ReadUInt64LittleEndian(this Stream stream)
    {
        stream.ForceRead(Buf8, 0, 8);
        return BinaryPrimitives.ReadUInt64LittleEndian(Buf8);
    }

    public static long ReadInt64LittleEndian(this Stream stream)
    {
        stream.ForceRead(Buf8, 0, 8);
        return BinaryPrimitives.ReadInt64LittleEndian(Buf8);
    }

    public static void WriteLittleEndian(this Stream stream, byte value)
    {
        Buf8[0] = value;
        stream.Write(Buf8, 0, 1);
    }

    public static void WriteLittleEndian(this Stream stream, sbyte value)
    {
        Buf8[0] = (byte)value;
        stream.Write(Buf8, 0, 1);
    }

    public static void WriteLittleEndian(this Stream stream, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(Buf8, value);
        stream.Write(Buf8, 0, 2);
    }

    public static void WriteLittleEndian(this Stream stream, short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(Buf8, value);
        stream.Write(Buf8, 0, 4);
    }

    public static void WriteLittleEndian(this Stream stream, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Buf8, value);
        stream.Write(Buf8, 0, 4);
    }

    public static void WriteLittleEndian(this Stream stream, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Buf8, value);
        stream.Write(Buf8, 0, 4);
    }

    public static void WriteLittleEndian(this Stream stream, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(Buf8, value);
        stream.Write(Buf8, 0, 8);
    }

    public static void WriteLittleEndian(this Stream stream, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(Buf8, value);
        stream.Write(Buf8, 0, 8);
    }

    public static unsafe void WriteStructure<T>(this Stream stream, T value, int length) where T : unmanaged
    {
        byte[] arr = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            new Span<byte>(&value, Math.Min(sizeof(T), length)).CopyTo(arr);
            stream.Write(arr, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(arr);
        }
    }

    public static void ForceRead(this Stream stream, byte[] buffer, int offset, int count)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (buffer.Length < offset + count) throw new ArgumentException("Invalid offset and count for this buffer");
        int left = count;
        while (left > 0)
        {
            int read = stream.Read(buffer, offset, left);
            left -= read;
            offset += read;
            if (read == 0 && left != 0) throw new EndOfStreamException();
        }
    }
}
