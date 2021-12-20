using System.Runtime.InteropServices;

#pragma warning disable CS1591

namespace GbaMus;

/// <summary>
/// SF2 spec v2.1 page 19.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 2)]
public readonly struct RangesType
{
    /// <summary>
    /// Low.
    /// </summary>
    [FieldOffset(0)] public readonly byte ByLo;

    /// <summary>
    /// High.
    /// </summary>
    [FieldOffset(1)] public readonly byte ByHi;

    /// <summary>
    /// Initializes a new instance of <see cref="RangesType"/>.
    /// </summary>
    /// <param name="lo">Low.</param>
    /// <param name="hi">High.</param>
    public RangesType(byte lo, byte hi)
    {
        ByLo = lo;
        ByHi = hi;
    }
}

/// <summary>
/// Represents 2 bytes that can handle either two 8-bit values or a  single 16-bit value.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 2)]
public struct GenAmountType
{
    /// <summary>
    /// Ranges.
    /// </summary>
    [FieldOffset(0)] public RangesType Ranges;
    /// <summary>
    /// Signed value.
    /// </summary>
    [FieldOffset(0)] public short ShAmount;
    /// <summary>
    /// Unsigned value.
    /// </summary>
    [FieldOffset(0)] public ushort WAmount;

    /// <summary>
    /// Initializes a new instance of <see cref="GenAmountType"/>.
    /// </summary>
    /// <param name="value">Value to assign.</param>
    public GenAmountType(ushort value = 0)
    {
        Ranges = default;
        ShAmount = default;
        WAmount = value;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GenAmountType"/>.
    /// </summary>
    /// <param name="lo">Low value to assign.</param>
    /// <param name="hi">High value to assign.</param>
    public GenAmountType(byte lo, byte hi)
    {
        ShAmount = default;
        WAmount = default;
        Ranges = new RangesType(lo, hi);
    }
}

/// <summary>
/// SF2 v2.1 spec page 20.
/// </summary>
public enum SfSampleLink : ushort
{
    MonoSample = 1,
    RightSample = 2,
    LeftSample = 4,
    LinkedSample = 8
}

/// <summary>
/// Generator's enumeration class. SF2 v2.1 spec page 38.
/// </summary>
public enum SfGenerator : ushort
{
    Null = 0,
    StartAddrsOffset = 0,
    EndAddrsOffset = 1,
    StartloopAddrsOffset = 2,
    EndloopAddrsOffset = 3,
    StartAddrsCoarseOffset = 4,
    ModLfoToPitch = 5,
    VibLfoToPitch = 6,
    ModEnvToPitch = 7,
    InitialFilterFc = 8,
    InitialFilterQ = 9,
    ModLfoToFilterFc = 10,
    ModEnvToFilterFc = 11,
    EndAddrsCoarseOffset = 12,
    ModLfoToVolume = 13,
    ChorusEffectsSend = 15,
    ReverbEffectsSend = 16,
    Pan = 17,
    DelayModLfo = 21,
    FreqModLfo = 22,
    DelayVibLfo = 23,
    FreqVibLfo = 24,
    DelayModEnv = 25,
    AttackModEnv = 26,
    HoldModEnv = 27,
    DecayModEnv = 28,
    SustainModEnv = 29,
    ReleaseModEnv = 30,
    KeynumToModEnvHold = 31,
    KeynumToModEnvDecay = 32,
    DelayVolEnv = 33,
    AttackVolEnv = 34,
    HoldVolEnv = 35,
    DecayVolEnv = 36,
    SustainVolEnv = 37,
    ReleaseVolEnv = 38,
    KeynumToVolEnvHold = 39,
    KeynumToVolEnvDecay = 40,
    Instrument = 41,
    KeyRange = 43,
    VelRange = 44,
    StartloopAddrsCoarseOffset = 45,
    Keynum = 46,
    Velocity = 47,
    InitialAttenuation = 48,
    EndloopAddrsCoarseOffset = 50,
    CoarseTune = 51,
    FineTune = 52,
    SampleId = 53,
    SampleModes = 54,
    ScaleTuning = 56,
    ExclusiveClass = 57,
    OverridingRootKey = 58,
    EndOper = 60
};

/// <summary>
/// Modulator's enumeration class. SF2 v2.1 spec page 50.
/// </summary>
public enum SfModulator : ushort
{
    Null = 0,
    None = 0,
    NoteOnVelocity = 1,
    NoteOnKey = 2,
    PolyPressure = 10,
    ChnPressure = 13,
    PitchWheel = 14,
    PtchWeelSensivity = 16
};

/// <summary>
/// SF2 v2.1 spec page 52.
/// </summary>
public enum SfTransform : ushort
{
    Null = 0,
    Linear = 0,
    Concave = 1,
    Convex = 2
};
