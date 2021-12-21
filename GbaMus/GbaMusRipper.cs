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

    private static void ParseArgs(string[] args, TextWriter debug, TextWriter error, out Settings settings, out string gbaPath, out string? outPath)
    {
        bool gm = false, xg = false, rc = false, sb = false, raw = false;
        gbaPath = null!;
        outPath = null!;
        uint songTblPtr = 0;
        if (args.Length < 1)
        {
            PrintInstructions(debug);
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
                        PrintInstructions(debug);
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
        settings = new Settings(debug, error, gm, xg, rc, sb, raw, songTblPtr);
    }

    /// <summary>
    /// Main execution function for song ripper.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>Something.</returns>
    public static int Main(string[] args)
    {
        // Parse arguments (without program name)
        Settings settings;
        string gbaPath;
        string? outPath;
        try
        {
            ParseArgs(args, Console.Out, Console.Error, out settings, out gbaPath, out outPath);
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

        int sampleRate = 0, mainVolume = 0; // Use default values when those are '0'

        uint songTblPtr = settings.SongTblPtr;
        // If the user hasn't provided an address manually, we'll try to automatically detect it
        if (songTblPtr == 0)
        {
            // Auto-detect address of sappy engine
            int soundEngineAdr;
            try
            {
                byte[] tmp = ArrayPool<byte>.Shared.Rent((int)inGba.Length);
                try
                {
                    inGba.ForceRead(tmp, 0, (int)inGba.Length);
                    soundEngineAdr = SappyDetector.Find(tmp, settings);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(tmp);
                }
            }
            catch
            {
                throw new EnvironmentExitException(0);
            }

            try
            {
                inGba.Position = soundEngineAdr;
            }
            catch
            {
                settings.Error?.WriteLine($"Error: Invalid offset within input GBA file: 0x{soundEngineAdr:x}");
                throw new EnvironmentExitException(0);
            }

            // Engine parameter's word
            uint parameterWord = inGba.ReadUInt32LittleEndian();

            // Get sampling rate
            sampleRate = s_sampleRates[(parameterWord >> 16) & 0xf];
            mainVolume = (int)((parameterWord >> 12) & 0xf);

            // Compute address of song table
            uint songLevels = inGba.ReadUInt32LittleEndian(); // Read # of song levels
            settings.Debug?.WriteLine($"# of song levels: {songLevels}");
            songTblPtr = inGba.GetGbaPointer() + 12 * songLevels;
        }

        // Create a directory named like the input ROM, without the .gba extention
        Directory.CreateDirectory(outPath);

        //  Get the size of the input GBA file

        if (songTblPtr >= inGba.Length)
        {
            settings.Error?.WriteLine($"Fatal error: Song table at 0x{songTblPtr:x} is past the end of the file.");
            throw new EnvironmentExitException(-2);
        }

        settings.Debug?.WriteLine("Parsing song table...");
        // New list of songs
        List<uint> songList = new();

        try
        {
            inGba.Position = songTblPtr;
        }
        catch
        {
            settings.Error?.WriteLine($"Fatal error: Can't seek to song table at: 0x{songTblPtr:x}");
            throw new EnvironmentExitException(-3);
        }

        // Ignores entries which are made of 0s at the start of the song table
        // this fix was necessarily for the game Fire Emblem
        uint songPointer;
        while (true)
        {
            songPointer = inGba.ReadUInt32LittleEndian();
            if (songPointer != 0) break;
            songTblPtr += 4;
        }

        uint i = 0;
        while (true)
        {
            songPointer -= 0x8000000; // Adjust pointer

            // Stop as soon as we met with an invalid pointer
            if (songPointer == 0 || songPointer >= inGba.Length) break;

            for (int j = 4; j != 0; --j) inGba.ReadUInt8LittleEndian(); // Discard 4 bytes (sound group)
            songList.Add(songPointer); // Add pointer to list
            i++;
            songPointer = inGba.ReadUInt32LittleEndian();
        }

        // As soon as data that is not a valid pointer is found, the song table is terminated

        // End of song table
        uint songTblEndPtr = 8 * i + songTblPtr;

        settings.Debug?.WriteLine("Collecting sound bank list...");

        // New list of sound banks
        SortedSet<uint> soundBankSet = new();
        Dictionary<uint, uint> soundBankSrcDict = new();
        for (i = 0; i < songList.Count; i++)
        {
            // Ignore unused song, which points to the end of the song table (for some reason)
            if (songList[(int)i] != songTblEndPtr)
            {
                // Seek to song data
                inGba.Position = songList[(int)i] + 4;
                if (inGba.Position > inGba.Length) continue;
                uint soundBankPtr = inGba.GetGbaPointer();

                // Add sound bank to list if not already in the list
                soundBankSet.Add(soundBankPtr);
                soundBankSrcDict.Add(i, soundBankPtr);
            }
        }
        Dictionary<uint, int> soundBankDict = soundBankSet.Select((v, idx) => (v, idx)).ToDictionary(v => v.v, v => v.idx);

        // Close GBA file so that SongRipper can access it
        inGba.Close();

        // Create directories for each sound bank if separate banks is enabled
        if (settings.Sb)
        {
            for (int j = 0; j < soundBankSet.Count; j++) Directory.CreateDirectory(Path.Combine(outPath, $"soundbank_{j:D4}"));
        }

        for (i = 0; i < songList.Count; i++)
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
                if (t != 0)
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

    private static void PrintArgSeq(IReadOnlyList<string> args, TextWriter? textWriter)
    {
        if (textWriter == null) return;
        StringBuilder sb = new();
        foreach (string arg in args)
            sb.Append(" \"").Append(arg).Append('\"');
        textWriter.WriteLine($"Call args: {sb}");
    }

    private record Settings(TextWriter? Debug, TextWriter? Error, bool Gm, bool Xg, bool Rc, bool Sb, bool Raw, uint SongTblPtr)
        : ToolSettings(Debug, Error);
}
