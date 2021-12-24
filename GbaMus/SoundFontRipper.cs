using System.Text;

namespace GbaMus;

/// <summary>
/// Provides sound_font_ripper functionality.
/// </summary>
public class SoundFontRipper
{
    private TextWriter? _textWriter;
    private Settings _settings;
    private Sf2 _sf2;
    private GbaInstr _instruments;
    private MemoryStream _inGba;

    //private bool change_sample_rate;
    private uint _currentAddress;
    private uint _currentBank;
    private uint _currentInstrument;


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
        if (_settings.MainVolume < 15)
        {
            ushort attenuation = (ushort)(100.0 * Math.Log(15.0 / _settings.MainVolume));
            _sf2.AddNewPresetGenerator(SfGenerator.InitialAttenuation, attenuation);
        }
    }

    // Convert a GBA instrument in its SF2 counterpart
    // if any kind of error happens, it will do nothing and exit
    private void BuildInstrument(InstData inst)
    {
        byte instrType = (byte)(inst.Word0 & 0xff);
        string name;
        if (_settings.GmPresetNames)
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
        if (!_settings.VerboseFlag) return;
        if (_textWriter != null) _textWriter.Write(s);
        else _settings.Debug?.Write(s);
    }

// Display ADSR values used
    private void Adsr(uint adsr)
    {
        int attack = (int)(adsr & 0xFF);
        int decay = (int)((adsr >> 8) & 0xFF);
        int sustain = (int)((adsr >> 16) & 0xFF);
        int release = (int)(adsr >> 24);
        // Print ADSR values
        _settings.Debug?.WriteLine($"      ADSR: {attack}, {decay}, {sustain}, {release}");
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
        _settings.Debug?.WriteLine($"      Duty cycle: {cycle}");
    }

// This function read instrument data and outputs info on the screen or on the verbose file
// it's not actually needed to convert the data to SF2 format, but is very useful for debugging
    private void VerboseInstrument(InstData inst, bool recursive)
    {
        // Do nothing with unused instruments
        if (inst.Word0 == 0x3c01 && inst.Word1 == 0x02 && inst.Word2 == 0x0F0000) return;

        byte instrType = (byte)(inst.Word0 & 0xff);
        _settings.Debug?.Write($"  Type: 0x{instrType:x}  ");
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
                    _settings.Debug?.WriteLine($"(sample @0x{sadr:x})");

                    try
                    {
                        _inGba.Position = sadr;
                        uint loop = _inGba.ReadUInt32LittleEndian();
                        uint pitch = _inGba.ReadUInt32LittleEndian();
                        uint loopPos = _inGba.ReadUInt32LittleEndian();
                        uint len = _inGba.ReadUInt32LittleEndian();

                        _settings.Debug?.WriteLine($"      Pitch: {pitch / 1024}");
                        _settings.Debug?.WriteLine($"      Length: {len}");

                        if (loop == 0)
                            _settings.Debug?.WriteLine("      Not looped");
                        else if (loop == 0x40000000)
                            _settings.Debug?.WriteLine($"      Loop enabled at: {loopPos}");
                        else if (loop == 0x1)
                            _settings.Debug?.WriteLine("      BDPCM compressed");
                        else
                            _settings.Debug?.WriteLine("      Unknown loop type");

                        Adsr(inst.Word2);
                    }
                    catch
                    {
                        _settings.Debug?.WriteLine("Error: Invalid instrument");
                    }
                }
                break;

            // Pulse channel 1 instruments
            case 1:
            case 9:
                {
                    _settings.Debug?.Write("(GB pulse channel 1)");
                    if ((byte)inst.Word0 != 8) // Display sweep if enabled on GB channel 1
                        _settings.Debug?.WriteLine($"      Sweep: 0x{inst.Word0 & 0xFF:x}");

                    Adsr(inst.Word2);
                    DutyCycle((int)inst.Word1);
                }
                break;

            // Pulse channel 2 instruments
            case 2:
            case 10:
            case 18:
                {
                    _settings.Debug?.Write("(GB pulse channel 2)");
                    Adsr(inst.Word2);
                    DutyCycle((int)inst.Word1);
                }
                break;

            // Channel 3 instruments
            case 3:
            case 11:
                {
                    _settings.Debug?.Write("(GB channel 3)");
                    Adsr(inst.Word2);
                    _settings.Debug?.Write("      Waveform: ");

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
                                    _settings.Debug?.Write('_');
                                else if (waveform[k] == 2 * j + 1)
                                    _settings.Debug?.Write('-');
                                else
                                    _settings.Debug?.Write(' ');
                            }
                            _settings.Debug?.WriteLine();
                        }
                    }
                    catch
                    {
                        _settings.Debug?.WriteLine("Error: Invalid instrument");
                    }
                }
                break;

            // Noise instruments
            case 4:
            case 12:
                _settings.Debug?.Write("(GB noise channel 4)");
                Adsr(inst.Word2);
                if (inst.Word1 == 0)
                    _settings.Debug?.WriteLine("      long random sequence");
                else
                    _settings.Debug?.WriteLine("      short random sequence");
                break;

            // Key-split instruments
            case 0x40:
                _settings.Debug?.Write("Key-split instrument");
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
                                    _settings.Debug?.WriteLine();
                                    _settings.Debug?.Write($"      Sub_instrument {k}");
                                    VerboseInstrument(subInstr, true);
                                }
                                catch
                                {
                                    _settings.Debug?.Write("Error: Invalid sub-instrument");
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
                    _settings.Debug?.Write("   Illegal double-recursive instrument!");
                break;

            // Every key split instruments
            case 0x80:
                _settings.Debug?.Write("Every key split instrument");

                if (!recursive)
                {
                    uint address = inst.Word1 & 0x3ffffff;
                    for (int k = 0; k < 128; ++k)
                    {
                        try
                        {
                            _inGba.Position = address + k * 12;
                            InstData keyInstr = _inGba.ReadStructure<InstData>();
                            _settings.Debug?.WriteLine();
                            _settings.Debug?.Write($"   Key {k}");
                            VerboseInstrument(keyInstr, true);
                        }
                        catch
                        {
                            _settings.Debug?.Write("Error: Illegal sub-instrument");
                        }
                    }
                }
                else // Prevent instruments with multiple recursivities
                    _settings.Debug?.Write("   Illegal double-recursive instrument!");
                break;

            default:
                _settings.Debug?.Write("Unknown instrument type");
                return;
        }
        if (recursive)
            _settings.Debug?.WriteLine($"      Key: {(inst.Word1 >> 8) & 0xFF}, Pan: {inst.Word1 >> 24}");
    }

    private static void ParseArguments(string[] args, ref Settings settings, out TextWriter? fileTextWriter, out string inGbaFile, out string outSf2File)
    {
        if (args.Length == 0) PrintInstructions();
        bool infileFound = false;
        bool outfileFound = false;
        List<uint> addresses = new();
        bool verboseFlag = false, gmPresetNames = false;
        uint sampleRate = 0, mainVolume = 0;
        fileTextWriter = null;
        inGbaFile = "";
        outSf2File = "";
        for (int i = 0; i < args.Length; i++)
        {
            // Enable verbose if -v flag encountered in arguments list
            if (args[i][0] == '-')
            {
                if (args[i] == "-v")
                {
                    verboseFlag = true;

                    // Verbose to file if a file name is given
                    if (i < args.Length - 1 && args[i + 1][0] != '-')
                    {
                        try
                        {
                            fileTextWriter = File.CreateText(args[i].Substring(2));
                        }
                        catch
                        {
                            throw new ArgumentException($"Invalid output log file: {args[i].Substring(2)}");
                        }
                    }
                }

                // Change sampling rate if -s is encountered
                else if (args[i][1] == 's')
                {
                    //change_sample_rate = true;
                    if (!uint.TryParse(args[i].Substring(2), out sampleRate))
                    {
                        throw new ArgumentException($"Error: sampling rate {args[i].Substring(2)} is not a valid number.");
                    }
                }

                // Change main volume if -mv is encountered
                else if (args[i][1] == 'm' && args[i][2] == 'v')
                {
                    if (!uint.TryParse(args[i].Substring(3), out uint volume) || volume is 0 or > 15)
                    {
                        throw new ArgumentException($"Error: main volume {args[i].Substring(3)} is not valid (should be 0-15).");
                    }
                    mainVolume = volume;
                }
                else if (args[i] == "-gm")
                    gmPresetNames = true;
                else if (args[i] == "--help")
                {
                    PrintInstructions();
                    throw new EnvironmentExitException(0);
                }
            }

            else if (!infileFound)
            {
                // Input File
                infileFound = true;
                inGbaFile = args[i];
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
                outSf2File = fn;
            }
            else
            {
                string arg = args[i];
                if (!InternalUtils.TryParseUIntHd(arg, out uint address))
                {
                    PrintInstructions();
                    throw new EnvironmentExitException(0);
                }
                addresses.Add(address);
            }
        }
        // Diagnostize errors/missing information
        if (!infileFound)
        {
            throw new ArgumentException("An input .gba file should be given. Use --help for more information.");
        }
        if (!outfileFound)
        {
            throw new ArgumentException("An output .sf2 file should be given. Use --help for more information.");
        }
        if (!addresses.Any())
        {
            throw new ArgumentException("At least one address should be given for decoding. Use --help for more information.");
        }
        settings = settings with
        {
            Addresses = addresses,
            VerboseFlag = verboseFlag,
            GmPresetNames = gmPresetNames,
            SampleRate = sampleRate,
            MainVolume = mainVolume
        };
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SoundFontRipper"/> with the specified source stream and settings.
    /// </summary>
    /// <param name="stream">Source stream.</param>
    /// <param name="settings">Settings.</param>
    /// <param name="fileTextWriter">Optional debugging output.</param>
    public SoundFontRipper(Stream stream, Settings settings, TextWriter? fileTextWriter = null)
    {
        _settings = settings;
        _textWriter = fileTextWriter;
        if (stream is MemoryStream ms)
        {
            _inGba = ms;
        }
        else
        {
            _inGba = new MemoryStream();
            stream.CopyTo(_inGba);
        }
        // Create SF2 class
        _sf2 = new Sf2(_settings.SampleRate);
        _instruments = new GbaInstr(_sf2);
    }

    /// <summary>
    /// Main execution function for sound font ripper.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>Something.</returns>
    public static int Main(string[] args)
    {
        return MainInternal(args);
    }

    private static int MainInternal(string[] args)
    {
        Settings settings = new(Console.Out, Console.Error, Array.Empty<uint>());
        settings.Debug?.WriteLine("GBA ROM sound font ripper (c) 2012 Bregalad");

        // Parse arguments without the program name
        TextWriter? fileTextWriter;
        string inGbaFile;
        string outSf2File;
        try
        {
            ParseArguments(args, ref settings, out fileTextWriter, out inGbaFile, out outSf2File);
        }
        catch (ArgumentException e)
        {
            Console.WriteLine(e);
            return -1;
        }
        catch (EnvironmentExitException e)
        {
            return e.Code;
        }

        MemoryStream inGba = new();
        try
        {
            using var fs = File.OpenRead(inGbaFile);
            fs.CopyTo(inGba);
        }
        catch
        {
            Console.Error.WriteLine($"Can't read input GBA file: {inGbaFile}");
            throw new EnvironmentExitException(-1);
        }

        Stream outSf2Src;
        try
        {
            outSf2Src = File.Create(outSf2File);
        }
        catch
        {
            Console.Error.WriteLine($"Can't write to file: {outSf2File}");
            throw new EnvironmentExitException(-1);
        }
        using Stream outSf2 = outSf2Src;
        if (settings.VerboseFlag && fileTextWriter != null) settings = settings with { Debug = fileTextWriter };

        SoundFontRipper r = new(inGba, settings, fileTextWriter);
        try
        {
            r.Write(outSf2);
        }
        catch (IOException e)
        {
            settings.Debug?.WriteLine(e);
            return 0;
        }
        return 0;
    }

    /// <summary>
    /// Writes output for soundbank to stream.
    /// </summary>
    /// <param name="stream">Stream to write to.</param>
    /// <exception cref="IOException">Thrown on an I/O exception.</exception>
    public void Write(Stream stream)
    {
        // Read instrument data from input GBA file
        InstData[] instrData = new InstData[128];

        // Decode all banks
        _currentBank = 0;
        var ad = _settings.Addresses.ToList();
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
                throw new IOException($"Error: Invalid position within input GBA file: 0x{_currentAddress:x}");
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

                if (_settings.VerboseFlag)
                    VerboseInstrument(instrData[_currentInstrument], false);

                // Build equivalent SF2 instrument
                BuildInstrument(instrData[_currentInstrument]);
            }
        }
        if (_textWriter != null)
        {
            Print("\n\n EOF");
            _textWriter.Dispose();
        }
        _settings.Debug?.Write("Dump complete, now outputting SF2 data...");
        _sf2.Write(stream);
        _settings.Debug?.WriteLine(" Done!");
    }

    /// <summary>
    /// Settings for sound font ripper.
    /// </summary>
    /// <param name="Debug">Debug output.</param>
    /// <param name="Error">Error output.</param>
    /// <param name="Addresses">Sound bank addresses.</param>
    /// <param name="VerboseFlag">Display info about the sound font in text format.</param>
    /// <param name="GmPresetNames">Give General MIDI names to presets. Note that this will only change the names and will NOT magically turn the soundfont into a General MIDI compliant soundfont.</param>
    /// <param name="SampleRate">Sampling rate for samples. Default: 22050 Hz</param>
    /// <param name="MainVolume">Main volume for sample instruments. Range: 1-15. Game Boy channels are unaffected.</param>
    public readonly record struct Settings(TextWriter? Debug = null, TextWriter? Error = null, IReadOnlyCollection<uint> Addresses = null!,
        bool VerboseFlag = false, bool GmPresetNames = false,
        uint SampleRate = 22050, uint MainVolume = 15);
}
