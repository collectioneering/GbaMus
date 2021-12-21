using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

// ReSharper disable PrivateFieldCanBeConvertedToLocalVariable

namespace GbaMus;

/// <summary>
/// SF2Chunks abstract class.
/// </summary>
public abstract class Sf2Chunks
{
    /// <summary>
    /// 4-letter name of the subchunk.
    /// </summary>
    protected readonly byte[] Name;

    /// <summary>
    /// Size in bytes of the subchunk.
    /// </summary>
    public uint Size;

    /// <summary>
    /// Base soundfont.
    /// </summary>
    protected readonly Sf2 Sf2;

    /// <summary>
    /// Initializes a new instance of <see cref="Sf2Chunks"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="name">Chunk name.</param>
    /// <param name="size">Size in bytes of the subchunk.</param>
    /// <exception cref="ArgumentException"></exception>
    protected Sf2Chunks(Sf2 sf2, string name, uint size = 0) : this(sf2, Encoding.ASCII.GetBytes(name), size)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="Sf2Chunks"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="name">Chunk name.</param>
    /// <param name="size">Size in bytes of the subchunk.</param>
    /// <exception cref="ArgumentException"></exception>
    protected Sf2Chunks(Sf2 sf2, ReadOnlySpan<byte> name, uint size = 0)
    {
        if (name.Length != 4) throw new ArgumentException("Incorrect buffer size", nameof(name));
        Name = new byte[4];
        name.CopyTo(Name);
        Size = size;
        Sf2 = sf2;
    }

    /// <summary>
    /// Writes chunk.
    /// </summary>
    public abstract void Write();

    /// <summary>
    /// Writes the name and size of the (sub)chunk (should be systematically called by sub-classes).
    /// </summary>
    protected void WriteHead()
    {
        Sf2.Stream.Write(Name, 0, 4);
        Sf2.Stream.WriteLittleEndian(Size);
    }
}

/// <summary>
/// Preset header.
/// </summary>
public readonly struct SfPresetHeader
{
    private readonly SfPresetHeaderContent Content;
    private readonly Sf2 Sf2;

    /// <summary>
    /// Initializes a new instance of <see cref="SfPresetHeader"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="name">Name.</param>
    /// <param name="patch">Patch.</param>
    /// <param name="bank">Bank.</param>
    public SfPresetHeader(Sf2 sf2, ReadOnlySpan<byte> name, ushort patch, ushort bank)
    {
        Content = new SfPresetHeaderContent(sf2, name, patch, bank);
        Sf2 = sf2;
    }

    /// <summary>
    /// Writes this header.
    /// </summary>
    public unsafe void Write()
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(38);
        try
        {
            fixed (void* p = &Content)
            {
                new ReadOnlySpan<byte>(p, 38).CopyTo(buf);
            }
            Sf2.Stream.Write(buf, 0, 38);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}

[StructLayout(LayoutKind.Explicit, Size = 38)]
internal unsafe struct SfPresetHeaderContent
{
    /// <summary>
    /// Preset's name.
    /// </summary>
    [FieldOffset(0)] private fixed byte AchPresetName[20];

    /// <summary>
    /// Patch #.
    /// </summary>
    [FieldOffset(20)] private readonly ushort WPreset;

    /// <summary>
    /// Bank #.
    /// </summary>
    [FieldOffset(22)] private readonly ushort WBank;

    /// <summary>
    /// Index to "bag" of instruments (private - created automatically).
    /// </summary>
    [FieldOffset(24)] private readonly ushort WPresetBagNdx;

    /// <summary>
    /// Unused value - should be kept to 0.
    /// </summary>
    [FieldOffset(26)] private readonly uint DwLibrary;

    /// <summary>
    /// Unused value - should be kept to 0.
    /// </summary>
    [FieldOffset(30)] private readonly uint DwGenre;

    /// <summary>
    /// Unuse values - should be kept to 0.
    /// </summary>
    [FieldOffset(38)] private readonly uint DwMorphology;

    public unsafe SfPresetHeaderContent(Sf2 sf2, ReadOnlySpan<byte> name, ushort patch, ushort bank)
    {
        fixed (byte* b = AchPresetName)
            name.Slice(0, Math.Min(20, name.Length)).CopyTo(new Span<byte>(b, 20));
        WPreset = patch;
        WBank = bank;
        DwLibrary = 0;
        DwGenre = 0;
        DwMorphology = 0;
        WPresetBagNdx = sf2.GetPbagSize();
    }
}

/// <summary>
/// Preset bag.
/// </summary>
public readonly struct SfBag
{
    private readonly SfBagContent Content;
    private readonly Sf2 Sf2;

    /// <summary>
    /// Initializes a new instance of <see cref="SfBag"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="preset">True if preset.</param>
    public SfBag(Sf2 sf2, bool preset)
    {
        Content = preset ? new SfBagContent(sf2.GetPgenSize(), sf2.GetPmodSize()) : new SfBagContent(sf2.GetIgenSize(), sf2.GetImodSize());
        Sf2 = sf2;
    }

    /// <summary>
    /// Writes this bag.
    /// </summary>
    public unsafe void Write()
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            fixed (void* p = &Content)
            {
                new ReadOnlySpan<byte>(p, 4).CopyTo(buf);
            }
            Sf2.Stream.Write(buf, 0, 4);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}

[StructLayout(LayoutKind.Explicit, Size = 4)]
internal readonly struct SfBagContent
{
    /// <summary>
    /// Index to list of generators.
    /// </summary>
    [FieldOffset(0)] private readonly ushort WGenNdx;

    /// <summary>
    /// Index to list of modulators.
    /// </summary>
    [FieldOffset(2)] private readonly ushort WModNdx;

    public SfBagContent(ushort wGenNdx, ushort wModNdx)
    {
        WGenNdx = wGenNdx;
        WModNdx = wModNdx;
    }
}

/// <summary>
/// Modulator class.
/// </summary>
public readonly struct SfModList
{
    /// <summary>
    /// Content.
    /// </summary>
    private readonly SfModListContent Content;

    /// <summary>
    /// Base soundfont.
    /// </summary>
    private readonly Sf2 Sf2;

    /// <summary>
    /// Initializes a new instance of <see cref="SfModList"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    public SfModList(Sf2 sf2)
    {
        Content = new SfModListContent();
        Sf2 = sf2;
    }

    /// <summary>
    /// Writes this modulator.
    /// </summary>
    public unsafe void Write()
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(2 * 5);
        try
        {
            fixed (void* p = &Content)
            {
                new ReadOnlySpan<byte>(p, 2 * 5).CopyTo(buf);
            }
            Sf2.Stream.Write(buf, 0, 2 * 5);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}

[StructLayout(LayoutKind.Explicit, Size = 2 * 5)]
internal struct SfModListContent
{
    /// <summary>
    /// Modulator source.
    /// </summary>
    [FieldOffset(0)] private readonly SfModulator SfModSrcOper;

    /// <summary>
    /// Modulator destination.
    /// </summary>
    [FieldOffset(2)] private readonly SfGenerator SfModDestOper;

    /// <summary>
    /// Modulator value.
    /// </summary>
    [FieldOffset(4)] private readonly ushort ModAmount;

    /// <summary>
    /// Modulator source ??
    /// </summary>
    [FieldOffset(6)] private readonly SfModulator SfModAmtSrcOper;

    /// <summary>
    /// Transformation curvative.
    /// </summary>
    [FieldOffset(8)] private readonly SfTransform SfModTransOper;

    public SfModListContent()
    {
        SfModSrcOper = SfModulator.Null;
        SfModDestOper = SfGenerator.Null;
        ModAmount = 0;
        SfModAmtSrcOper = SfModulator.Null;
        SfModTransOper = SfTransform.Null;
    }
}

/// <summary>
/// Generator class.
/// </summary>
public readonly struct SfGenList
{
    /// <summary>
    /// Content.
    /// </summary>
    private readonly SfGenListContent Content;

    /// <summary>
    /// Base soundfont.
    /// </summary>
    private readonly Sf2 Sf2;

    /// <summary>
    /// Initializes a new instance of <see cref="SfGenList"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    public SfGenList(Sf2 sf2)
    {
        Content = new SfGenListContent();
        Sf2 = sf2;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SfGenList"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="operation">Operation.</param>
    /// <param name="amount">Amount.</param>
    public SfGenList(Sf2 sf2, SfGenerator operation, GenAmountType amount)
    {
        Content = new SfGenListContent(operation, amount);
        Sf2 = sf2;
    }

    /// <summary>
    /// Writes this generator.
    /// </summary>
    public unsafe void Write()
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(2 * 2);
        try
        {
            fixed (void* p = &Content)
            {
                new ReadOnlySpan<byte>(p, 2 * 2).CopyTo(buf);
            }
            Sf2.Stream.Write(buf, 0, 2 * 2);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}

[StructLayout(LayoutKind.Explicit, Size = 4)]
internal struct SfGenListContent
{
    [FieldOffset(0)] private readonly SfGenerator SfGenOper;
    [FieldOffset(2)] private readonly GenAmountType GenAmount;

    public SfGenListContent()
    {
        SfGenOper = SfGenerator.Null;
        GenAmount = default;
        GenAmount.ShAmount = 0;
    }

    public SfGenListContent(SfGenerator sfGenOper, GenAmountType genAmount)
    {
        SfGenOper = sfGenOper;
        GenAmount = genAmount;
    }
}

/// <summary>
/// Instrument zone class.
/// </summary>
public readonly struct SfInst
{
    private readonly SfInstContent Content;
    private readonly Sf2 Sf2;

    /// <summary>
    /// Initializes a new instance of <see cref="SfInst"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="name">Instrument name.</param>
    public SfInst(Sf2 sf2, ReadOnlySpan<byte> name)
    {
        Content = new SfInstContent(sf2, name);
        Sf2 = sf2;
    }

    /// <summary>
    /// Writes this zone.
    /// </summary>
    public unsafe void Write()
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(22);
        try
        {
            fixed (void* p = &Content)
            {
                new ReadOnlySpan<byte>(p, 22).CopyTo(buf);
            }
            Sf2.Stream.Write(buf, 0, 22);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}

[StructLayout(LayoutKind.Explicit, Size = 22)]
internal unsafe struct SfInstContent
{
    [FieldOffset(0)] private fixed byte AchPresetName[20];
    [FieldOffset(20)] private readonly ushort WInstBagNx;

    public unsafe SfInstContent(Sf2 sf2, ReadOnlySpan<byte> name)
    {
        fixed (byte* b = AchPresetName)
            name.Slice(0, Math.Min(20, name.Length)).CopyTo(new Span<byte>(b, 20));
        WInstBagNx = sf2.GetIbagSize();
    }
}

/// <summary>
/// Sample class.
/// </summary>
public readonly struct SfSample
{
    /// <summary>
    /// Content.
    /// </summary>
    private readonly SfSampleContent Content;

    /// <summary>
    /// Base soundfont.
    /// </summary>
    private readonly Sf2 Sf2;

    /// <summary>
    /// Initializes a new instance of <see cref="SfSample"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="name">Instrument name.</param>
    /// <param name="start">Sample start.</param>
    /// <param name="end">Sample end.</param>
    /// <param name="startLoop">Start loop.</param>
    /// <param name="endLoop">End loop.</param>
    /// <param name="sampleRate">Sample rate.</param>
    /// <param name="originalPitch">Original pitch.</param>
    /// <param name="pitchCorrection">Pitch correction.</param>
    public SfSample(Sf2 sf2, ReadOnlySpan<byte> name, uint start, uint end, uint startLoop, uint endLoop, uint sampleRate, sbyte originalPitch, sbyte pitchCorrection)
    {
        Content = new SfSampleContent(name, start, end, startLoop, endLoop, sampleRate, originalPitch, pitchCorrection);
        Sf2 = sf2;
    }

    /// <summary>
    /// Writes this zone.
    /// </summary>
    public unsafe void Write()
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(46);
        try
        {
            fixed (void* p = &Content)
            {
                new ReadOnlySpan<byte>(p, 46).CopyTo(buf);
            }
            Sf2.Stream.Write(buf, 0, 46);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }
}

[StructLayout(LayoutKind.Explicit, Size = 46)]
internal unsafe struct SfSampleContent
{
    [FieldOffset(0)] private fixed byte AchPresetName[20];
    [FieldOffset(20)] private readonly uint dwStart;
    [FieldOffset(24)] private readonly uint dwEnd;
    [FieldOffset(28)] private readonly uint dwStartloop;
    [FieldOffset(32)] private readonly uint dwEndloop;
    [FieldOffset(36)] private readonly uint dwSampleRate;
    [FieldOffset(40)] private readonly sbyte byOriginalPitch;
    [FieldOffset(41)] private readonly sbyte chPitchCorrection;
    [FieldOffset(42)] private readonly ushort wSampleLink;
    [FieldOffset(44)] private readonly SfSampleLink sfSampleType;

    public unsafe SfSampleContent(ReadOnlySpan<byte> name, uint start, uint end, uint startLoop, uint endLoop, uint sampleRate, sbyte originalPitch, sbyte pitchCorrection)
    {
        fixed (byte* b = AchPresetName)
            name.Slice(0, Math.Min(20, name.Length)).CopyTo(new Span<byte>(b, 20));
        dwStart = start;
        dwEnd = end;
        dwStartloop = startLoop;
        dwEndloop = endLoop;
        dwSampleRate = sampleRate;
        byOriginalPitch = originalPitch;
        chPitchCorrection = pitchCorrection;
        wSampleLink = 0;
        sfSampleType = SfSampleLink.MonoSample;
    }
}

/// <summary>
/// Version sub-chunk.
/// </summary>
public class IFILSubChunk : Sf2Chunks
{
    /// <summary>
    /// Version major. Default 2.
    /// </summary>
    private readonly ushort WMajor;

    /// <summary>
    /// Version minor. Default 1.
    /// </summary>
    private readonly ushort WMinor;

    /// <summary>
    /// Initializes a new instance of <see cref="IFILSubChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    public IFILSubChunk(Sf2 sf2) : base(sf2, "ifil", 4)
    {
        WMajor = 2;
        WMinor = 1;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        Sf2.Stream.WriteLittleEndian(WMajor);
        Sf2.Stream.WriteLittleEndian(WMinor);
    }
}

/// <summary>
/// Class for the various header chunks that just contain a string.
/// </summary>
public class HeaderSubChunk : Sf2Chunks
{
    /// <summary>
    /// String content.
    /// </summary>
    private readonly string Field;

    /// <summary>
    /// Initializes a new instance of <see cref="HeaderSubChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="subchunkType">Subchunk type.</param>
    /// <param name="s">String content.</param>
    public HeaderSubChunk(Sf2 sf2, string subchunkType, string s) : base(sf2, subchunkType, (uint)Encoding.ASCII.GetByteCount(s) + 1)
    {
        Field = s;
    }

    /// <inheritdoc />
    public override unsafe void Write()
    {
        WriteHead();
        byte[] arr = ArrayPool<byte>.Shared.Rent((int)Size);
        try
        {
            fixed (char* c = Field)
            fixed (byte* b = arr)
                Encoding.ASCII.GetBytes(c, Field.Length, b, (int)(Size - 1));
            Sf2.Stream.Write(arr, 0, (int)Size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(arr);
        }
    }
}

/// <summary>
/// Class for the samples sub chunk.
/// </summary>
public class SMPLSubChunk : Sf2Chunks
{
    /// <summary>
    /// riina: ORIGINAL TABLE
    /// </summary>
    private static readonly byte[] s_convTbl = { 0x00, 0xC0, 0x00, 0xC8, 0x00, 0xD0, 0x00, 0xD8, 0x00, 0xE0, 0x00, 0xE8, 0x00, 0xFF, 0x00, 0xF8, 0x00, 0x00, 0x00, 0x08, 0x00, 0x10, 0x00, 0x18, 0x00, 0x20, 0x00, 0x28, 0x00, 0x30, 0x00, 0x38 };
    //private static readonly byte[] s_convTbl = { 0x00, 0xC0, 0x00, 0xC8, 0x00, 0xD0, 0x00, 0xD8, 0x00, 0xE0, 0x00, 0xE8, 0x00, 0xF0, 0x00, 0xF8, 0x00, 0x00, 0x00, 0x08, 0x00, 0x10, 0x00, 0x18, 0x00, 0x20, 0x00, 0x28, 0x00, 0x30, 0x00, 0x38 };
    private static readonly sbyte[] s_deltaLut = { 0, 1, 4, 9, 16, 25, 36, 49, -64, -49, -36, -25, -16, -9, -4, -1 };
    private static readonly byte[] s_dummy46Samples = new byte[2 * 46];
    private readonly List<Stream> FileList;
    private readonly List<uint> PointerList;
    private readonly List<uint> SizeList;
    private readonly List<bool> LoopFlagList;
    private readonly List<uint> LoopPosList;
    private readonly List<SampleType> SampleTypeList;

    /// <summary>
    /// Initializes a new instance of <see cref="SMPLSubChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont</param>
    public SMPLSubChunk(Sf2 sf2) : base(sf2, "smpl")
    {
        FileList = new List<Stream>();
        PointerList = new List<uint>();
        SizeList = new List<uint>();
        LoopFlagList = new List<bool>();
        LoopPosList = new List<uint>();
        SampleTypeList = new List<SampleType>();
    }

    /// <summary>
    /// Adds a sample to the package.
    /// </summary>
    /// <param name="stream">Stream.</param>
    /// <param name="type">Sample type.</param>
    /// <param name="pointer">Pointer.</param>
    /// <param name="size">Size.</param>
    /// <param name="loopFlag">Loop flag.</param>
    /// <param name="loopPos">Loop position,</param>
    /// <returns>Directory index of the start of the sample.</returns>
    public uint AddSample(Stream stream, SampleType type, uint pointer, uint size, bool loopFlag, uint loopPos)
    {
        FileList.Add(stream);
        PointerList.Add(pointer);
        SizeList.Add(size);
        LoopFlagList.Add(loopFlag);
        LoopPosList.Add(loopPos);
        SampleTypeList.Add(type);
        uint dirOffset = Size >> 1;
        // 2 bytes per sample
        // Compute size including the 8 samples after loop point
        // and 46 dummy samples
        if (loopFlag)
            Size += (size + 8 + 46) * 2;
        else
            Size += (size + 46) * 2;
        return dirOffset;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        for (int i = 0; i < FileList.Count; i++)
        {
            Stream stream = FileList[i];
            stream.Position = PointerList[i];
            uint size = SizeList[i];
            byte[] outBuf = ArrayPool<byte>.Shared.Rent((int)(2 * size));
            outBuf.AsSpan().Clear();
            try
            {
                switch (SampleTypeList[i])
                {
                    case SampleType.UNSIGNED_8:
                        {
                            byte[] data = ArrayPool<byte>.Shared.Rent((int)size);
                            data.AsSpan().Clear();
                            try
                            {
                                stream.ForceRead(data, 0, (int)size);
                                for (int j = 0; j < size; j++)
                                    outBuf[2 * j + 1] = (byte)(data[j] - 0x80);
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(data);
                            }
                            break;
                        }
                    case SampleType.SIGNED_8:
                        {
                            byte[] data = ArrayPool<byte>.Shared.Rent((int)size);
                            data.AsSpan().Clear();
                            try
                            {
                                stream.ForceRead(data, 0, (int)size);
                                for (int j = 0; j < size; j++)
                                    outBuf[2 * j + 1] = data[j];
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(data);
                            }
                            break;
                        }
                    case SampleType.SIGNED_16:
                        {
                            stream.ForceRead(outBuf, 0, (int)(size * 2));
                            break;
                        }
                    case SampleType.GAMEBOY_CH3:
                        {
                            uint numOfRepts = size / 32;
                            byte[] data = ArrayPool<byte>.Shared.Rent(16);
                            data.AsSpan().Clear();
                            try
                            {
                                stream.ForceRead(data, 0, 16);
                                for (int j = 0, l = 0; j < 16; j++)
                                {
                                    for (uint k = numOfRepts; k != 0; k--, l++)
                                    {
                                        int v = data[j] >> 4;
                                        outBuf[2 * l] = s_convTbl[v * 2];
                                        outBuf[2 * l + 1] = s_convTbl[v * 2 + 1];
                                    }
                                    for (uint k = numOfRepts; k != 0; k--, l++)
                                    {
                                        int v = data[j] & 0xf;
                                        outBuf[2 * l] = s_convTbl[v * 2];
                                        outBuf[2 * l + 1] = s_convTbl[v * 2 + 1];
                                    }
                                }
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(data);
                            }
                            break;
                        }
                    case SampleType.BDPCM:
                        {
                            uint nBlocks = size / 64;
                            byte[] data = ArrayPool<byte>.Shared.Rent((int)(nBlocks * 33));
                            data.AsSpan().Clear();
                            stream.ForceRead(data, 0, (int)(nBlocks * 33));
                            try
                            {
                                for (uint block = 0; block < nBlocks; block++)
                                {
                                    sbyte sample = (sbyte)data[block * 33];
                                    outBuf[2 * 64 * block + 1] = (byte)sample;
                                    sample += s_deltaLut[data[block * 33 + 1] & 0xf];
                                    outBuf[2 * 64 * block + 2 + 1] = (byte)sample;
                                    for (uint j = 1; j < 32; j++)
                                    {
                                        byte d = data[block * 33 + j + 1];
                                        sample += s_deltaLut[d >> 4];
                                        outBuf[2 * 64 * block + 2 * 2 * j + 1] = (byte)sample;
                                        sample += s_deltaLut[d & 0xf];
                                        outBuf[2 * 64 * block + 2 * 2 * j + +2 + 1] = (byte)sample;
                                    }
                                }
                                outBuf.AsSpan((int)(2 * 64 * nBlocks), (int)(size * 2 - 2 * 64 * nBlocks)).Clear();
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(data);
                            }
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                Sf2.Stream.Write(outBuf, 0, (int)(2 * size));
                if (LoopFlagList[i])
                    Sf2.Stream.Write(outBuf, (int)(LoopPosList[i] * 2), 2 * 8);
                Sf2.Stream.Write(s_dummy46Samples, 0, 2 * 46);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(outBuf);
            }
        }
    }
}

/// <summary>
/// Preset header list sub-chunk.
/// </summary>
public class PHDRSubChunk : Sf2Chunks
{
    private readonly List<SfPresetHeader> PresetList;

    /// <summary>
    /// Initializes a new instance of <see cref="PHDRSubChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    public PHDRSubChunk(Sf2 sf2) : base(sf2, "phdr")
    {
        PresetList = new List<SfPresetHeader>();
    }

    /// <summary>
    /// Add an existing preset header to the list.
    /// </summary>
    /// <param name="preset">Preset to add.</param>
    public void AddPreset(SfPresetHeader preset)
    {
        PresetList.Add(preset);
        Size += 38;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        foreach (var preset in PresetList)
            preset.Write();
    }
}

/// <summary>
/// Instrument list sub chunk.
/// </summary>
public class INSTSubChunk : Sf2Chunks
{
    private readonly List<SfInst> InstrumentList;

    /// <summary>
    /// Initializes a new instance of <see cref="INSTSubChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    public INSTSubChunk(Sf2 sf2) : base(sf2, "inst")
    {
        InstrumentList = new List<SfInst>();
    }

    /// <summary>
    /// Adds an existing instrument.
    /// </summary>
    /// <param name="instrument">Instrument.</param>
    public void AddInstrument(SfInst instrument)
    {
        InstrumentList.Add(instrument);
        Size += 22;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        foreach (var instrument in InstrumentList)
            instrument.Write();
    }
}

/// <summary>
/// Preset/instrument bag list sub chunk.
/// </summary>
public class BAGSubChunk : Sf2Chunks
{
    internal readonly List<SfBag> BagList;

    /// <summary>
    /// Initializes a new instance of <see cref="BAGSubChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="preset">Preset.</param>
    public BAGSubChunk(Sf2 sf2, bool preset) : base(sf2, preset ? "pbag" : "ibag")
    {
        BagList = new List<SfBag>();
    }

    /// <summary>
    /// Adds a bag.
    /// </summary>
    /// <param name="bag">Bag.</param>
    public void AddBag(SfBag bag)
    {
        BagList.Add(bag);
        Size += 4;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        foreach (var bag in BagList)
            bag.Write();
    }
}

/// <summary>
/// Preset/insrument modulator list class.
/// </summary>
public class MODSubChunk : Sf2Chunks
{
    internal readonly List<SfModList> ModulatorList;

    /// <summary>
    /// Initializes a new instance of <see cref="MODSubChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="preset">Preset.</param>
    public MODSubChunk(Sf2 sf2, bool preset) : base(sf2, preset ? "pmod" : "imod")
    {
        ModulatorList = new List<SfModList>();
    }

    /// <summary>
    /// Adds a modulator.
    /// </summary>
    /// <param name="modulator">Modulator.</param>
    public void AddModulator(SfModList modulator)
    {
        ModulatorList.Add(modulator);
        Size += 10;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        foreach (var modulator in ModulatorList)
            modulator.Write();
    }
}

/// <summary>
/// Preset/instrument generator list class.
/// </summary>
public class GENSubChunk : Sf2Chunks
{
    internal readonly List<SfGenList> GeneratorList;

    /// <summary>
    /// Initializes a new instance of <see cref="GENSubChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    /// <param name="preset">Preset.</param>
    public GENSubChunk(Sf2 sf2, bool preset) : base(sf2, preset ? "pgen" : "igen")
    {
        GeneratorList = new List<SfGenList>();
    }

    /// <summary>
    /// Adds a modulator.
    /// </summary>
    /// <param name="generator">Modulator.</param>
    public void AddGenerator(SfGenList generator)
    {
        GeneratorList.Add(generator);
        Size += 4;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        foreach (var generator in GeneratorList)
            generator.Write();
    }
}

/// <summary>
/// Sample header list class.
/// </summary>
public class SHDRSubChunk : Sf2Chunks
{
    private readonly List<SfSample> SampleList;

    /// <summary>
    /// Initializes a new instance of <see cref="SHDRSubChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    public SHDRSubChunk(Sf2 sf2) : base(sf2, "shdr")
    {
        SampleList = new List<SfSample>();
    }

    /// <summary>
    /// Adds a modulator.
    /// </summary>
    /// <param name="sample">Modulator.</param>
    public void AddSample(SfSample sample)
    {
        SampleList.Add(sample);
        Size += 46;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        foreach (var sample in SampleList)
            sample.Write();
    }
}

/// <summary>
/// Info list chunk, containing info about the SF2 file.
/// </summary>
public class InfoListChunk : Sf2Chunks
{
    private static readonly byte[] s_info = Encoding.ASCII.GetBytes("INFO");

    private readonly IFILSubChunk ifilSubchunk;
    private readonly HeaderSubChunk isngSubchunk;
    private readonly HeaderSubChunk inamSubchunk;
    private readonly HeaderSubChunk iengSubchunk;
    private readonly HeaderSubChunk icopSubchunk;

    /// <summary>
    /// Initializes a new instance of <see cref="InfoListChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    public InfoListChunk(Sf2 sf2) : base(sf2, "LIST")
    {
        ifilSubchunk = new IFILSubChunk(sf2);
        isngSubchunk = new HeaderSubChunk(sf2, "isng", "EMU8000");
        inamSubchunk = new HeaderSubChunk(sf2, "INAM", "Unnamed");
        iengSubchunk = new HeaderSubChunk(sf2, "IENG", "Nintendo Game Boy Advance SoundFont");
        icopSubchunk = new HeaderSubChunk(sf2, "ICOP", "Ripped with SF2Ripper v0.0 (c) 2012 by Bregalad");
    }

    /// <summary>
    /// Computes size of the info-list chunk.
    /// </summary>
    /// <returns>Size of this chunk.</returns>
    public uint CalcSize()
    {
        Size = 4;
        Size += ifilSubchunk.Size + 8;
        Size += isngSubchunk.Size + 8;
        Size += inamSubchunk.Size + 8;
        Size += iengSubchunk.Size + 8;
        Size += icopSubchunk.Size + 8;
        return Size;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        Sf2.Stream.Write(s_info, 0, 4);
        ifilSubchunk.Write();
        isngSubchunk.Write();
        inamSubchunk.Write();
        iengSubchunk.Write();
        icopSubchunk.Write();
    }
}

/// <summary>
/// Sample data list chuk, contains samples.
/// </summary>
public class SdtaListChunk : Sf2Chunks
{
    private static readonly byte[] s_sdta = Encoding.ASCII.GetBytes("sdta");

    internal readonly SMPLSubChunk smplSubchunk;

    /// <summary>
    /// Initializes a new instance of <see cref="SdtaListChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    public SdtaListChunk(Sf2 sf2) : base(sf2, "LIST")
    {
        smplSubchunk = new SMPLSubChunk(sf2);
    }

    /// <summary>
    /// Computes the size of sample-data-list chunk.
    /// </summary>
    /// <returns>Size of chunk.</returns>
    public uint CalcSize()
    {
        Size = 4;
        Size += smplSubchunk.Size + 8;
        return Size;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        Sf2.Stream.Write(s_sdta, 0, 4);
        smplSubchunk.Write();
    }
}

/// <summary>
/// Hydra chunk, contains data for instruments, presets and samples header.
/// </summary>
public class HydraChunk : Sf2Chunks
{
    private static readonly byte[] s_pdta = Encoding.ASCII.GetBytes("pdta");

    internal readonly PHDRSubChunk phdrSubchunk;
    internal readonly BAGSubChunk pbagSubchunk;
    internal readonly MODSubChunk pmodSubchunk;
    internal readonly GENSubChunk pgenSubchunk;
    internal readonly INSTSubChunk instSubchunk;
    internal readonly BAGSubChunk ibagSubchunk;
    internal readonly MODSubChunk imodSubchunk;
    internal readonly GENSubChunk igenSubchunk;
    internal readonly SHDRSubChunk shdrSubchunk;

    /// <summary>
    /// Initializes a new instance of <see cref="HydraChunk"/>.
    /// </summary>
    /// <param name="sf2">Base soundfont.</param>
    public HydraChunk(Sf2 sf2) : base(sf2, "LIST")
    {
        phdrSubchunk = new PHDRSubChunk(sf2);
        pbagSubchunk = new BAGSubChunk(sf2, true);
        pmodSubchunk = new MODSubChunk(sf2, true);
        pgenSubchunk = new GENSubChunk(sf2, true);
        instSubchunk = new INSTSubChunk(sf2);
        ibagSubchunk = new BAGSubChunk(sf2, false);
        imodSubchunk = new MODSubChunk(sf2, false);
        igenSubchunk = new GENSubChunk(sf2, false);
        shdrSubchunk = new SHDRSubChunk(sf2);
    }

    /// <summary>
    /// Computes the size of this chunk.
    /// </summary>
    /// <returns>Size of chunk.</returns>
    public uint CalcSize()
    {
        Size = 4;
        Size += phdrSubchunk.Size + 8;
        Size += pbagSubchunk.Size + 8;
        Size += pmodSubchunk.Size + 8;
        Size += pgenSubchunk.Size + 8;
        Size += instSubchunk.Size + 8;
        Size += ibagSubchunk.Size + 8;
        Size += imodSubchunk.Size + 8;
        Size += igenSubchunk.Size + 8;
        Size += shdrSubchunk.Size + 8;
        return Size;
    }

    /// <inheritdoc />
    public override void Write()
    {
        WriteHead();
        Sf2.Stream.Write(s_pdta, 0, 4);
        phdrSubchunk.Write();
        pbagSubchunk.Write();
        pmodSubchunk.Write();
        pgenSubchunk.Write();
        instSubchunk.Write();
        ibagSubchunk.Write();
        imodSubchunk.Write();
        igenSubchunk.Write();
        shdrSubchunk.Write();
    }
}
