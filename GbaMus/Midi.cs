using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace GbaMus;

/// <summary>
/// Represents a MIDI file.
/// </summary>
public class Midi
{
    /// <summary>
    /// User can change the order of the channels.
    /// </summary>
    public byte[] chanReorder;

    /// <summary>
    /// Delta time per beat.
    /// </summary>
    private readonly ushort DeltaTimePerBeat;

    private readonly short[] LastRpnType;

    private readonly short[] LastNrpnType;

    private readonly int[] LastType;

    /// <summary>
    /// Last channel, used for compression.
    /// </summary>
    private int LastChannel;

    /// <summary>
    /// Last event type, used for compression.
    /// </summary>
    private MidiEventType LastEventType;

    /// <summary>
    /// Time counter.
    /// </summary>
    private uint TimeCtr;

    /// <summary>
    /// Track data.
    /// </summary>
    private MemoryStream Data;

    /// <summary>
    /// Initializes a new instance of <summary>Midi</summary>.
    /// </summary>
    /// <param name="deltaTime">Delta time per beat.</param>
    public Midi(ushort deltaTime)
    {
        chanReorder = new byte[16];
        LastRpnType = new short[16];
        LastNrpnType = new short[16];
        LastType = new int[16];
        Data = new MemoryStream();
        DeltaTimePerBeat = deltaTime;
        for (int i = 15; i >= 0; i--)
        {
            LastRpnType[i] = -1;
            LastNrpnType[i] = -1;
            LastType[i] = -1;
            chanReorder[i] = (byte)i;
        }
        LastChannel = -1;
        TimeCtr = 0;
    }

    /// <summary>
    /// Adds delta time in MIDI variable length coding.
    /// </summary>
    /// <param name="code">Value to write.</param>
    private void AddVlengthCode(int code)
    {
        byte word1 = (byte)(code & 0x7f);
        byte word2 = (byte)((code >> 7) & 0x7f);
        byte word3 = (byte)((code >> 14) & 0x7f);
        byte word4 = (byte)((code >> 21) & 0x7f);

        if (word4 != 0)
        {
            Data.WriteByte((byte)(word4 | 0x80));
            Data.WriteByte((byte)(word3 | 0x80));
            Data.WriteByte((byte)(word2 | 0x80));
        }
        else if (word3 != 0)
        {
            Data.WriteByte((byte)(word3 | 0x80));
            Data.WriteByte((byte)(word2 | 0x80));
        }
        else if (word2 != 0)
        {
            Data.WriteByte((byte)(word2 | 0x80));
        }
        Data.WriteByte(word1);
    }

    /// <summary>
    /// Adds delta time event.
    /// </summary>
    private void AddDeltaTime()
    {
        AddVlengthCode((int)TimeCtr);
        // Reset time counter to zero.
        TimeCtr = 0;
    }

    /// <summary>
    /// Adds any MIDI event.
    /// </summary>
    /// <param name="type">Event type.</param>
    /// <param name="chan">Channel.</param>
    /// <param name="param1">Parameter 1.</param>
    private void AddEvent(MidiEventType type, int chan, byte param1)
    {
        AddDeltaTime();
        if (chan != LastChannel || type != LastEventType)
        {
            LastChannel = chan;
            LastEventType = type;
            Data.WriteByte((byte)(((int)type << 4) | chanReorder[chan]));
        }
        Data.WriteByte(param1);
    }

    /// <summary>
    /// Adds any MIDI event.
    /// </summary>
    /// <param name="type">Event type.</param>
    /// <param name="chan">Channel.</param>
    /// <param name="param1">Parameter 1.</param>
    /// <param name="param2">Parameter 2.</param>
    private void AddEvent(MidiEventType type, int chan, byte param1, byte param2)
    {
        AddDeltaTime();
        if (chan != LastChannel || type != LastEventType)
        {
            LastChannel = chan;
            LastEventType = type;
            Data.WriteByte((byte)(((int)type << 4) | chanReorder[chan]));
        }
        Data.WriteByte(param1);
        Data.WriteByte(param2);
    }

    [StructLayout(LayoutKind.Explicit, Size = 14)]
    private unsafe struct MthdChunk
    {
        private static ReadOnlySpan<byte> _head => new[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' };

        [FieldOffset(0)] private fixed byte Name[4];
        [FieldOffset(4)] private fixed byte HdrLen[4];
        [FieldOffset(8)] private fixed byte Format[2];
        [FieldOffset(10)] private fixed byte NTracks[2];
        [FieldOffset(12)] private fixed byte Division[2];

        public unsafe MthdChunk(ushort deltaTimePerBeat)
        {
            fixed (byte* b = Name)
                _head.CopyTo(new Span<byte>(b, 4));
            fixed (byte* b = HdrLen)
                BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(b, 4), 6);
            fixed (byte* b = Format)
                BinaryPrimitives.WriteUInt16BigEndian(new Span<byte>(b, 2), 0);
            fixed (byte* b = NTracks)
                BinaryPrimitives.WriteUInt16BigEndian(new Span<byte>(b, 2), 1);
            fixed (byte* b = Division)
                BinaryPrimitives.WriteUInt16BigEndian(new Span<byte>(b, 2), deltaTimePerBeat);
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private unsafe struct MtrkChunk
    {
        private static ReadOnlySpan<byte> _head => new[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' };

        [FieldOffset(0)] private fixed byte Name[4];
        [FieldOffset(4)] private fixed byte Size[4];

        public unsafe MtrkChunk(uint s)
        {
            fixed (byte* b = Name)
                _head.CopyTo(new Span<byte>(b, 4));
            fixed (byte* b = Size)
                BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(b, 4), s);
        }
    }

    /// <summary>
    /// Writes cached data to midi file.
    /// </summary>
    /// <param name="stream">Output stream.</param>
    public void Write(Stream stream)
    {
        //Add end-of-track meta event
        Data.WriteByte(0x00);
        Data.WriteByte(0xff);
        Data.WriteByte(0x2f);
        Data.WriteByte(0x00);

        //Write MIDI header
        stream.WriteStructure(new MthdChunk(DeltaTimePerBeat), 14);

        //Write MIDI track data
        //we use SMF-0 standard therefore there is only a single track :)
        uint s = (uint)Data.Length;
        stream.WriteStructure(new MtrkChunk(s), 8);

        //Write the track itself
        Data.Position = 0;
        Data.CopyTo(stream);
        Data.Position = Data.Length;
    }

    /// <summary>
    /// Increments time by one clock.
    /// </summary>
    public void Clock()
    {
        TimeCtr++;
    }

    /// <summary>
    /// Adds a note on event to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="key">Key.</param>
    /// <param name="vel">Velocity.</param>
    public void AddNoteOn(int chan, byte key, byte vel)
    {
        AddEvent(MidiEventType.NOTEON, chan, key, vel);
    }

    /// <summary>
    /// Adds a note off event to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="key">Key.</param>
    /// <param name="vel">Velocity.</param>
    public void AddNoteOff(int chan, byte key, byte vel)
    {
        AddEvent(MidiEventType.NOTEOFF, chan, key, vel);
    }

    /// <summary>
    /// Adds a controller event to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="ctrl">Control.</param>
    /// <param name="value">Value.</param>
    public void AddController(int chan, byte ctrl, byte value)
    {
        AddEvent(MidiEventType.CONTROLLER, chan, ctrl, value);
    }

    /// <summary>
    /// Adds a channel aftertouch event to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="value">Value.</param>
    public void AddChanaft(int chan, byte value)
    {
        AddEvent(MidiEventType.CHNAFT, chan, value);
    }

    /// <summary>
    /// Adds a PChange event to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="number">Number.</param>
    public void AddPChange(int chan, byte number)
    {
        AddEvent(MidiEventType.PCHANGE, chan, number);
    }

    /// <summary>
    /// Adds an RPN event to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="value">Value.</param>
    public void AddPitchBend(int chan, short value)
    {
        byte lo = (byte)(value & 0x7f);
        byte hi = (byte)((value >> 7) & 0x7f);
        AddEvent(MidiEventType.PITCHBEND, chan, lo, hi);
    }

    /// <summary>
    /// Adds a pitch bend event with only the MSB used to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="value">Value.</param>
    public void AddPitchBend(int chan, byte value)
    {
        AddEvent(MidiEventType.PITCHBEND, chan, 0, value);
    }

    /// <summary>
    /// Adds an RPN event to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="type">Type.</param>
    /// <param name="value">Value.</param>
    public void AddRpn(int chan, short type, short value)
    {
        if (LastRpnType[chan] != type || LastType[chan] != 0)
        {
            LastRpnType[chan] = type;
            LastType[chan] = 0;
            AddEvent(MidiEventType.CONTROLLER, chan, 101, (byte)(type >> 7));
            AddEvent(MidiEventType.CONTROLLER, chan, 100, (byte)(type & 0x7f));
        }
        AddEvent(MidiEventType.CONTROLLER, chan, 6, (byte)(value >> 7));
        if ((value & 0x7f) != 0)
            AddEvent(MidiEventType.CONTROLLER, chan, 38, (byte)(value & 0x7f));
    }

    /// <summary>
    /// Adds an RPN event with only the MSB of <paramref name="value"/> used to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="type">Type.</param>
    /// <param name="value">Value.</param>
    public void AddRpn(int chan, short type, byte value)
    {
        AddRpn(chan, type, (short)(value << 7));
    }

    /// <summary>
    /// Adds an NRPN event to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="type">Type.</param>
    /// <param name="value">Value.</param>
    public void AddNrpn(int chan, short type, short value)
    {
        if (LastNrpnType[chan] != type || LastType[chan] != 1)
        {
            LastNrpnType[chan] = type;
            LastType[chan] = 1;
            AddEvent(MidiEventType.CONTROLLER, chan, 99, (byte)(type >> 7));
            AddEvent(MidiEventType.CONTROLLER, chan, 98, (byte)(type & 0x7f));
        }
        AddEvent(MidiEventType.CONTROLLER, chan, 6, (byte)(value >> 7));
        if ((value & 0x7f) != 0)
            AddEvent(MidiEventType.CONTROLLER, chan, 38, (byte)(value & 0x7f));
    }

    //Add NRPN event with only the MSB of value used
    /// <summary>
    /// Adds an NRPN event with only the MSB of <paramref name="value"/> used to the stream.
    /// </summary>
    /// <param name="chan">Channel.</param>
    /// <param name="type">Type.</param>
    /// <param name="value">Value.</param>
    public void AddNrpn(int chan, short type, byte value)
    {
        AddNrpn(chan, type, (short)(value << 7));
    }

    /// <summary>
    /// Adds a marker event.
    /// </summary>
    /// <param name="text">Content.</param>
    public void AddMarker(ReadOnlySpan<byte> text)
    {
        AddDeltaTime();
        Data.WriteByte(0xFF);
        //Add text meta event if marker is false, marker meta even if true
        Data.WriteByte(6);
        int len = text.Length;
        AddVlengthCode(len);
        //Add text itself
        byte[] buf = ArrayPool<byte>.Shared.Rent(text.Length);
        try
        {
            text.CopyTo(buf);
            Data.Write(buf, 0, text.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    /// <summary>
    /// Adds a sysex event.
    /// </summary>
    /// <param name="sysexData">Data.</param>
    public void AddSysex(ReadOnlySpan<byte> sysexData)
    {
        AddDeltaTime();
        Data.WriteByte(0xf0);
        int len = sysexData.Length;
        //Actually variable length code
        AddVlengthCode(len + 1);

        byte[] buf = ArrayPool<byte>.Shared.Rent(len);
        try
        {
            sysexData.CopyTo(buf);
            Data.Write(buf, 0, len);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
        Data.WriteByte(0xf7);
    }

    /// <summary>
    /// Adds a tempo event.
    /// </summary>
    /// <param name="tempo">Tempo.</param>
    public void AddTempo(double tempo)
    {
        int t = (int)(60000000.0 / tempo);
        byte t1 = (byte)t;
        byte t2 = (byte)(t >> 8);
        byte t3 = (byte)(t >> 16);

        AddDeltaTime();
        Data.WriteByte(0xff);
        Data.WriteByte(0x51);
        Data.WriteByte(0x03);
        Data.WriteByte(t3);
        Data.WriteByte(t2);
        Data.WriteByte(t1);
    }
}
