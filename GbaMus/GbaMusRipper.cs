using System.Buffers;
using System.Text;

namespace GbaMus;

/// <summary>
/// Provides gba_mus_ripper functionality.
/// </summary>
public static class GbaMusRipper
{
    private static readonly int[] s_sampleRates = { -1, 5734, 7884, 10512, 13379, 15768, 18157, 21024, 26758, 31536, 36314, 40137, 42048 };

    private static void PrintInstructions(TextWriter? textWriter)
    {
        if (textWriter == null) return;
        textWriter.WriteLine("  /==========================================================================\\");
        textWriter.WriteLine("-<   GBA Mus Ripper 3.3 (c) 2012-2015 Bregalad, (c) 2017-2018 CaptainSwag101  >-");
        textWriter.WriteLine("  \\==========================================================================/");
        textWriter.WriteLine();
        textWriter.WriteLine("Usage: gba_mus_ripper (input_file) [-o output_directory] [address] [flags]");
        textWriter.WriteLine();
        textWriter.WriteLine("-gm  : Give General MIDI names to presets. Note that this will only change the names and will NOT magically turn the soundfont into a General MIDI compliant soundfont.");
        textWriter.WriteLine("-rc  : Rearrange channels in output MIDIs so channel 10 is avoided. Needed by sound cards where it's impossible to disable \"drums\" on channel 10 even with GS or XG commands.");
        textWriter.WriteLine("-xg  : Output MIDI will be compliant to XG standard (instead of default GS standard).");
        textWriter.WriteLine("-sb  : Separate banks. Every sound bank is riper to a different .sf2 file and placed into different sub-folders (instead of doing it in a single .sf2 file and a single folder).");
        textWriter.WriteLine("-raw : Output MIDIs exactly as they're encoded in ROM, without linearise volume and velocities and without simulating vibratos.");
        textWriter.WriteLine("[address]: Force address of the song table manually. This is required for manually dumping music data from ROMs where the location can't be detected automatically.");
    }

    private static void ParseArgs(string[] args, ref Settings settings, out string gbaPath, out string? outPath)
    {
        bool gm = false, xg = false, rc = false, sb = false, raw = false;
        gbaPath = null!;
        outPath = null!;
        uint songTblPtr = 0;
        if (args.Length < 1)
        {
            PrintInstructions(settings.Debug);
            throw new EnvironmentExitException(0);
        }

        bool pathFound = false, songTblFound = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i][0] == '-')
            {
                switch (args[i])
                {
                    case "--help":
                        PrintInstructions(settings.Debug);
                        throw new EnvironmentExitException(0);
                    case "-gm":
                        gm = true;
                        break;
                    case "-xg":
                        xg = true;
                        break;
                    case "-rc":
                        rc = true;
                        break;
                    case "-sb":
                        sb = true;
                        break;
                    case "-raw":
                        raw = true;
                        break;
                    case "-o" when args.Length >= i + 1:
                        outPath = args[i + 1];
                        break;
                    default:
                        throw new ArgumentException($"Error: Unknown command line option: {args[i]}. Try with --help to get information.");
                }
            }
            // Convert given address to binary, use it instead of automatically detected one
            else if (!pathFound)
            {
                gbaPath = args[i];
                pathFound = true;
            }
            else if (!songTblFound)
            {
                if (!InternalUtils.TryParseUIntHd(args[i], out songTblPtr))
                {
                    throw new ArgumentException($"Error: {args[i]} is not a valid song table address.");
                }
                songTblFound = true;
            }
            else
            {
                throw new ArgumentException($"Error: Don't know what to do with {args[i]}. Try with --help to get more information.");
            }
        }
        if (!pathFound)
        {
            throw new ArgumentException("Error: No input GBA file. Try with --help to get more information.");
        }
        settings = settings with
        {
            Gm = gm,
            Xg = xg,
            Rc = rc,
            Sb = sb,
            Raw = raw,
            SongTblPtr = songTblPtr
        };
    }

    /// <summary>
    /// Main execution function for song ripper.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>Something.</returns>
    public static int Main(string[] args)
    {
        Settings settings = new(Console.Out, Console.Error);
        string gbaPath;
        string? outPath;
        try
        {
            ParseArgs(args, ref settings, out gbaPath, out outPath);
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

        // Get GBA file
        FileStream inGba;
        try
        {
            inGba = File.OpenRead(gbaPath);
        }
        catch
        {
            settings.Error?.WriteLine($"Error: Can't open file {gbaPath} for reading.");
            throw new EnvironmentExitException(-1);
        }

        // Name is filename without the extention and without path
        string inGbaPath = gbaPath;
        string name = Path.GetFileNameWithoutExtension(inGbaPath);

        outPath ??= ".";

        int sampleRate;
        int mainVolume;
        uint songTblEndPtr;
        List<uint> songList;
        SortedSet<uint> soundBankSet;
        Dictionary<uint, uint> soundBankSrcDict;
        Dictionary<uint, int> soundBankDict;
        try
        {
            Load(inGba, settings, out sampleRate, out mainVolume, out songTblEndPtr,
                out songList, out soundBankSet, out soundBankSrcDict, out soundBankDict);
        }
        catch (IOException e)
        {
            settings.Error?.WriteLine(e.Message);
            return -2;
        }

        // Close GBA file so that SongRipper can access it
        inGba.Close();

        // Create directories for each sound bank if separate banks is enabled
        if (settings.Sb)
        {
            for (int j = 0; j < soundBankSet.Count; j++) Directory.CreateDirectory(Path.Combine(outPath, $"soundbank_{j:D4}"));
        }

        for (uint i = 0; i < songList.Count; i++)
        {
            if (songList[(int)i] != songTblEndPtr)
            {
                if (!soundBankSrcDict.ContainsKey(i)) continue;
                uint bankIndex = (uint)soundBankDict[soundBankSrcDict[i]];
                List<string> seqRipCmd = new();
                seqRipCmd.Add(inGbaPath);

                string cippyP = outPath;
                // Add leading zeroes to file name
                if (settings.Sb) cippyP = Path.Combine(cippyP, $"soundbank_{bankIndex:D4}");
                cippyP = Path.Combine(cippyP, $"song{i:D4}.mid");
                seqRipCmd.Add(cippyP);

                seqRipCmd.Add($"0x{songList[(int)i]:x}");
                seqRipCmd.Add(settings.Rc ? "-rc" : settings.Xg ? "-xg" : "-gs");
                if (!settings.Raw)
                {
                    seqRipCmd.Add("-sv");
                    seqRipCmd.Add("-lv");
                }
                // Bank number, if banks are not separated
                if (!settings.Sb)
                    seqRipCmd.Add($"-b{bankIndex}");

                settings.Debug?.WriteLine($"Song {i}");

                int rcd;
                PrintArgSeq(seqRipCmd, settings.Debug);
                try
                {
                    rcd = SongRipper.Main(seqRipCmd.ToArray());
                }
                catch (EnvironmentExitException e)
                {
                    rcd = e.Code;
                }
                if (rcd == 0) settings.Debug?.WriteLine("An error occurred while calling song_ripper.");
            }
        }

        if (settings.Sb)
        {
            // Rips each sound bank in a different file/folder
            int j = 0;
            foreach (uint b in soundBankSet)
            {
                uint bankIndex = (uint)j++;

                string foldername = $"soundbank_{bankIndex:D4}";
                List<string> sfRipArgs = new();
                sfRipArgs.Add(inGbaPath);
                string cippyP = Path.Combine(outPath, foldername, foldername + ".sf2");
                sfRipArgs.Add(cippyP);
                if (sampleRate != 0) sfRipArgs.Add($"-s{sampleRate}");
                if (mainVolume != 0) sfRipArgs.Add($"-mv{mainVolume}");
                if (settings.Gm) sfRipArgs.Add("-gm");
                sfRipArgs.Add($"0x{b:x}");

                PrintArgSeq(args, settings.Debug);
                try
                {
                    SoundFontRipper.Main(sfRipArgs.ToArray());
                }
                catch (EnvironmentExitException)
                {
                    // ignored
                }
            }
        }
        else
        {
            // Rips each sound bank in a single soundfont file
            // Build argument list to call sound_font_riper
            // Output sound font named after the input ROM
            List<string> sfRipArgs = new();
            sfRipArgs.Add(inGbaPath);
            string cippyP = Path.Combine(outPath, name + ".sf2");
            sfRipArgs.Add(cippyP);
            if (sampleRate != 0) sfRipArgs.Add($"-s{sampleRate}");
            if (mainVolume != 0) sfRipArgs.Add($"-mv{mainVolume}");
            // Pass -gm argument if necessary
            if (settings.Gm) sfRipArgs.Add("-gm");

            // Make sound banks addresses list.
            foreach (uint t in soundBankSet)
                sfRipArgs.Add($"0x{t:x}");

            // Call sound font ripper
            PrintArgSeq(sfRipArgs, settings.Debug);
            try
            {
                SoundFontRipper.Main(sfRipArgs.ToArray());
            }
            catch (EnvironmentExitException)
            {
                // ignored
            }
        }

        settings.Debug?.WriteLine("Rip completed!");
        return 0;
    }

    /// <summary>
    /// Loads data for the specified stream.
    /// </summary>
    /// <param name="stream">Stream to load from.</param>
    /// <param name="settings">Settings.</param>
    /// <param name="sampleRate">Detected or set sample rate.</param>
    /// <param name="mainVolume">Detected or set main volume.</param>
    /// <param name="songTblEndPtr">End of song table.</param>
    /// <param name="songList">Song list.</param>
    /// <param name="soundBankSet">Sound banks.</param>
    /// <param name="soundBankSrcDict">Mapping of song index to sound bank data offset.</param>
    /// <param name="soundBankDict">Mapping of sound bank data offset to generated sound bank number (in order of <paramref name="soundBankSet"/>).</param>
    /// <exception cref="IOException">Thrown for I/O errors.</exception>
    public static void Load(Stream stream, Settings settings, out int sampleRate, out int mainVolume, out uint songTblEndPtr,
        out List<uint> songList, out SortedSet<uint> soundBankSet, out Dictionary<uint, uint> soundBankSrcDict, out Dictionary<uint, int> soundBankDict)
    {
        sampleRate = 0;
        mainVolume = 0;

        uint songTblPtr = settings.SongTblPtr;
        // If the user hasn't provided an address manually, we'll try to automatically detect it
        if (songTblPtr == 0)
        {
            // Auto-detect address of sappy engine
            int soundEngineAdr;
            byte[] tmp = ArrayPool<byte>.Shared.Rent((int)stream.Length);
            try
            {
                stream.ForceRead(tmp, 0, (int)stream.Length);
                soundEngineAdr = SappyDetector.Find(tmp, settings);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(tmp);
            }

            if (soundEngineAdr > stream.Length)
                throw new IOException($"Error: Invalid offset within input GBA file: 0x{soundEngineAdr:x}");

            stream.Position = soundEngineAdr;

            // Engine parameter's word
            uint parameterWord = stream.ReadUInt32LittleEndian();

            // Get sampling rate
            sampleRate = s_sampleRates[(parameterWord >> 16) & 0xf];
            mainVolume = (int)((parameterWord >> 12) & 0xf);

            // Compute address of song table
            uint songLevels = stream.ReadUInt32LittleEndian(); // Read # of song levels
            settings.Debug?.WriteLine($"# of song levels: {songLevels}");
            songTblPtr = stream.GetGbaPointer() + 12 * songLevels;
        }

        //  Get the size of the input GBA file

        if (songTblPtr >= stream.Length)
            throw new IOException($"Fatal error: Song table at 0x{songTblPtr:x} is past the end of the file.");
        stream.Position = songTblPtr;

        settings.Debug?.WriteLine("Parsing song table...");
        // New list of songs
        songList = new List<uint>();

        // Ignores entries which are made of 0s at the start of the song table
        // this fix was necessarily for the game Fire Emblem
        uint songPointer;
        while (true)
        {
            songPointer = stream.ReadUInt32LittleEndian();
            if (songPointer != 0) break;
            songTblPtr += 4;
        }

        uint i = 0;
        while (true)
        {
            songPointer -= 0x8000000; // Adjust pointer

            // Stop as soon as we met with an invalid pointer
            if (songPointer == 0 || songPointer >= stream.Length) break;

            for (int j = 4; j != 0; --j) stream.ReadUInt8LittleEndian(); // Discard 4 bytes (sound group)
            songList.Add(songPointer); // Add pointer to list
            i++;
            songPointer = stream.ReadUInt32LittleEndian();
        }

        // As soon as data that is not a valid pointer is found, the song table is terminated

        // End of song table
        songTblEndPtr = 8 * i + songTblPtr;

        settings.Debug?.WriteLine("Collecting sound bank list...");

        // New list of sound banks
        soundBankSet = new SortedSet<uint>();
        soundBankSrcDict = new Dictionary<uint, uint>();
        for (i = 0; i < songList.Count; i++)
        {
            // Ignore unused song, which points to the end of the song table (for some reason)
            if (songList[(int)i] != songTblEndPtr)
            {
                // Seek to song data
                stream.Position = songList[(int)i] + 4;
                if (stream.Position > stream.Length) continue;
                uint soundBankPtr = stream.GetGbaPointer();

                // Add sound bank to list if not already in the list
                if (soundBankPtr == 0) continue;
                soundBankSet.Add(soundBankPtr);
                soundBankSrcDict.Add(i, soundBankPtr);
            }
        }
        soundBankDict = soundBankSet.Select((v, idx) => (v, idx)).ToDictionary(v => v.v, v => v.idx);
    }

    private static void PrintArgSeq(IReadOnlyList<string> args, TextWriter? textWriter)
    {
        if (textWriter == null) return;
        StringBuilder sb = new();
        foreach (string arg in args)
            sb.Append(" \"").Append(arg).Append('\"');
        textWriter.WriteLine($"Call args: {sb}");
    }

    /// <summary>
    /// Settings for gba mus ripper.
    /// </summary>
    /// <param name="Debug">Debug output.</param>
    /// <param name="Error">Error output.</param>
    /// <param name="Gm">Give General MIDI names to presets. Note that this will only change the names and will NOT magically turn the soundfont into a General MIDI compliant soundfont.</param>
    /// <param name="Xg">Output MIDI will be compliant to XG standard (instead of default GS standard).</param>
    /// <param name="Rc">Rearrange channels in output MIDIs so channel 10 is avoided. Needed by sound cards where it's impossible to disable "drums" on channel 10 even with GS or XG commands.</param>
    /// <param name="Sb">Separate banks. Every sound bank is ripped to a different .sf2 file and placed into different sub-folders (instead of doing it in a single .sf2 file and a single folder).</param>
    /// <param name="Raw">Output MIDIs exactly as they're encoded in ROM, without linearize volume and velocities and without simulating vibratos.</param>
    /// <param name="SongTblPtr">Force address of the song table manually. This is required for manually dumping music data from ROMs where the location can't be detected automatically.</param>
    public record Settings(TextWriter? Debug = null, TextWriter? Error = null, bool Gm = false, bool Xg = false, bool Rc = false, bool Sb = false, bool Raw = false,
            uint SongTblPtr = 0)
        : ToolSettings(Debug, Error);
}
