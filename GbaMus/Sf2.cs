using System.Text;

namespace GbaMus;

/// <summary>
/// Represents a SF2 soundfont.
/// </summary>
public sealed class Sf2 : IDisposable
{
    private static readonly byte[] s_riff = Encoding.ASCII.GetBytes("RIFF");
    private static readonly byte[] s_sfbk = Encoding.ASCII.GetBytes("sfbk");
    private static readonly byte[] s_eos = Encoding.ASCII.GetBytes("EOS");
    private static readonly byte[] s_eoi = Encoding.ASCII.GetBytes("EOI");
    private static readonly byte[] s_eop = Encoding.ASCII.GetBytes("EOP");

    private bool _output;
    private uint _size;
    private readonly InfoListChunk _infoListChunk;
    private readonly SdtaListChunk _sdtaListChunk;
    private readonly HydraChunk _hydraChunk;

    private void AddTerminals()
    {
        AddNewSampleHeader(s_eos, 0, 0, 0, 0, 0, 0, 0);
        AddNewInstrument(s_eoi);
        AddNewInstBag();
        AddNewInstGenerator();
        AddNewInstModulator();
        AddNewPreset(s_eop, 255, 255);
        AddNewPresetBag();
        AddNewPresetGenerator();
        AddNewPresetModulator();
    }

    /// <summary>
    /// Target stream.
    /// </summary>
    public Stream Stream;

    /// <summary>
    /// Default sample rate.
    /// </summary>
    public uint DefaultSampleRate;

    /// <summary>
    /// Initializes a new instance <see cref="Sf2"/>.
    /// </summary>
    /// <param name="sampleRate">Sample rate.</param>
    public Sf2(uint sampleRate = 22050)
    {
        Stream = null!;
        _size = 0;
        _infoListChunk = new InfoListChunk(this);
        _sdtaListChunk = new SdtaListChunk(this);
        _hydraChunk = new HydraChunk(this);
        DefaultSampleRate = sampleRate;
    }

    /// <summary>
    /// Writes this soundfont to a stream.
    /// </summary>
    /// <param name="stream">Stream to write to.</param>
    public void Write(Stream stream)
    {
        if (_output) throw new InvalidOperationException($"Already called {nameof(Write)}");
        _output = true;
        Stream = stream;
        AddTerminals();
        _size = 4;
        _size += _infoListChunk.CalcSize() + 8;
        _size += _sdtaListChunk.CalcSize() + 8;
        _size += _hydraChunk.CalcSize() + 8;
        Stream.Write(s_riff, 0, 4);
        Stream.WriteLittleEndian(_size);
        Stream.Write(s_sfbk, 0, 4);
        _infoListChunk.Write();
        _sdtaListChunk.Write();
        _hydraChunk.Write();
    }

    /// <summary>
    /// Adds a new preset.
    /// </summary>
    /// <param name="name">Preset name.</param>
    /// <param name="patch">Patch.</param>
    /// <param name="bank">Bank.</param>
    public void AddNewPreset(ReadOnlySpan<byte> name, ushort patch, ushort bank)
    {
        _hydraChunk._phdrSubchunk.AddPreset(new SfPresetHeader(this, name, patch, bank));
    }

    /// <summary>
    /// Adds a new instrument.
    /// </summary>
    /// <param name="name">Instrument name.</param>
    public void AddNewInstrument(ReadOnlySpan<byte> name)
    {
        _hydraChunk._instSubchunk.AddInstrument(new SfInst(this, name));
    }

    /// <summary>
    /// Adds a new instrument bag to the instrument bag list.
    /// </summary>
    /// <remarks>Do not use this to add a preset bag.</remarks>
    public void AddNewInstBag()
    {
        _hydraChunk._ibagSubchunk.AddBag(new SfBag(this, false));
    }

    /// <summary>
    /// Adds a new preset bag to the preset bag list.
    /// </summary>
    /// <remarks>Do not use this to add an instrument bag.</remarks>
    public void AddNewPresetBag()
    {
        _hydraChunk._pbagSubchunk.AddBag(new SfBag(this, true));
    }

    /// <summary>
    /// Adds a new modulator to the list.
    /// </summary>
    public void AddNewPresetModulator()
    {
        _hydraChunk._pmodSubchunk.AddModulator(new SfModList(this));
    }

    /// <summary>
    /// Adds a new blank preset generator to the list.
    /// </summary>
    public void AddNewPresetGenerator()
    {
        _hydraChunk._pgenSubchunk.AddGenerator(new SfGenList(this));
    }

    /// <summary>
    /// Adds a new customized preset generator to the list.
    /// </summary>
    /// <param name="operation">Operation.</param>
    /// <param name="value">Value.</param>
    public void AddNewPresetGenerator(SfGenerator operation, ushort value)
    {
        _hydraChunk._pgenSubchunk.AddGenerator(new SfGenList(this, operation, new GenAmountType(value)));
    }

    /// <summary>
    /// Adds a new customized preset generator to the list.
    /// </summary>
    /// <param name="operation">Operation.</param>
    /// <param name="lo">Low.</param>
    /// <param name="hi">High.</param>
    public void AddNewPresetGenerator(SfGenerator operation, byte lo, byte hi)
    {
        _hydraChunk._pgenSubchunk.AddGenerator(new SfGenList(this, operation, new GenAmountType(lo, hi)));
    }

    /// <summary>
    /// Adds a new modulator to the list.
    /// </summary>
    public void AddNewInstModulator()
    {
        _hydraChunk._imodSubchunk.AddModulator(new SfModList(this));
    }

    /// <summary>
    /// Adds a new blank generator to the list.
    /// </summary>
    public void AddNewInstGenerator()
    {
        _hydraChunk._igenSubchunk.AddGenerator(new SfGenList(this));
    }

    /// <summary>
    /// Adds a new customized generator to the list.
    /// </summary>
    /// <param name="operation">Operation.</param>
    /// <param name="value">Value.</param>
    public void AddNewInstGenerator(SfGenerator operation, ushort value)
    {
        _hydraChunk._igenSubchunk.AddGenerator(new SfGenList(this, operation, new GenAmountType(value)));
    }

    /// <summary>
    /// Adds a new customized generator to the list.
    /// </summary>
    /// <param name="operation">Operation.</param>
    /// <param name="lo">Low.</param>
    /// <param name="hi">High.</param>
    public void AddNewInstGenerator(SfGenerator operation, byte lo, byte hi)
    {
        _hydraChunk._igenSubchunk.AddGenerator(new SfGenList(this, operation, new GenAmountType(lo, hi)));
    }

    /// <summary>
    /// Adds a new header.
    /// </summary>
    /// <param name="name">Sample name.</param>
    /// <param name="start">Sample start.</param>
    /// <param name="end">Sample end.</param>
    /// <param name="startLoop">Sample start loop.</param>
    /// <param name="endLoop">Sample end loop.</param>
    /// <param name="sampleRate">Sample rate.</param>
    /// <param name="originalPitch">Original pitch.</param>
    /// <param name="pitchCorrection">Pitch correction.</param>
    public void AddNewSampleHeader(ReadOnlySpan<byte> name, uint start, uint end, uint startLoop, uint endLoop, uint sampleRate, sbyte originalPitch, sbyte pitchCorrection)
    {
        _hydraChunk._shdrSubchunk.AddSample(new SfSample(this, name, start, end, startLoop, endLoop, sampleRate, originalPitch, pitchCorrection));
    }

    /// <summary>
    /// Adds a new sample and creates corresponding header.
    /// </summary>
    /// <param name="stream">Source stream.</param>
    /// <param name="type">Sample type.</param>
    /// <param name="name">Sample nane.</param>
    /// <param name="pointer">Sample offset.</param>
    /// <param name="size">Size.</param>
    /// <param name="loopFlag">Loop flag.</param>
    /// <param name="loopPos">Loop position.</param>
    /// <param name="originalPitch">Original pitch.</param>
    /// <param name="pitchCorrection">Pitch correction.</param>
    /// <param name="sampleRate">Sample rate.</param>
    public void AddNewSample(Stream stream, SampleType type, ReadOnlySpan<byte> name, uint pointer, uint size, bool loopFlag,
        uint loopPos, sbyte originalPitch, sbyte pitchCorrection, uint sampleRate)
    {
        uint dirOffset = _sdtaListChunk._smplSubchunk.AddSample(stream, type, pointer, size, loopFlag, loopPos);
        uint dirEnd, dirLoopEnd, dirLoopStart;
        if (loopFlag)
        {
            dirEnd = dirOffset + size + 8;
            dirLoopEnd = dirOffset + size;
            dirLoopStart = dirOffset + loopPos;
        }
        else
        {
            dirEnd = dirOffset + size;
            dirLoopEnd = 0;
            dirLoopStart = 0;
        }

        AddNewSampleHeader(name, dirOffset, dirEnd, dirLoopStart, dirLoopEnd, sampleRate, originalPitch, pitchCorrection);
    }

    /// <summary>
    /// Adds a new sample using default sample rate.
    /// </summary>
    /// <param name="stream">Source stream.</param>
    /// <param name="type">Sample type.</param>
    /// <param name="name">Sample nane.</param>
    /// <param name="pointer">Sample offset.</param>
    /// <param name="size">Size.</param>
    /// <param name="loopFlag">Loop flag.</param>
    /// <param name="loopPos">Loop position.</param>
    /// <param name="originalPitch">Original pitch.</param>
    /// <param name="pitchCorrection">Pitch correction.</param>
    public void AddNewSample(Stream stream, SampleType type, ReadOnlySpan<byte> name, uint pointer, uint size,
        bool loopFlag, uint loopPos, sbyte originalPitch, sbyte pitchCorrection)
    {
        AddNewSample(stream, type, name, pointer, size, loopFlag, loopPos, originalPitch, pitchCorrection, DefaultSampleRate);
    }

    /// <summary>
    /// Gets ibag size.
    /// </summary>
    /// <returns>Ibag size.</returns>
    public ushort GetIbagSize()
    {
        return (ushort)_hydraChunk._ibagSubchunk._bagList.Count;
    }

    /// <summary>
    /// Gets igen size.
    /// </summary>
    /// <returns>Igen size.</returns>
    public ushort GetIgenSize()
    {
        return (ushort)_hydraChunk._igenSubchunk._generatorList.Count;
    }

    /// <summary>
    /// Gets imod size.
    /// </summary>
    /// <returns>Imod size.</returns>
    public ushort GetImodSize()
    {
        return (ushort)_hydraChunk._imodSubchunk._modulatorList.Count;
    }

    /// <summary>
    /// Gets pbag size.
    /// </summary>
    /// <returns>Pbag size.</returns>
    public ushort GetPbagSize()
    {
        return (ushort)_hydraChunk._pbagSubchunk._bagList.Count;
    }

    /// <summary>
    /// Gets pgen size.
    /// </summary>
    /// <returns>Pgen size.</returns>
    public ushort GetPgenSize()
    {
        return (ushort)_hydraChunk._pgenSubchunk._generatorList.Count;
    }

    /// <summary>
    /// Gets pmod size.
    /// </summary>
    /// <returns>Pmod size.</returns>
    public ushort GetPmodSize()
    {
        return (ushort)_hydraChunk._pmodSubchunk._modulatorList.Count;
    }

    /// <inheritdoc />
    public void Dispose() => Stream.Dispose();
}
