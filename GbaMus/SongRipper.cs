using System.Text;

namespace GbaMus;

/// <summary>
/// Provides song_ripper functionality.
/// </summary>
public class SongRipper
{
    private class Note
    {
        private readonly SongRipper _songRipper;
        private readonly Midi _midi;
        private int _counter;
        private readonly int _key;
        private readonly int _vel;
        private readonly int _chn;
        private bool _eventMade;

        // Tick counter, if it becomes zero
        // then create key off event
        // this function returns "true" when the note should be freed from memory
        private bool Tick()
        {
            if (_counter <= 0 || --_counter != 0) return false;
            _midi.AddNoteOff(_chn, (byte)_key, (byte)_vel);
            _songRipper.StopLfo(_chn);
            _songRipper._simultaneousNotesCtr--;
            return true;
        }

        // Create note and key on event
        public Note(SongRipper songRipper, Midi midi, int chn, int len, int key, int vel)
        {
            _songRipper = songRipper;
            _midi = midi;
            _chn = chn;
            _counter = len;
            _key = key;
            _vel = vel;
            _eventMade = false;

            songRipper.StartLfo(chn);
            songRipper.AddSimultaneousNote();
        }


        internal bool CountdownIsOver()
        {
            return Tick() || _counter < 0;
        }

        internal void MakeNoteOnEvent()
        {
            if (_eventMade) return;
            _midi.AddNoteOn(_chn, (byte)_key, (byte)_vel);
            _eventMade = true;
        }
    }

    private readonly uint[] _trackPtr = new uint[16];
    private readonly byte[] _lastCmd = new byte[16];
    private readonly byte[] _lastKey = new byte[16];
    private readonly byte[] _lastVel = new byte[16];
    private readonly int[] _counter = new int[16];
    private readonly uint[] _returnPtr = new uint[16];
    private readonly int[] _keyShift = new int[16];
    private readonly bool[] _returnFlag = new bool[16];
    private readonly bool[] _trackCompleted = new bool[16];
    private bool _endFlag;
    private bool _loopFlag;
    private uint _loopAdr;

    private readonly int[] _lfoDelayCtr = new int[16];
    private readonly int[] _lfoDelay = new int[16];
    private readonly int[] _lfoDepth = new int[16];
    private readonly int[] _lfoType = new int[16];
    private readonly bool[] _lfoFlag = new bool[16];
    private readonly bool[] _lfoHack = new bool[16];

    private uint _simultaneousNotesCtr;
    private uint _simultaneousNotesMax;

    private readonly List<Note> _notesPlaying;

    /// <summary>
    /// Number of tracks in this song.
    /// </summary>
    public int TrackCount { get; private set; }

    private Settings _settings;
    private bool _processed;

    private Midi _midi;
    private readonly Stream _inGba;

    private static void PrintInstructions(TextWriter? textWriter)
    {
        textWriter?.WriteLine("Rips sequence data from a GBA game using Sappy sound engine to MIDI (.mid) format.");
        textWriter?.WriteLine();
        textWriter?.WriteLine("Usage: song_riper infile.gba outfile.mid song_address [-b1 -gm -gs -xg]");
        textWriter?.WriteLine("-b : Bank: forces all patches to be in the specified bank (0-127).");
        textWriter?.WriteLine("In General MIDI, channel 10 is reserved for drums.");
        textWriter?.WriteLine("Unfortunately, we do not want to use any \"drums\" in the output file.");
        textWriter?.WriteLine("I have 3 modes to fix this problem.");
        textWriter?.WriteLine("-rc : Rearrange Channels. This will avoid using the channel 10, and use it at last ressort only if all 16 channels should be used");
        textWriter?.WriteLine("-gs : This will send a GS system exclusive message to tell the player channel 10 is not drums.");
        textWriter?.WriteLine("-xg : This will send a XG system exclusive message, and force banks number which will disable \"drums\".");
        textWriter?.WriteLine("-lv : Linearise volume and velocities. This should be used to have the output \"sound\" like the original song, but shouldn't be used to get an exact dump of sequence data.");
        textWriter?.WriteLine("-sv : Simulate vibrato. This will insert controllers in real time to simulate a vibrato, instead of just when commands are given. Like -lv, this should be used to have the output \"sound\" like the original song, but shouldn't be used to get an exact dump of sequence data.");
        textWriter?.WriteLine();
        textWriter?.WriteLine("It is possible, but not recommended, to use more than one of these flags at a time.");
    }


    private void AddSimultaneousNote()
    {
        // Update simultaneous notes max.
        if (++_simultaneousNotesCtr > _simultaneousNotesMax)
            _simultaneousNotesMax = _simultaneousNotesCtr;
    }

    // LFO logic on tick
    private void ProcessLfo(int track)
    {
        if (_settings.Sv && _lfoDelayCtr[track] != 0)
        {
            // Decrease counter if it's value was nonzero
            if (--_lfoDelayCtr[track] == 0)
            {
                // If 1->0 transition we need to add a signal to start the LFO
                if (_lfoType[track] == 0)
                    // Send a controller 1 if pitch LFO
                    _midi.AddController(track, 1, (byte)(_lfoDepth[track] < 16 ? _lfoDepth[track] * 8 : 127));
                else
                    // Send a channel aftertouch otherwise
                    _midi.AddChanaft(track, (byte)(_lfoDepth[track] < 16 ? _lfoDepth[track] * 8 : 127));
                _lfoFlag[track] = true;
            }
        }
    }

    private void StartLfo(int track)
    {
        // Reset down delay counter to its initial value
        if (_settings.Sv && _lfoDelay[track] != 0)
            _lfoDelayCtr[track] = _lfoDelay[track];
    }

    private void StopLfo(int track)
    {
        // Cancel a LFO if it was playing,
        if (_settings.Sv && _lfoFlag[track])
        {
            if (_lfoType[track] == 0)
                _midi.AddController(track, 1, 0);
            else
                _midi.AddChanaft(track, 0);
            _lfoFlag[track] = false;
        }
        else
            _lfoDelayCtr[track] = 0; // cancel delay counter if it wasn't playing
    }

    private bool Tick(int trackAmnt)
    {
        // Tick all playing notes, and remove notes which
        // have been keyed off OR which are infinite length from the list
        _notesPlaying.RemoveAll(v => v.CountdownIsOver());

        // Process all tracks
        for (int track = 0; track < trackAmnt; track++)
        {
            _counter[track]--;
            // Process events until counter non-null or pointer null
            // This might not be executed if counter both are non null.
            while (_trackPtr[track] != 0 && !_endFlag && _counter[track] <= 0)
            {
                // Check if we're at loop start point
                if (track == 0 && _loopFlag && !_returnFlag[0] && !_trackCompleted[0] && _trackPtr[0] == _loopAdr)
                    _midi.AddMarker(Encoding.ASCII.GetBytes("loopStart"));

                ProcessEvent(track);
            }
        }

        for (int track = 0; track < trackAmnt; track++)
        {
            ProcessLfo(track);
        }

        // Compute if all still active channels are completely decoded
        bool allCompletedFlag = true;
        for (int i = 0; i < trackAmnt; i++)
            allCompletedFlag &= _trackCompleted[i];

        // If everything is completed, the main program should quit its loop
        if (allCompletedFlag) return false;

        // Make note on events for this tick
        //(it's important they are made after all other events)
        foreach (var p in _notesPlaying)
            p.MakeNoteOnEvent();

        // Increment MIDI time
        _midi.Clock();
        return true;
    }

    // Length table for notes and rests
    private static readonly int[] s_lenTbl = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 28, 30, 32, 36, 40, 42, 44, 48, 52, 54, 56, 60, 64, 66, 68, 72, 76, 78, 80, 84, 88, 90, 92, 96 };

    private void ProcessEvent(int track)
    {
        _inGba.Position = _trackPtr[track];
        // Read command
        byte command = _inGba.ReadUInt8LittleEndian();

        _trackPtr[track]++;
        byte arg1;
        // Repeat last command, the byte read was in fact the first argument
        if (command < 0x80)
        {
            arg1 = command;
            command = _lastCmd[track];
        }

        // Delta time command
        else if (command <= 0xb0)
        {
            _counter[track] = s_lenTbl[command - 0x80];
            return;
        }

        // End track command
        else if (command == 0xb1)
        {
            // Null pointer
            _trackPtr[track] = 0;
            _trackCompleted[track] = true;
            return;
        }

        // Jump command
        else if (command == 0xb2)
        {
            _trackPtr[track] = _inGba.GetGbaPointer();

            // detect the end track
            _trackCompleted[track] = true;
            return;
        }

        // Call command
        else if (command == 0xb3)
        {
            uint addr = _inGba.GetGbaPointer();

            // Return address for the track
            _returnPtr[track] = _trackPtr[track] + 4;
            // Now points to called address
            _trackPtr[track] = addr;
            _returnFlag[track] = true;
            return;
        }

        // Return command
        else if (command == 0xb4)
        {
            if (_returnFlag[track])
            {
                _trackPtr[track] = _returnPtr[track];
                _returnFlag[track] = false;
            }
            return;
        }

        // Tempo change
        else if (command == 0xbb)
        {
            int tempo = 2 * _inGba.ReadUInt8LittleEndian();
            _trackPtr[track]++;
            _midi.AddTempo(tempo);
            return;
        }

        else
        {
            // Normal command
            _lastCmd[track] = command;
            // Need argument
            arg1 = _inGba.ReadUInt8LittleEndian();
            _trackPtr[track]++;
        }

        // Note on with specified length command
        if (command >= 0xd0)
        {
            int key, vel, lenOfs = 0;
            // Is arg1 a key value ?
            if (arg1 < 0x80)
            {
                // Yes -> use new key value
                key = arg1;
                _lastKey[track] = (byte)key;

                byte arg2 = _inGba.ReadUInt8LittleEndian();
                // Is arg2 a velocity ?
                byte arg3 = (_inGba.ReadUInt8LittleEndian());
                if (arg2 < 0x80)
                {
                    // Yes -> use new velocity value
                    vel = arg2;
                    _lastVel[track] = (byte)vel;
                    _trackPtr[track]++;

                    // Is there a length offset ?
                    if (arg3 < 0x80)
                    {
                        // Yes -> read it and increment pointer
                        lenOfs = arg3;
                        _trackPtr[track]++;
                    }
                }
                else
                {
                    // No -> use previous velocity value
                    vel = _lastVel[track];
                }
            }
            else
            {
                // No -> use last value
                key = _lastKey[track];
                vel = _lastVel[track];
                _trackPtr[track]--; // Seek back, as arg 1 is unused and belong to next event !
            }

            // Linearise velocity if needed
            if (_settings.Lv) vel = (int)Math.Sqrt(127.0 * vel);

            _notesPlaying.Insert(0, new Note(this, _midi, track, s_lenTbl[command - 0xd0 + 1] + lenOfs, key + _keyShift[track], vel));
            return;
        }

        // Other commands
        switch (command)
        {
            // Key shift
            case 0xbc:
                _keyShift[track] = arg1;
                return;

            // Set instrument
            case 0xbd:
                if (_settings.BankNumber is { } bn)
                {
                    if (!_settings.Xg)
                        _midi.AddController(track, 0, (byte)bn);
                    else
                    {
                        _midi.AddController(track, 0, (byte)(bn >> 7));
                        _midi.AddController(track, 32, (byte)(bn & 0x7f));
                    }
                }
                _midi.AddPChange(track, arg1);
                return;

            // Set volume
            case 0xbe:
                {
                    // Linearise volume if needed
                    int volume = _settings.Lv ? (int)Math.Sqrt(127.0 * arg1) : arg1;
                    _midi.AddController(track, 7, (byte)volume);
                }
                return;

            // Set panning
            case 0xbf:
                _midi.AddController(track, 10, arg1);
                return;

            // Pitch bend
            case 0xc0:
                _midi.AddPitchBend(track, arg1);
                return;

            // Pitch bend range
            case 0xc1:
                if (_settings.Sv)
                    _midi.AddRpn(track, 0, arg1);
                else
                    _midi.AddController(track, 20, arg1);
                return;

            // LFO Speed
            case 0xc2:
                if (_settings.Sv)
                    _midi.AddNrpn(track, 136, arg1);
                else
                    _midi.AddController(track, 21, arg1);
                return;

            // LFO delay
            case 0xc3:
                if (_settings.Sv)
                    _lfoDelay[track] = arg1;
                else
                    _midi.AddController(track, 26, arg1);
                return;

            // LFO depth
            case 0xc4:
                if (_settings.Sv)
                {
                    if (_lfoDelay[track] == 0 && _lfoHack[track])
                    {
                        if (_lfoType[track] == 0)
                            _midi.AddController(track, 1, (byte)(arg1 > 12 ? 127 : 10 * arg1));
                        else
                            _midi.AddChanaft(track, (byte)(arg1 > 12 ? 127 : 10 * arg1));

                        _lfoFlag[track] = true;
                    }
                    _lfoDepth[track] = arg1;
                    // I had a stupid bug with LFO inserting controllers I didn't want at the start of files
                    // So I made a terrible quick fix for it, in the mean time I can find something better to prevent it.
                    _lfoHack[track] = true;
                }
                else
                    _midi.AddController(track, 1, arg1);
                return;

            // LFO type
            case 0xc5:
                if (_settings.Sv)
                    _lfoType[track] = arg1;
                else
                    _midi.AddController(track, 22, arg1);
                return;

            // Detune
            case 0xc8:
                if (_settings.Sv)
                    _midi.AddRpn(track, 1, arg1);
                else
                    _midi.AddController(track, 24, arg1);
                return;

            // Key off
            case 0xce:
                {
                    int key, vel = 0;

                    // Is arg1 a key value ?
                    if (arg1 < 0x80)
                    {
                        // Yes -> use new key value
                        key = arg1;
                        _lastKey[track] = (byte)key;
                    }
                    else
                    {
                        // No -> use last value
                        key = _lastKey[track];
                        vel = _lastVel[track];
                        _trackPtr[track]--; // Seek back, as arg 1 is unused and belong to next event !
                    }

                    _midi.AddNoteOff(track, (byte)(key + _keyShift[track]), (byte)vel);
                    StopLfo(track);
                    _simultaneousNotesCtr--;
                }
                return;

            // Key on
            case 0xcf:
                {
                    int key, vel;
                    // Is arg1 a key value ?
                    if (arg1 < 0x80)
                    {
                        // Yes -> use new key value
                        key = arg1;
                        _lastKey[track] = (byte)key;

                        byte arg2 = _inGba.ReadUInt8LittleEndian();
                        // Is arg2 a velocity ?
                        if (arg2 < 0x80)
                        {
                            // Yes -> use new velocity value
                            vel = arg2;
                            _lastVel[track] = (byte)vel;
                            _trackPtr[track]++;
                        }
                        else // No -> use previous velocity value
                            vel = _lastVel[track];
                    }
                    else
                    {
                        // No -> use last value
                        key = _lastKey[track];
                        vel = _lastVel[track];
                        _trackPtr[track]--; // Seek back, as arg 1 is unused and belong to next event !
                    }
                    // Linearise velocity if needed
                    if (_settings.Lv) vel = (int)Math.Sqrt(127.0 * vel);

                    // Make note of infinite length
                    _notesPlaying.Insert(0, new Note(this, _midi, track, -1, key + _keyShift[track], vel));
                }
                return;
        }
    }

    private static void ParseArguments(string[] args, ref Settings settings, out string gbaFile)
    {
        bool rc = false, gs = false, xg = false, lv = false, sv = false;
        int bankNumber = 0;
        bool bankUsed = false;
        if (args.Length < 3)
        {
            PrintInstructions(settings.Debug);
            throw new EnvironmentExitException(0);
        }

        gbaFile = args[0];

        for (int i = 3; i < args.Length; i++)
        {
            if (args[i][0] == '-')
            {
                switch (args[i][1])
                {
                    case 'b':
                        {
                            if (args[i].Length < 3)
                            {
                                PrintInstructions(settings.Debug);
                                throw new EnvironmentExitException(0);
                            }
                            bankNumber = int.Parse(args[i].Substring(2));
                            bankUsed = true;
                            break;
                        }
                    case 'r' when args[i][2] == 'c':
                        rc = true;
                        break;
                    case 'g' when args[i][2] == 's':
                        gs = true;
                        break;
                    case 'x' when args[i][2] == 'g':
                        xg = true;
                        break;
                    case 'l' when args[i][2] == 'v':
                        lv = true;
                        break;
                    case 's' when args[i][2] == 'v':
                        sv = true;
                        break;
                    default:
                        PrintInstructions(settings.Debug);
                        throw new EnvironmentExitException(0);
                }
            }
            else
            {
                PrintInstructions(settings.Debug);
                throw new EnvironmentExitException(0);
            }
        }
        // Return base address, parsed correctly in both decimal and hex
        if (!InternalUtils.TryParseUIntHd(args[2], out uint baseAddress))
            throw new ArgumentException("Failed to parse base address");
        settings = settings with
        {
            Rc = rc,
            Gs = gs,
            Xg = xg,
            Lv = lv,
            Sv = sv,
            BankNumber = bankUsed ? bankNumber : null,
            BaseAddress = baseAddress
        };
    }


    // Part 10 to normal
    private static readonly byte[] s_gsResetSysex = { 0x41, 0x10, 0x42, 0x12, 0x40, 0x00, 0x7f, 0x00, 0x41 };
    private static readonly byte[] s_part10NormalSysex = { 0x41, 0x10, 0x42, 0x12, 0x40, 0x10, 0x15, 0x00, 0x1b };
    private static readonly byte[] s_xgSysex = { 0x43, 0x10, 0x4C, 0x00, 0x00, 0x7E, 0x00 };

    /// <summary>
    /// Main execution function for song ripper.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>Something.</returns>
    public static int Main(string[] args)
    {
        return MainInternal(args, new Settings(Console.Out, Console.Error));
    }

    private static int MainInternal(string[] args, Settings settings)
    {
        settings.Debug?.WriteLine("GBA ROM sequence ripper (c) 2012 Bregalad");
        string gbaFile;
        try
        {
            ParseArguments(args, ref settings, out gbaFile);
        }
        catch (ArgumentException e)
        {
            Console.WriteLine(e.Message);
            return -1;
        }
        catch (EnvironmentExitException e)
        {
            return e.Code;
        }

        // Open the input and output files
        Stream inGba;
        try
        {
            inGba = File.OpenRead(gbaFile);
        }
        catch
        {
            settings.Error?.WriteLine($"Can't open file {gbaFile} for reading.");
            throw new EnvironmentExitException(0);
        }

        SongRipper sr;
        try
        {
            sr = new SongRipper(inGba, settings);
        }
        catch (InvalidDataException e)
        {
            settings.Debug?.WriteLine(e.Message);
            throw new EnvironmentExitException(0);
        }
        catch (ArgumentException e)
        {
            settings.Debug?.WriteLine(e.Message);
            throw new EnvironmentExitException(0);
        }

        // Open output file once we know the pointer points to correct data
        //(this avoids creating blank files when there is an error)
        Stream outMid;
        try
        {
            outMid = File.Create(args[1]);
        }
        catch
        {
            settings.Error?.WriteLine($"Can't write to file {args[1]}.");
            throw new EnvironmentExitException(0);
        }

        int instrBankAddress = sr.Write(outMid);

        // Close files
        inGba.Dispose();
        outMid.Dispose();
        settings.Debug?.WriteLine(" Done!");
        return instrBankAddress;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SongRipper"/> for the specified input file and settings.
    /// </summary>
    /// <param name="stream">Input stream.</param>
    /// <param name="settings">Settings.</param>
    /// <exception cref="ArgumentException">Thrown for bad arguments.</exception>
    /// <exception cref="InvalidDataException">Thrown for invalid properties.</exception>
    public SongRipper(Stream stream, Settings settings)
    {
        _settings = settings;
        _inGba = stream;
        _notesPlaying = new List<Note>();
        _midi = new Midi(24);
        if (_inGba.Length < settings.BaseAddress)
            throw new ArgumentException($"Can't seek to the base address 0x{settings.BaseAddress:x}.");
        _inGba.Position = settings.BaseAddress;
        int trackAmnt = _inGba.ReadUInt8LittleEndian();
        if (trackAmnt is < 1 or > 16)
            throw new InvalidDataException($"Invalid amount of tracks {trackAmnt}! (must be 1-16).");
        settings.Debug?.WriteLine($"{trackAmnt} tracks.");
        TrackCount = trackAmnt;
    }

    private void ChangeSettings(Settings settings)
    {
        if (_inGba.Length < settings.BaseAddress)
            throw new ArgumentException($"Can't seek to the base address 0x{settings.BaseAddress:x}.");
        _inGba.Position = settings.BaseAddress;
        int trackAmnt = _inGba.ReadUInt8LittleEndian();
        if (trackAmnt is < 1 or > 16)
            throw new InvalidDataException($"Invalid amount of tracks {trackAmnt}! (must be 1-16).");
        settings.Debug?.WriteLine($"{trackAmnt} tracks.");
        TrackCount = trackAmnt;
        _settings = settings;
    }

    private void Reset()
    {
        if (!_processed)
        {
            _processed = true;
            return;
        }
        _midi = new Midi(24);
        _trackPtr.AsSpan().Clear();
        _lastCmd.AsSpan().Clear();
        _lastKey.AsSpan().Clear();
        _counter.AsSpan().Clear();
        _returnPtr.AsSpan().Clear();
        _keyShift.AsSpan().Clear();
        _returnFlag.AsSpan().Clear();
        _trackCompleted.AsSpan().Clear();
        _endFlag = false;
        _loopFlag = false;
        _lfoDelayCtr.AsSpan().Clear();
        _lfoDelay.AsSpan().Clear();
        _lfoDepth.AsSpan().Clear();
        _lfoType.AsSpan().Clear();
        _lfoFlag.AsSpan().Clear();
        _lfoHack.AsSpan().Clear();
        _simultaneousNotesCtr = 0;
        _simultaneousNotesMax = 0;
        _notesPlaying.Clear();
    }

    /// <summary>
    /// Write converted MIDI to stream with the specified settings.
    /// </summary>
    /// <param name="stream">Stream to write to.</param>
    /// <param name="settings">Settings to configure.</param>
    /// <returns>Instrument bank address.</returns>
    /// <exception cref="ArgumentException">Thrown for bad arguments.</exception>
    /// <exception cref="InvalidDataException">Thrown for invalid state resulting from provided settings.</exception>
    public int Write(Stream stream, Settings settings)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        ChangeSettings(settings);
        return Write(stream);
    }

    /// <summary>
    /// Write converted MIDI to stream.
    /// </summary>
    /// <param name="stream">Stream to write to.</param>
    /// <returns>Instrument bank address.</returns>
    public int Write(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        Reset();
        _settings.Debug?.WriteLine("Converting...");
        if (_settings.Rc)
        {
            // Make the drum channel last in the list, hopefully reducing the risk of it being used
            _midi.ChanReorder[9] = 15;
            for (uint j = 10; j < 16; ++j)
                _midi.ChanReorder[j] = (byte)(j - 1);
        }

        if (_settings.Gs)
        {
            // GS reset
            _midi.AddSysex(s_gsResetSysex);
            _midi.AddSysex(s_part10NormalSysex);
        }

        if (_settings.Xg)
        {
            // XG reset
            _midi.AddSysex(s_xgSysex);
        }

        _midi.AddMarker(Encoding.ASCII.GetBytes("Converted by SequenceRipper 2.0"));

        _inGba.Position = _settings.BaseAddress + 1;
        _inGba.ReadUInt8LittleEndian(); // Unknown byte
        _inGba.ReadUInt8LittleEndian(); // Priority
        sbyte reverb = _inGba.ReadInt8LittleEndian(); // Reverb

        int instrBankAddress = (int)_inGba.GetGbaPointer();

        // Read table of pointers
        for (int i = 0; i < TrackCount; i++)
        {
            _trackPtr[i] = _inGba.GetGbaPointer();

            _lfoDepth[i] = 0;
            _lfoDelay[i] = 0;
            _lfoFlag[i] = false;

            if (reverb < 0) // add reverb controller on all tracks
                _midi.AddController(i, 91, (byte)(_settings.Lv ? (int)Math.Sqrt((reverb & 0x7f) * 127.0) : reverb & 0x7f));
        }

        // Search for loop address of track #0
        if (TrackCount > 1) // If 2 or more track, end of track is before start of track 2
            _inGba.Position = _trackPtr[1] - 9;
        else
            // If only a single track, the end is before start of header data
            _inGba.Position = _settings.BaseAddress - 9;

        // Read where in track 1 the loop starts
        for (int i = 0; i < 5; i++)
            if (_inGba.ReadUInt8LittleEndian() == 0xb2)
            {
                _loopFlag = true;
                _loopAdr = _inGba.GetGbaPointer();
                break;
            }

        // This is the main loop which will process all channels
        // until they are all inactive
        int x = 100000;
        while (Tick(TrackCount))
        {
            if (x-- == 0)
            {
                // Security thing to avoid infinite loop in case things goes wrong
                _settings.Debug?.Write("Time out!");
                break;
            }
        }

        // If a loop was detected this is its end
        if (_loopFlag) _midi.AddMarker(Encoding.ASCII.GetBytes("loopEnd"));

        _settings.Debug?.WriteLine($" Maximum simultaneous notes: {_simultaneousNotesMax}");

        _settings.Debug?.Write("Dump complete. Now outputting MIDI file...");
        _midi.Write(stream);
        return instrBankAddress;
    }

    /// <summary>
    /// Settings for song ripper.
    /// </summary>
    /// <param name="Debug">Debug output.</param>
    /// <param name="Error">Error output.</param>
    /// <param name="Rc">This will avoid using the channel 10, and use it at last resort only if all 16 channels should be used.</param>
    /// <param name="Gs">This will send a GS system exclusive message to tell the player channel 10 is not drums.</param>
    /// <param name="Xg">This will send a XG system exclusive message, and force banks number which will disable "drums".</param>
    /// <param name="Lv">Linearize volume and velocities. This should be used to have the output \"sound\" like the original song, but shouldn't be used to get an exact dump of sequence data.</param>
    /// <param name="Sv">Simulate vibrato. This will insert controllers in real time to simulate a vibrato, instead of just when commands are given. Like -lv, this should be used to have the output \"sound\" like the original song, but shouldn't be used to get an exact dump of sequence data.</param>
    /// <param name="BankNumber">Forces all patches to be in the specified bank (0-127).</param>
    /// <param name="BaseAddress">Base address of song.</param>
    public record Settings(TextWriter? Debug = null, TextWriter? Error = null,
            bool Rc = false, bool Gs = false, bool Xg = false, bool Lv = false, bool Sv = false,
            int? BankNumber = null, uint BaseAddress = 0)
        : ToolSettings(Debug, Error);
}
