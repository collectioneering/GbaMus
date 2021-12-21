using System.Text;

namespace GbaMus;

/// <summary>
/// Provides gba_mus_ripper functionality.
/// </summary>
public class GbaMusRipper
{
    private Stream inGBA;
    private string inGBA_path;
    private string? outPath;
    private int inGBA_size;
    private string name;
    private string path;
    private bool gm = false;
    private bool xg = false;
    private bool rc = false;
    private bool sb = false;
    private bool raw = false;
    private uint song_tbl_ptr = 0;

    private static readonly int[] sample_rates = { -1, 5734, 7884, 10512, 13379, 15768, 18157, 21024, 26758, 31536, 36314, 40137, 42048 };

    private GbaMusRipper()
    {
        inGBA = null!;
        inGBA_path = null!;
        name = null!;
        path = null!;
    }

    private static void print_instructions()
    {
        Console.WriteLine("  /==========================================================================\\");
        Console.WriteLine("-<   GBA Mus Ripper 3.3 (c) 2012-2015 Bregalad, (c) 2017-2018 CaptainSwag101  >-");
        Console.WriteLine("  \\==========================================================================/");
        Console.WriteLine();
        Console.WriteLine("Usage: gba_mus_ripper (input_file) [-o output_directory] [address] [flags]");
        Console.WriteLine();
        Console.WriteLine("-gm  : Give General MIDI names to presets. Note that this will only change the names and will NOT magically turn the soundfont into a General MIDI compliant soundfont.");
        Console.WriteLine("-rc  : Rearrange channels in output MIDIs so channel 10 is avoided. Needed by sound cards where it's impossible to disable \"drums\" on channel 10 even with GS or XG commands.");
        Console.WriteLine("-xg  : Output MIDI will be compliant to XG standard (instead of default GS standard).");
        Console.WriteLine("-sb  : Separate banks. Every sound bank is riper to a different .sf2 file and placed into different sub-folders (instead of doing it in a single .sf2 file and a single folder).");
        Console.WriteLine("-raw : Output MIDIs exactly as they're encoded in ROM, without linearise volume and velocities and without simulating vibratos.");
        Console.WriteLine("[address]: Force address of the song table manually. This is required for manually dumping music data from ROMs where the location can't be detected automatically.");
    }

    private uint get_GBA_pointer()
    {
        return inGBA.ReadUInt32LittleEndian() - 0x8000000;
    }

    private void parse_args(string[] args)
    {
        if (args.Length < 1)
        {
            print_instructions();
            throw new EnvironmentExitException(0);
        }

        bool path_found = false, song_tbl_found = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i][0] == '-')
            {
                if ((args[i] == "--help"))
                {
                    print_instructions();
                    throw new EnvironmentExitException(0);
                }
                else if ((args[i] == "-gm"))
                    gm = true;
                else if ((args[i] == "-xg"))
                    xg = true;
                else if ((args[i] == "-rc"))
                    rc = true;
                else if ((args[i] == "-sb"))
                    sb = true;
                else if ((args[i] == "-raw"))
                    raw = true;
                else if ((args[i] == "-o") && args.Length >= i + 1)
                {
                    outPath = args[i + 1];
                }
                else
                {
                    Console.Error.WriteLine($"Error: Unknown command line option: {args[i]}. Try with --help to get information.");
                    throw new EnvironmentExitException(-1);
                }
            }
            // Convert given address to binary, use it instead of automatically detected one
            else if (!path_found)
            {
                // Get GBA file
                try
                {
                    inGBA = File.OpenRead(args[i]);
                }
                catch
                {
                    Console.Error.WriteLine($"Error: Can't open file {args[i]} for reading.");
                    throw new EnvironmentExitException(-1);
                }

                // Name is filename without the extention and without path
                inGBA_path = args[i];
                name = Path.GetFileNameWithoutExtension(inGBA_path);

                // Path where the input GBA file is located
                path = Path.GetDirectoryName(inGBA_path)!;
                path_found = true;
            }
            else if (!song_tbl_found)
            {
                if (!InternalUtils.TryParseUIntHD(args[i], out song_tbl_ptr))
                {
                    Console.Error.WriteLine($"Error: {args[i]} is not a valid song table address.");
                    throw new EnvironmentExitException(-1);
                }
                song_tbl_found = true;
            }
            else
            {
                Console.Error.WriteLine($"Error: Don't know what to do with {args[i]}. Try with --help to get more information.");
                throw new EnvironmentExitException(-1);
            }
        }
        if (!path_found)
        {
            Console.Error.WriteLine("Error: No input GBA file. Try with --help to get more information.");
            throw new EnvironmentExitException(-1);
        }
    }

    /// <summary>
    /// Main execution function for song ripper.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>Something.</returns>
    public static int Main(string[] args)
    {
        GbaMusRipper sr = new();
        return sr.MainInternal(args);
    }

    private int MainInternal(string[] args)
    {
        // Parse arguments (without program name)
        parse_args(args);

        outPath ??= ".";

        int sample_rate = 0, main_volume = 0; // Use default values when those are '0'

        // If the user hasn't provided an address manually, we'll try to automatically detect it
        if (song_tbl_ptr == 0)
        {
            // Auto-detect address of sappy engine
            int sound_engine_adr;
            try
            {
                sound_engine_adr = SappyDetector.Main(new[] { inGBA_path });
            }
            catch
            {
                throw new EnvironmentExitException(0);
            }

            try
            {
                inGBA.Position = sound_engine_adr;
            }
            catch
            {
                Console.Error.WriteLine($"Error: Invalid offset within input GBA file: 0x{sound_engine_adr:x}");
                throw new EnvironmentExitException(0);
            }

            // Engine parameter's word
            uint parameter_word = inGBA.ReadUInt32LittleEndian();

            // Get sampling rate
            sample_rate = sample_rates[(parameter_word >> 16) & 0xf];
            main_volume = (int)((parameter_word >> 12) & 0xf);

            // Compute address of song table
            uint song_levels = inGBA.ReadUInt32LittleEndian(); // Read # of song levels
            Console.WriteLine($"# of song levels: {song_levels}");
            song_tbl_ptr = get_GBA_pointer() + 12 * song_levels;
        }

        // Create a directory named like the input ROM, without the .gba extention
        Directory.CreateDirectory(outPath);

        //  Get the size of the input GBA file
        inGBA_size = (int)inGBA.Length;

        if (song_tbl_ptr >= inGBA_size)
        {
            Console.Error.WriteLine($"Fatal error: Song table at 0x{song_tbl_ptr:x} is past the end of the file.");
            throw new EnvironmentExitException(-2);
        }

        Console.WriteLine("Parsing song table...");
        // New list of songs
        List<uint> song_list = new();

        try
        {
            inGBA.Position = song_tbl_ptr;
        }
        catch
        {
            Console.WriteLine($"Fatal error: Can't seek to song table at: 0x{song_tbl_ptr:x}");
            throw new EnvironmentExitException(-3);
        }

        // Ignores entries which are made of 0s at the start of the song table
        // this fix was necessarily for the game Fire Emblem
        uint song_pointer;
        while (true)
        {
            song_pointer = inGBA.ReadUInt32LittleEndian();
            if (song_pointer != 0) break;
            song_tbl_ptr += 4;
        }

        uint i = 0;
        while (true)
        {
            song_pointer -= 0x8000000; // Adjust pointer

            // Stop as soon as we met with an invalid pointer
            if (song_pointer == 0 || song_pointer >= inGBA_size) break;

            for (int j = 4; j != 0; --j) inGBA.ReadUInt8LittleEndian(); // Discard 4 bytes (sound group)
            song_list.Add(song_pointer); // Add pointer to list
            i++;
            song_pointer = inGBA.ReadUInt32LittleEndian();
        }
        ;
        // As soon as data that is not a valid pointer is found, the song table is terminated

        // End of song table
        uint song_tbl_end_ptr = 8 * i + song_tbl_ptr;

        Console.WriteLine("Collecting sound bank list...");

        // New list of sound banks
        SortedSet<uint> sound_bank_set = new();
        Dictionary<uint, uint> sound_bank_src_dict = new();
        for (i = 0; i < song_list.Count; i++)
        {
            // Ignore unused song, which points to the end of the song table (for some reason)
            if (song_list[(int)i] != song_tbl_end_ptr)
            {
                // Seek to song data
                inGBA.Position = song_list[(int)i] + 4;
                if (inGBA.Position > inGBA.Length) continue;
                uint sound_bank_ptr = get_GBA_pointer();

                // Add sound bank to list if not already in the list
                sound_bank_set.Add(sound_bank_ptr);
                sound_bank_src_dict.Add(i, sound_bank_ptr);
            }
        }
        Dictionary<uint, int> sound_bank_dict = sound_bank_set.Select((v, idx) => (v, idx)).ToDictionary(v => v.v, v => v.idx);

        // Close GBA file so that SongRipper can access it
        inGBA.Close();

        // Create directories for each sound bank if separate banks is enabled
        if (sb)
        {
            for (int j = 0; j < sound_bank_set.Count; j++) Directory.CreateDirectory(Path.Combine(outPath, $"soundbank_{j:D4}"));
        }

        for (i = 0; i < song_list.Count; i++)
        {
            if (song_list[(int)i] != song_tbl_end_ptr)
            {
                if (!sound_bank_src_dict.ContainsKey(i)) continue;
                uint bank_index = (uint)sound_bank_dict[sound_bank_src_dict[i]];
                List<string> seqRipCmd = new();
                seqRipCmd.Add(inGBA_path);

                string cippyP = outPath;
                // Add leading zeroes to file name
                if (sb) cippyP = Path.Combine(cippyP, $"soundbank_{bank_index:D4}");
                cippyP = Path.Combine(cippyP, $"song{i:D4}.mid");
                seqRipCmd.Add(cippyP);

                seqRipCmd.Add($"0x{song_list[(int)i]:x}");
                seqRipCmd.Add(rc ? "-rc" : xg ? "-xg" : "-gs");
                if (!raw)
                {
                    seqRipCmd.Add("-sv");
                    seqRipCmd.Add("-lv");
                }
                // Bank number, if banks are not separated
                if (!sb)
                    seqRipCmd.Add($"-b{bank_index}");

                Console.WriteLine($"Song {i}");

                int rcd;
                PrintArgSeq(seqRipCmd);
                try
                {
                    rcd = SongRipper.Main(seqRipCmd.ToArray());
                }
                catch (EnvironmentExitException e)
                {
                    rcd = e.Code;
                }
                if (rcd == 0) Console.WriteLine("An error occurred while calling song_ripper.");
            }
        }

        if (sb)
        {
            // Rips each sound bank in a different file/folder
            int j = 0;
            foreach (uint b in sound_bank_set)
            {
                uint bank_index = (uint)j++;

                string foldername = $"soundbank_{bank_index:D4}";
                List<string> sfRipArgs = new();
                sfRipArgs.Add(inGBA_path);
                string cippyP = Path.Combine(outPath, foldername, foldername + ".sf2");
                sfRipArgs.Add(cippyP);
                if (sample_rate != 0) sfRipArgs.Add($"-s{sample_rate}");
                if (main_volume != 0) sfRipArgs.Add($"-mv{main_volume}");
                if (gm) sfRipArgs.Add("-gm");
                sfRipArgs.Add($"0x{b:x}");

                PrintArgSeq(args);
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
            sfRipArgs.Add(inGBA_path);
            string cippyP = Path.Combine(outPath, name + ".sf2");
            sfRipArgs.Add(cippyP);
            if (sample_rate != 0) sfRipArgs.Add($"-s{sample_rate}");
            if (main_volume != 0) sfRipArgs.Add($"-mv{main_volume}");
            // Pass -gm argument if necessary
            if (gm) sfRipArgs.Add("-gm");

            // Make sound banks addresses list.
            foreach (uint t in sound_bank_set)
                if (t != 0)
                    sfRipArgs.Add($"0x{t:x}");

            // Call sound font ripper
            PrintArgSeq(sfRipArgs);
            try
            {
                SoundFontRipper.Main(sfRipArgs.ToArray());
            }
            catch (EnvironmentExitException)
            {
                // ignored
            }
        }

        Console.WriteLine("Rip completed!");
        return 0;
    }

    private static void PrintArgSeq(IReadOnlyList<string> args)
    {
        StringBuilder sb = new();
        foreach (string arg in args)
            sb.Append(" \"").Append(arg).Append('\"');
        Console.WriteLine($"Call args: {sb}");
    }


    // TODO
}
