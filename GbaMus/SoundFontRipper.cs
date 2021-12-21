using System.Text;

namespace GbaMus;

/// <summary>
/// Provides sound_font_ripper functionality.
/// </summary>
public class SoundFontRipper
{
    private TextWriter? _tw;

    private Stream _outSf2;
    private MemoryStream _inGba;

    private bool _verboseFlag;
    private bool _verboseOutputToFile;
    //private bool change_sample_rate;
    private bool _gmPresetNames;

    private uint _sampleRate = 22050;
    private HashSet<uint> _addresses;
    private uint _currentAddress;
    private uint _currentBank;
    private uint _currentInstrument;
    private uint _mainVolume = 15;

    private Sf2 _sf2;
    private GbaInstr _instruments;


    private SoundFontRipper()
    {
        _addresses = new HashSet<uint>();
        _sf2 = null!;
        _instruments = null!;
        _outSf2 = null!;
        _inGba = null!;
    }

    private static void PrintInstructions()
    {
        Console.WriteLine("Dumps a sound bank (or a list of sound banks) from a GBA game which is using the Sappy sound engine to SoundFont 2.0 (.sf2) format.");
        Console.WriteLine("Usage: sound_font_riper [options] in.gba out.sf2 address1 [address2] ...");
        Console.WriteLine("addresses will correspond to instrument banks in increasing order...");
        Console.WriteLine("Available options :");
        Console.WriteLine("-v  : Verbose; display info about the sound font in text format. If -v is followed by a file name, info is output to the specified file instead.");
        Console.WriteLine("-s  : Sampling rate for samples. Default: 22050 Hz");
        Console.WriteLine("-gm : Give General MIDI names to presets. Note that this will only change the names and will NOT magically turn the soundfont into a General MIDI compliant soundfont.");
        Console.WriteLine("-mv : Main volume for sample instruments. Range: 1-15. Game Boy channels are unnaffected.");
    }


    // General MIDI instrument names
    private static string[] s_generalMidiInstrNames = { "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano", "Rhodes Piano", "Chorused Piano", "Harpsichord", "Clavinet", "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer", "Hammond Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion", "Acoustic Guitar (nylon)", "Acoustic Guitar (steel)", "Electric Guitar (jazz)", "Electric Guitar (clean)", "Electric Guitar (muted)", "Overdriven Guitar", "Distortion Guitar", "Guitar Harmonics", "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2", "Violin", "Viola", "Cello", "Contrabass", "Tremelo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani", "String Ensemble 1", "String Ensemble 2", "SynthStrings 1", "SynthStrings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit", "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "Synth Brass 1", "Synth Brass 2", "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet", "Piccolo", "Flute", "Recorder", "Pan Flute", "Bottle Blow", "Shakuhachi", "Whistle", "Ocarina", "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope lead)", "Lead 4 (chiff lead)", "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass + lead)", "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)", "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)", "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)", "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)", "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bagpipe", "Fiddle", "Shanai", "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal", "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot" };

    // Add initial attenuation preset to balance between GameBoy and sampled instruments
    private void AddAttenuationPreset()
    {
        if (_mainVolume < 15)
        {
            ushort attenuation = (ushort)(100.0 * Math.Log(15.0 / _mainVolume));
            _sf2.AddNewPresetGenerator(SfGenerator.InitialAttenuation, attenuation);
        }
    }

    // Convert a GBA instrument in its SF2 counterpart
    // if any kind of error happens, it will do nothing and exit
    private void BuildInstrument(InstData inst)
    {
        byte instrType = (byte)(inst.Word0 & 0xff);
        string name;
        if (_gmPresetNames)
            name = s_generalMidiInstrNames[_currentInstrument];
        else
            // (poetic) name of the SF2 preset...
            name = $"Type {instrType} @0x{_currentAddress:x}";

        try
        {
            switch (instrType)
            {
                // Sampled instrument types
                case 0x00:
                case 0x08:
                case 0x10:
                case 0x18:
                case 0x20:
                case 0x28:
                case 0x30:
                case 0x38:
                    {
                        // riina: adjusted to match behaviour on original (that just errors out on hex 0)
                        uint samplePointer = inst.Word1 & 0x3ffffff;
                        if (samplePointer == 0) break;
                        int i = _instruments.BuildSampledInstrument(_inGba, inst);
                        _sf2.AddNewPreset(Encoding.ASCII.GetBytes(name), (ushort)_currentInstrument, (ushort)_currentBank);
                        _sf2.AddNewPresetBag();
                        // Add initial attenuation preset to balance volume between sampled and GB instruments
                        AddAttenuationPreset();
                        _sf2.AddNewPresetGenerator(SfGenerator.Instrument, (ushort)i);
                    }
                    break;

                // GameBoy pulse wave instruments
                case 0x01:
                case 0x02:
                case 0x09:
                case 0x0a:
                    {
                        // Can only convert them if the psg_data file is found
                        int i = _instruments.BuildPulseInstrument(inst);
                        _sf2.AddNewPreset(Encoding.ASCII.GetBytes(name), (ushort)_currentInstrument, (ushort)_currentBank);
                        _sf2.AddNewPresetBag();
                        _sf2.AddNewPresetGenerator(SfGenerator.Instrument, (ushort)i);
                    }
                    break;

                // GameBoy channel 3 instrument
                case 0x03:
                case 0x0b:
                    {
                        int i = _instruments.BuildGb3Instrument(_inGba, inst);
                        _sf2.AddNewPreset(Encoding.ASCII.GetBytes(name), (ushort)_currentInstrument, (ushort)_currentBank);
                        _sf2.AddNewPresetBag();
                        _sf2.AddNewPresetGenerator(SfGenerator.Instrument, (ushort)i);
                    }
                    break;

                // GameBoy noise instruments, not supported yet
                case 0x04:
                case 0x0c:
                    {
                        int i = _instruments.BuildNoiseInstrument(inst);
                        _sf2.AddNewPreset(Encoding.ASCII.GetBytes(name), (ushort)_currentInstrument, (ushort)_currentBank);
                        _sf2.AddNewPresetBag();
                        _sf2.AddNewPresetGenerator(SfGenerator.Instrument, (ushort)i);
                    }
                    break;

                // Key split instrument
                case 0x40:
                    {
                        int i = _instruments.BuildKeysplitInstrument(_inGba, inst);
                        _sf2.AddNewPreset(Encoding.ASCII.GetBytes(name), (ushort)_currentInstrument, (ushort)_currentBank);
                        _sf2.AddNewPresetBag();
                        // Add initial attenuation preset to balance volume between sampled and GB instruments
                        AddAttenuationPreset();
                        _sf2.AddNewPresetGenerator(SfGenerator.Instrument, (ushort)i);
                    }
                    break;

                // Every key split instrument
                case 0x80:
                    {
                        int i = _instruments.BuildEveryKeysplitInstrument(_inGba, inst);
                        _sf2.AddNewPreset(Encoding.ASCII.GetBytes(name), (ushort)_currentInstrument, (ushort)_currentBank);
                        _sf2.AddNewPresetBag();
                        // Add initial attenuation preset to balance volume between sampled and GB instruments
                        AddAttenuationPreset();
                        _sf2.AddNewPresetGenerator(SfGenerator.Instrument, (ushort)i);
                    }
                    break;

                // Ignore other instrument types
                default:
                    break;
            }

            // If there is any error in the process just ignore it and silently continue
            // In fact dozen of errors always happened all the times so I removed any form of error messages
        }
        catch
        {
            // ignored
        }
    }

    // Display verbose to console or output to file if requested
    private void Print(string s)
    {
        if (_verboseFlag)
            _tw?.Write(s);
    }

// Display ADSR values used
    private void Adsr(uint adsr)
    {
        int attack = (int)(adsr & 0xFF);
        int decay = (int)((adsr >> 8) & 0xFF);
        int sustain = (int)((adsr >> 16) & 0xFF);
        int release = (int)(adsr >> 24);
        // Print ADSR values
        _tw?.WriteLine($"      ADSR: {attack}, {decay}, {sustain}, {release}");
    }

// Display duty cycle used
    private void DutyCycle(int duty)
    {
        string cycle = (duty & 3) switch
        {
            0 => "12.5%",
            1 => "25%",
            2 => "50%",
            3 => "75%",
            _ => throw new ArgumentOutOfRangeException()
        };
        _tw?.WriteLine($"      Duty cycle: {cycle}");
    }

// This function read instrument data and outputs info on the screen or on the verbose file
// it's not actually needed to convert the data to SF2 format, but is very useful for debugging
    private void VerboseInstrument(InstData inst, bool recursive)
    {
        // Do nothing with unused instruments
        if (inst.Word0 == 0x3c01 && inst.Word1 == 0x02 && inst.Word2 == 0x0F0000) return;

        byte instrType = (byte)(inst.Word0 & 0xff);
        _tw?.Write($"  Type: 0x{instrType:x}  ");
        switch (instrType)
        {
            // Sampled instruments
            case 0:
            case 8:
            case 0x10:
            case 0x18:
            case 0x20:
            case 0x28:
            case 0x30:
            case 0x38:
                {
                    uint sadr = inst.Word1 & 0x3ffffff;
                    _tw?.WriteLine($"(sample @0x{sadr:x})");

                    try
                    {
                        _inGba.Position = sadr;
                        uint loop = _inGba.ReadUInt32LittleEndian();
                        uint pitch = _inGba.ReadUInt32LittleEndian();
                        uint loopPos = _inGba.ReadUInt32LittleEndian();
                        uint len = _inGba.ReadUInt32LittleEndian();

                        _tw?.WriteLine($"      Pitch: {pitch / 1024}");
                        _tw?.WriteLine($"      Length: {len}");

                        if (loop == 0)
                            _tw?.WriteLine("      Not looped");
                        else if (loop == 0x40000000)
                            _tw?.WriteLine($"      Loop enabled at: {loopPos}");
                        else if (loop == 0x1)
                            _tw?.WriteLine("      BDPCM compressed");
                        else
                            _tw?.WriteLine("      Unknown loop type");

                        Adsr(inst.Word2);
                    }
                    catch
                    {
                        _tw?.WriteLine("Error: Invalid instrument");
                    }
                }
                break;

            // Pulse channel 1 instruments
            case 1:
            case 9:
                {
                    _tw?.Write("(GB pulse channel 1)");
                    if ((byte)inst.Word0 != 8) // Display sweep if enabled on GB channel 1
                        _tw?.WriteLine($"      Sweep: 0x{inst.Word0 & 0xFF:x}");

                    Adsr(inst.Word2);
                    DutyCycle((int)inst.Word1);
                }
                break;

            // Pulse channel 2 instruments
            case 2:
            case 10:
            case 18:
                {
                    _tw?.Write("(GB pulse channel 2)");
                    Adsr(inst.Word2);
                    DutyCycle((int)inst.Word1);
                }
                break;

            // Channel 3 instruments
            case 3:
            case 11:
                {
                    _tw?.Write("(GB channel 3)");
                    Adsr(inst.Word2);
                    _tw?.Write("      Waveform: ");

                    try
                    {
                        // Seek to waveform's location
                        _inGba.Position = inst.Word1 & 0x3ffffff;

                        int[] waveform = new int[32];

                        for (int j = 0; j < 16; j++)
                        {
                            byte a = _inGba.ReadUInt8LittleEndian();
                            waveform[2 * j] = a >> 4;
                            waveform[2 * j + 1] = a & 0xF;
                        }

                        // Display waveform in text format
                        for (int j = 7; j >= 0; j--)
                        {
                            for (int k = 0; k != 32; k++)
                            {
                                if (waveform[k] == 2 * j)
                                    _tw?.Write('_');
                                else if (waveform[k] == 2 * j + 1)
                                    _tw?.Write('-');
                                else
                                    _tw?.Write(' ');
                            }
                            _tw?.WriteLine();
                        }
                    }
                    catch
                    {
                        _tw?.WriteLine("Error: Invalid instrument");
                    }
                }
                break;

            // Noise instruments
            case 4:
            case 12:
                _tw?.Write("(GB noise channel 4)");
                Adsr(inst.Word2);
                if (inst.Word1 == 0)
                    _tw?.WriteLine("      long random sequence");
                else
                    _tw?.WriteLine("      short random sequence");
                break;

            // Key-split instruments
            case 0x40:
                _tw?.Write("Key-split instrument");

                if (!recursive)
                {
                    bool[] keysUsed = new bool[128];
                    try
                    {
                        // seek to key table's location
                        _inGba.Position = inst.Word2 & 0x3ffffff;

                        for (int k = 0; k != 128; k++)
                        {
                            byte c = _inGba.ReadUInt8LittleEndian();
                            if ((c & 0x80) != 0) continue; // Ignore entries with MSB set (invalid)
                            keysUsed[c] = true;
                        }

                        int instrTable = (int)(inst.Word1 & 0x3ffffff);

                        for (int k = 0; k != 128; k++)
                        {
                            // Decode instruments used at least once in the key table
                            if (keysUsed[k])
                            {
                                try
                                {
                                    // Seek to the addressed instrument
                                    _inGba.Position = instrTable + 12 * k;
                                    // Read the addressed instrument
                                    InstData subInstr = _inGba.ReadStructure<InstData>();

                                    _tw?.WriteLine();
                                    _tw?.Write($"      Sub_instrument {k}");
                                    VerboseInstrument(subInstr, true);
                                }
                                catch
                                {
                                    _tw?.Write("Error: Invalid sub-instrument");
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignored
                    }
                }
                else
                    _tw?.Write("   Illegal double-recursive instrument!");
                break;

            // Every key split instruments
            case 0x80:
                _tw?.Write("Every key split instrument");

                if (!recursive)
                {
                    uint address = inst.Word1 & 0x3ffffff;
                    for (int k = 0; k < 128; ++k)
                    {
                        try
                        {
                            _inGba.Position = address + k * 12;
                            InstData keyInstr = _inGba.ReadStructure<InstData>();

                            _tw?.WriteLine();
                            _tw?.Write($"   Key {k}");
                            VerboseInstrument(keyInstr, true);
                        }
                        catch
                        {
                            _tw?.Write("Error: Illegal sub-instrument");
                        }
                    }
                }
                else // Prevent instruments with multiple recursivities
                    _tw?.Write("   Illegal double-recursive instrument!");
                break;

            default:
                _tw?.Write("Unknown instrument type");
                return;
        }
        if (recursive)
            _tw?.WriteLine($"      Key: {(inst.Word1 >> 8)&0xFF}, Pan: {inst.Word1 >> 24}");
    }

    private void ParseArguments(string[] args)
    {
        if (args.Length == 0) PrintInstructions();
        bool infileFound = false;
        bool outfileFound = false;
        for (int i = 0; i < args.Length; i++)
        {
            // Enable verbose if -v flag encountered in arguments list
            if (args[i][0] == '-')
            {
                if (args[i] == "-v")
                {
                    _verboseFlag = true;

                    // Verbose to file if a file name is given
                    if (i < args.Length - 1 && args[i + 1][0] != '-')
                    {
                        _verboseOutputToFile = true;
                        try
                        {
                            _tw = File.CreateText(args[i].Substring(2));
                        }
                        catch
                        {
                            Console.WriteLine($"Invalid output log file: {args[i].Substring(2)}");
                            throw new EnvironmentExitException(-1);
                        }
                    }
                }

                // Change sampling rate if -s is encountered
                else if (args[i][1] == 's')
                {
                    //change_sample_rate = true;
                    if (!uint.TryParse(args[i].Substring(2), out _sampleRate))
                    {
                        Console.Error.WriteLine($"Error: sampling rate {args[i].Substring(2)} is not a valid number.");
                        throw new EnvironmentExitException(-1);
                    }
                }

                // Change main volume if -mv is encountered
                else if (args[i][1] == 'm' && args[i][2] == 'v')
                {
                    if (!uint.TryParse(args[i].Substring(3), out uint volume) || volume is 0 or > 15)
                    {
                        Console.Error.WriteLine($"Error: main volume {args[i].Substring(3)} is not valid (should be 0-15).");
                        throw new EnvironmentExitException(-1);
                    }
                    _mainVolume = volume;
                }
                else if (args[i] == "-gm")
                    _gmPresetNames = true;
                else if (args[i] == "--help")
                {
                    PrintInstructions();
                    throw new EnvironmentExitException(0);
                }
            }

            // Try to parse an address and add it to list if succes
            else if (!infileFound)
            {
                // Input File
                infileFound = true;
                try
                {
                    using var fs = File.OpenRead(args[0]);
                    _inGba = new MemoryStream();
                    fs.CopyTo(_inGba);
                }
                catch
                {
                    Console.Error.WriteLine($"Can't read input GBA file: {args[0]}");
                    throw new EnvironmentExitException(-1);
                }
            }
            else if (!outfileFound)
            {
                outfileFound = true;
                string fn = args[i];
                if (Path.GetExtension(fn).ToLowerInvariant() != ".sf2")
                {
                    // Append ".sf2" after the given file name if there isn't it already
                    fn += ".sf2";
                }

                try
                {
                    _outSf2 = File.Create(fn);
                }
                catch
                {
                    Console.Error.WriteLine($"Can't write to file: {fn}");
                    throw new EnvironmentExitException(-1);
                }
            }
            else
            {
                string arg = args[i];
                if (arg.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                    arg = arg.Substring(2);
                if (!InternalUtils.TryParseUIntHd(arg, out uint address))
                {
                    PrintInstructions();
                    throw new EnvironmentExitException(0);
                }
                _addresses.Add(address);
            }
        }
        // Diagnostize errors/missing information
        if (!infileFound)
        {
            Console.Error.WriteLine("An input .gba file should be given. Use --help for more information.");
            throw new EnvironmentExitException(-1);
        }
        if (!outfileFound)
        {
            Console.Error.WriteLine("An output .sf2 file should be given. Use --help for more information.");
            throw new EnvironmentExitException(-1);
        }
        if (!_addresses.Any())
        {
            Console.Error.WriteLine("At least one adress should be given for decoding. Use --help for more information.");
            throw new EnvironmentExitException(-1);
        }
    }

    /// <summary>
    /// Main execution function for sound font ripper.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>Something.</returns>
    public static int Main(string[] args)
    {
        SoundFontRipper sfr = new();
        return sfr.MainInternal(args);
    }

    private int MainInternal(string[] args)
    {
        Console.WriteLine("GBA ROM sound font ripper (c) 2012 Bregalad");

        // Parse arguments without the program name
        ParseArguments(args);

        if (_verboseFlag && !_verboseOutputToFile) _tw = Console.Out;

        // Compute prefix (path) of this program's name

        // Create SF2 class
        _sf2 = new Sf2(_sampleRate);
        _instruments = new GbaInstr(_sf2);

        // Read instrument data from input GBA file
        InstData[] instrData = new InstData[128];

        // Decode all banks
        _currentBank = 0;
        var ad = _addresses.ToList();
        for (int i = 0; i < ad.Count; i++, ++_currentBank)
        {
            _currentAddress = ad[i];
            uint nextAddress = ad[i + 1 == ad.Count ? i : i + 1];

            // Limit the # of presets if the addresses overlaps
            uint ninstr = 128;
            if (i + 1 != ad.Count && (nextAddress - _currentAddress) / 12 < 128)
                ninstr = (nextAddress - _currentAddress) / 12;

            // Seek at the start of the sound bank
            try
            {
                // Read entire sound bank in memory
                _inGba.Position = _currentAddress;
                for (int j = 0; j < ninstr; j++)
                    instrData[j] = _inGba.ReadStructure<InstData>(12);
            }
            catch
            {
                Console.Error.WriteLine($"Error: Invalid position within input GBA file: 0x{_currentAddress:x}");
                return 0;
            }

            // Decode all instruments
            for (_currentInstrument = 0; _currentInstrument < ninstr; ++_currentInstrument, _currentAddress += 12)
            {
                Print($"\nBank: {_currentBank}, Instrument: {_currentInstrument} @0x{_currentAddress:x}");

                // Ignore unused instruments
                if (instrData[_currentInstrument].Word0 == 0x3c01
                    && instrData[_currentInstrument].Word1 == 0x02
                    && instrData[_currentInstrument].Word2 == 0x0F0000)
                {
                    Print(" (unused)");
                    continue;
                }

                if (_verboseFlag)
                    VerboseInstrument(instrData[_currentInstrument], false);

                // Build equivalent SF2 instrument
                BuildInstrument(instrData[_currentInstrument]);
            }
        }

        if (_verboseOutputToFile)
        {
            Print("\n\n EOF");
            _tw?.Close();
        }

        Console.Write("Dump complete, now outputting SF2 data...");

        _sf2.Write(_outSf2);
        _outSf2.Close();
        if (_verboseOutputToFile) _tw?.Dispose();

        Console.WriteLine(" Done!");
        return 0;
    }
}
