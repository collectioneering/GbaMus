using System.Runtime.InteropServices;

namespace GbaMus;

/// <summary>
/// Instrument data.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public readonly struct InstData : IComparable<InstData>
{
    /// <summary>
    /// Word 0.
    /// </summary>
    [FieldOffset(0)] public readonly uint Word0;
    /// <summary>
    /// Word 1.
    /// </summary>
    [FieldOffset(4)] public readonly uint Word1;
    /// <summary>
    /// Word 2.
    /// </summary>
    [FieldOffset(8)] public readonly uint Word2;

    /// <inheritdoc />
    public int CompareTo(InstData other)
    {
        int word2Comparison = Word2.CompareTo(other.Word2);
        if (word2Comparison != 0) return word2Comparison;
        int word1Comparison = Word1.CompareTo(other.Word1);
        if (word1Comparison != 0) return word1Comparison;
        return Word0.CompareTo(other.Word0);
    }
}
