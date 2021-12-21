using System.Runtime.InteropServices;

namespace GbaMus;

/// <summary>
/// Instrument data.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 12)]
public readonly struct InstData : IComparable<InstData>, IEquatable<InstData>
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

    /// <inheritdoc />
    public bool Equals(InstData other) => Word0 == other.Word0 && Word1 == other.Word1 && Word2 == other.Word2;

    public override bool Equals(object? obj) => obj is InstData other && Equals(other);

    public override int GetHashCode() {
        unchecked {
            var hashCode = (int)Word0;
            hashCode = (hashCode * 397) ^ (int)Word1;
            hashCode = (hashCode * 397) ^ (int)Word2;
            return hashCode;
        }
    }
}
