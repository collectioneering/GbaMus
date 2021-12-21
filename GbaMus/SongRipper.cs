using System.Text;

namespace GbaMus;

/// <summary>
/// Provides song_ripper functionality.
/// </summary>
public class SongRipper
{
    private class Note
    {
        private SongRipper _songRipper;
        private Midi midi;
        private int counter;
        private int key;
        private int vel;
        private int chn;
        private bool event_made;

        // Tick counter, if it becomes zero
        // then create key off event
        // this function returns "true" when the note should be freed from memory
        private bool tick()
        {
            if (counter <= 0 || --counter != 0) return false;
            midi.AddNoteOff(chn, (byte)key, (byte)vel);
            _songRipper.stop_lfo(chn);
            _songRipper.simultaneous_notes_ctr--;
            return true;
        }

        // Create note and key on event
        public Note(SongRipper songRipper, Midi midi, int chn, int len, int key, int vel)
        {
            _songRipper = songRipper;
            this.midi = midi;
            this.chn = chn;
            counter = len;
            this.key = key;
            this.vel = vel;
            event_made = false;

            songRipper.start_lfo(chn);
            songRipper.add_simultaneous_note();
        }


        internal bool countdown_is_over()
        {
            return tick() || counter < 0;
        }

        internal void make_note_on_event()
        {
            if (event_made) return;
            midi.AddNoteOn(chn, (byte)key, (byte)vel);
            event_made = true;
        }
    }

    private uint[] track_ptr = new uint[16];
    private byte[] last_cmd = new byte[16];
    private byte[] last_key = new byte[16];
    private byte[] last_vel = new byte[16];
    private int[] counter = new int[16];
    private uint[] return_ptr = new uint[16];
    private int[] key_shift = new int[16];
    private bool[] return_flag = new bool[16];
    private bool[] track_completed = new bool[16];
    static bool end_flag = false;
    static bool loop_flag = false;
    private uint loop_adr;

    private int[] lfo_delay_ctr = new int[16];
    private int[] lfo_delay = new int[16];
    private int[] lfo_depth = new int[16];
    private int[] lfo_type = new int[16];
    private bool[] lfo_flag = new bool[16];
    private bool[] lfo_hack = new bool[16];

    private uint simultaneous_notes_ctr = 0;
    private uint simultaneous_notes_max = 0;

    private List<Note> notes_playing;

    private int bank_number;
    private bool bank_used = false;
    private bool rc = false;
    private bool gs = false;
    private bool xg = false;
    private bool lv = false;
    private bool sv = false;

    private Midi midi;
    private Stream inGBA;

    private SongRipper()
    {
        notes_playing = new List<Note>();
        midi = new Midi(24);
        inGBA = null!;
    }

    private static void print_instructions()
    {
        Console.WriteLine("Rips sequence data from a GBA game using Sappy sound engine to MIDI (.mid) format.");
        Console.WriteLine();
        Console.WriteLine("Usage: song_riper infile.gba outfile.mid song_address [-b1 -gm -gs -xg]");
        Console.WriteLine("-b : Bank: forces all patches to be in the specified bank (0-127).");
        Console.WriteLine("In General MIDI, channel 10 is reserved for drums.");
        Console.WriteLine("Unfortunately, we do not want to use any \"drums\" in the output file.");
        Console.WriteLine("I have 3 modes to fix this problem.");
        Console.WriteLine("-rc : Rearrange Channels. This will avoid using the channel 10, and use it at last ressort only if all 16 channels should be used");
        Console.WriteLine("-gs : This will send a GS system exclusive message to tell the player channel 10 is not drums.");
        Console.WriteLine("-xg : This will send a XG system exclusive message, and force banks number which will disable \"drums\".");
        Console.WriteLine("-lv : Linearise volume and velocities. This should be used to have the output \"sound\" like the original song, but shouldn't be used to get an exact dump of sequence data.");
        Console.WriteLine("-sv : Simulate vibrato. This will insert controllers in real time to simulate a vibrato, instead of just when commands are given. Like -lv, this should be used to have the output \"sound\" like the original song, but shouldn't be used to get an exact dump of sequence data.");
        Console.WriteLine();
        Console.WriteLine("It is possible, but not recommended, to use more than one of these flags at a time.");
    }


    private void add_simultaneous_note()
    {
        // Update simultaneous notes max.
        if (++simultaneous_notes_ctr > simultaneous_notes_max)
            simultaneous_notes_max = simultaneous_notes_ctr;
    }

    // LFO logic on tick
    private void process_lfo(int track)
    {
        if (sv && lfo_delay_ctr[track] != 0)
        {
            // Decrease counter if it's value was nonzero
            if (--lfo_delay_ctr[track] == 0)
            {
                // If 1->0 transition we need to add a signal to start the LFO
                if (lfo_type[track] == 0)
                    // Send a controller 1 if pitch LFO
                    midi.AddController(track, 1, (byte)(lfo_depth[track] < 16 ? lfo_depth[track] * 8 : 127));
                else
                    // Send a channel aftertouch otherwise
                    midi.AddChanaft(track, (byte)(lfo_depth[track] < 16 ? lfo_depth[track] * 8 : 127));
                lfo_flag[track] = true;
            }
        }
    }

    private void start_lfo(int track)
    {
        // Reset down delay counter to its initial value
        if (sv && lfo_delay[track] != 0)
            lfo_delay_ctr[track] = lfo_delay[track];
    }

    private void stop_lfo(int track)
    {
        // Cancel a LFO if it was playing,
        if (sv && lfo_flag[track])
        {
            if (lfo_type[track] == 0)
                midi.AddController(track, 1, 0);
            else
                midi.AddChanaft(track, 0);
            lfo_flag[track] = false;
        }
        else
            lfo_delay_ctr[track] = 0; // cancel delay counter if it wasn't playing
    }

    private bool tick(int track_amnt)
    {
        // Tick all playing notes, and remove notes which
        // have been keyed off OR which are infinite length from the list
        notes_playing.RemoveAll(v => v.countdown_is_over());

        // Process all tracks
        for (int track = 0; track < track_amnt; track++)
        {
            counter[track]--;
            // Process events until counter non-null or pointer null
            // This might not be executed if counter both are non null.
            while (track_ptr[track] != 0 && !end_flag && counter[track] <= 0)
            {
                // Check if we're at loop start point
                if (track == 0 && loop_flag && !return_flag[0] && !track_completed[0] && track_ptr[0] == loop_adr)
                    midi.AddMarker(Encoding.ASCII.GetBytes("loopStart"));

                process_event(track);
            }
        }

        for (int track = 0; track < track_amnt; track++)
        {
            process_lfo(track);
        }

        // Compute if all still active channels are completely decoded
        bool all_completed_flag = true;
        for (int i = 0; i < track_amnt; i++)
            all_completed_flag &= track_completed[i];

        // If everything is completed, the main program should quit its loop
        if (all_completed_flag) return false;

        // Make note on events for this tick
        //(it's important they are made after all other events)
        foreach (var p in notes_playing)
            p.make_note_on_event();

        // Increment MIDI time
        midi.Clock();
        return true;
    }

    private uint get_GBA_pointer()
    {
        return inGBA.ReadUInt32LittleEndian() & 0x3FFFFFF;
    }

    // Length table for notes and rests
    private static readonly int[] lenTbl = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 28, 30, 32, 36, 40, 42, 44, 48, 52, 54, 56, 60, 64, 66, 68, 72, 76, 78, 80, 84, 88, 90, 92, 96 };

    private void process_event(int track)
    {
        inGBA.Position = track_ptr[track];
        // Read command
        byte command = inGBA.ReadUInt8LittleEndian();

        track_ptr[track]++;
        byte arg1;
        // Repeat last command, the byte read was in fact the first argument
        if (command < 0x80)
        {
            arg1 = command;
            command = last_cmd[track];
        }

        // Delta time command
        else if (command <= 0xb0)
        {
            counter[track] = lenTbl[command - 0x80];
            return;
        }

        // End track command
        else if (command == 0xb1)
        {
            // Null pointer
            track_ptr[track] = 0;
            track_completed[track] = true;
            return;
        }

        // Jump command
        else if (command == 0xb2)
        {
            track_ptr[track] = get_GBA_pointer();

            // detect the end track
            track_completed[track] = true;
            return;
        }

        // Call command
        else if (command == 0xb3)
        {
            uint addr = get_GBA_pointer();

            // Return address for the track
            return_ptr[track] = track_ptr[track] + 4;
            // Now points to called address
            track_ptr[track] = addr;
            return_flag[track] = true;
            return;
        }

        // Return command
        else if (command == 0xb4)
        {
            if (return_flag[track])
            {
                track_ptr[track] = return_ptr[track];
                return_flag[track] = false;
            }
            return;
        }

        // Tempo change
        else if (command == 0xbb)
        {
            int tempo = 2 * inGBA.ReadUInt8LittleEndian();
            track_ptr[track]++;
            midi.AddTempo(tempo);
            return;
        }

        else
        {
            // Normal command
            last_cmd[track] = command;
            // Need argument
            arg1 = inGBA.ReadUInt8LittleEndian();
            track_ptr[track]++;
        }

        // Note on with specified length command
        if (command >= 0xd0)
        {
            int key, vel, len_ofs = 0;
            // Is arg1 a key value ?
            if (arg1 < 0x80)
            {
                // Yes -> use new key value
                key = arg1;
                last_key[track] = (byte)key;

                byte arg2 = inGBA.ReadUInt8LittleEndian();
                // Is arg2 a velocity ?
                byte arg3 = (inGBA.ReadUInt8LittleEndian());
                if (arg2 < 0x80)
                {
                    // Yes -> use new velocity value
                    vel = arg2;
                    last_vel[track] = (byte)vel;
                    track_ptr[track]++;

                    // Is there a length offset ?
                    if (arg3 < 0x80)
                    {
                        // Yes -> read it and increment pointer
                        len_ofs = arg3;
                        track_ptr[track]++;
                    }
                }
                else
                {
                    // No -> use previous velocity value
                    vel = last_vel[track];
                }
            }
            else
            {
                // No -> use last value
                key = last_key[track];
                vel = last_vel[track];
                track_ptr[track]--; // Seek back, as arg 1 is unused and belong to next event !
            }

            // Linearise velocity if needed
            if (lv) vel = (int)Math.Sqrt(127.0 * vel);

            notes_playing.Insert(0, new Note(this, midi, track, lenTbl[command - 0xd0 + 1] + len_ofs, key + key_shift[track], vel));
            return;
        }

        // Other commands
        switch (command)
        {
            // Key shift
            case 0xbc:
                key_shift[track] = arg1;
                return;

            // Set instrument
            case 0xbd:
                if (bank_used)
                {
                    if (!xg)
                        midi.AddController(track, 0, (byte)bank_number);
                    else
                    {
                        midi.AddController(track, 0, (byte)(bank_number >> 7));
                        midi.AddController(track, 32, (byte)(bank_number & 0x7f));
                    }
                }
                midi.AddPChange(track, arg1);
                return;

            // Set volume
            case 0xbe:
                {
                    // Linearise volume if needed
                    int volume = lv ? (int)Math.Sqrt(127.0 * arg1) : arg1;
                    midi.AddController(track, 7, (byte)volume);
                }
                return;

            // Set panning
            case 0xbf:
                midi.AddController(track, 10, arg1);
                return;

            // Pitch bend
            case 0xc0:
                midi.AddPitchBend(track, arg1);
                return;

            // Pitch bend range
            case 0xc1:
                if (sv)
                    midi.AddRpn(track, 0, arg1);
                else
                    midi.AddController(track, 20, arg1);
                return;

            // LFO Speed
            case 0xc2:
                if (sv)
                    midi.AddNrpn(track, 136, arg1);
                else
                    midi.AddController(track, 21, arg1);
                return;

            // LFO delay
            case 0xc3:
                if (sv)
                    lfo_delay[track] = arg1;
                else
                    midi.AddController(track, 26, arg1);
                return;

            // LFO depth
            case 0xc4:
                if (sv)
                {
                    if (lfo_delay[track] == 0 && lfo_hack[track])
                    {
                        if (lfo_type[track] == 0)
                            midi.AddController(track, 1, (byte)(arg1 > 12 ? 127 : 10 * arg1));
                        else
                            midi.AddChanaft(track, (byte)(arg1 > 12 ? 127 : 10 * arg1));

                        lfo_flag[track] = true;
                    }
                    lfo_depth[track] = arg1;
                    // I had a stupid bug with LFO inserting controllers I didn't want at the start of files
                    // So I made a terrible quick fix for it, in the mean time I can find something better to prevent it.
                    lfo_hack[track] = true;
                }
                else
                    midi.AddController(track, 1, arg1);
                return;

            // LFO type
            case 0xc5:
                if (sv)
                    lfo_type[track] = arg1;
                else
                    midi.AddController(track, 22, arg1);
                return;

            // Detune
            case 0xc8:
                if (sv)
                    midi.AddRpn(track, 1, arg1);
                else
                    midi.AddController(track, 24, arg1);
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
                        last_key[track] = (byte)key;
                    }
                    else
                    {
                        // No -> use last value
                        key = last_key[track];
                        vel = last_vel[track];
                        track_ptr[track]--; // Seek back, as arg 1 is unused and belong to next event !
                    }

                    midi.AddNoteOff(track, (byte)(key + key_shift[track]), (byte)vel);
                    stop_lfo(track);
                    simultaneous_notes_ctr--;
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
                        last_key[track] = (byte)key;

                        byte arg2 = inGBA.ReadUInt8LittleEndian();
                        // Is arg2 a velocity ?
                        if (arg2 < 0x80)
                        {
                            // Yes -> use new velocity value
                            vel = arg2;
                            last_vel[track] = (byte)vel;
                            track_ptr[track]++;
                        }
                        else // No -> use previous velocity value
                            vel = last_vel[track];
                    }
                    else
                    {
                        // No -> use last value
                        key = last_key[track];
                        vel = last_vel[track];
                        track_ptr[track]--; // Seek back, as arg 1 is unused and belong to next event !
                    }
                    // Linearise velocity if needed
                    if (lv) vel = (int)Math.Sqrt(127.0 * vel);

                    // Make note of infinite length
                    notes_playing.Insert(0, new Note(this, midi, track, -1, key + key_shift[track], vel));
                }
                return;
        }
    }


    private uint parseArguments(string[] args)
    {
        if (args.Length < 3) print_instructions();

        // Open the input and output files
        try
        {
            inGBA = File.OpenRead(args[0]);
        }
        catch
        {
            Console.Error.WriteLine($"Can't open file {args[0]} for reading.");
            throw new EnvironmentExitException(0);
        }

        for (int i = 3; i < args.Length; i++)
        {
            if (args[i][0] == '-')
            {
                if (args[i][1] == 'b')
                {
                    if (args[i].Length < 3) print_instructions();
                    bank_number = int.Parse(args[i].Substring(2));
                    bank_used = true;
                }
                else if (args[i][1] == 'r' && args[i][2] == 'c')
                    rc = true;
                else if (args[i][1] == 'g' && args[i][2] == 's')
                    gs = true;
                else if (args[i][1] == 'x' && args[i][2] == 'g')
                    xg = true;
                else if (args[i][1] == 'l' && args[i][2] == 'v')
                    lv = true;
                else if (args[i][1] == 's' && args[i][2] == 'v')
                    sv = true;
                else
                    print_instructions();
            }
            else
                print_instructions();
        }
        // Return base address, parsed correctly in both decimal and hex
        if (!InternalUtils.TryParseUIntHD(args[2], out uint baseAddress))
        {
            Console.WriteLine("Failed to parse base address");
            throw new EnvironmentExitException(0);
        }
        return baseAddress;
    }


    // Part 10 to normal
    private static readonly byte[] gs_reset_sysex = { 0x41, 0x10, 0x42, 0x12, 0x40, 0x00, 0x7f, 0x00, 0x41 };
    private static readonly byte[] part_10_normal_sysex = { 0x41, 0x10, 0x42, 0x12, 0x40, 0x10, 0x15, 0x00, 0x1b };
    private static readonly byte[] xg_sysex = { 0x43, 0x10, 0x4C, 0x00, 0x00, 0x7E, 0x00 };

    /// <summary>
    /// Main execution function for song ripper.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>Something.</returns>
    public static int Main(string[] args)
    {
        SongRipper sr = new();
        return sr.MainInternal(args);
    }

    private int MainInternal(string[] args)
    {
        Console.Write("GBA ROM sequence ripper (c) 2012 Bregalad");
        uint base_address = parseArguments(args);

        try
        {
            inGBA.Position = base_address;
        }
        catch
        {
            Console.Error.WriteLine($"Can't seek to the base address 0x{base_address:x}.");
            return 0;
        }

        int track_amnt = inGBA.ReadUInt8LittleEndian();
        if (track_amnt < 1 || track_amnt > 16)
        {
            Console.Error.WriteLine($"Invalid amount of tracks {track_amnt}! (must be 1-16).");
            return 0;
        }
        Console.WriteLine($"{track_amnt} tracks.");

        // Open output file once we know the pointer points to correct data
        //(this avoids creating blank files when there is an error)
        Stream outMID;
        try
        {
            outMID = File.Create(args[1]);
        }
        catch
        {
            Console.Error.WriteLine($"Can't write to file {args[1]}.");
            return 0;
        }

        Console.WriteLine("Converting...");

        if (rc)
        {
            // Make the drum channel last in the list, hopefully reducing the risk of it being used
            midi.chanReorder[9] = 15;
            for (uint j = 10; j < 16; ++j)
                midi.chanReorder[j] = (byte)(j - 1);
        }

        if (gs)
        {
            // GS reset
            midi.AddSysex(gs_reset_sysex);
            midi.AddSysex(part_10_normal_sysex);
        }

        if (xg)
        {
            // XG reset
            midi.AddSysex(xg_sysex);
        }

        midi.AddMarker(Encoding.ASCII.GetBytes("Converted by SequenceRipper 2.0"));

        inGBA.ReadUInt8LittleEndian(); // Unknown byte
        inGBA.ReadUInt8LittleEndian(); // Priority
        sbyte reverb = inGBA.ReadInt8LittleEndian(); // Reverb

        int instr_bank_address = (int)get_GBA_pointer();

        // Read table of pointers
        for (int i = 0; i < track_amnt; i++)
        {
            track_ptr[i] = get_GBA_pointer();

            lfo_depth[i] = 0;
            lfo_delay[i] = 0;
            lfo_flag[i] = false;

            if (reverb < 0) // add reverb controller on all tracks
                midi.AddController(i, 91, (byte)(lv ? (int)Math.Sqrt((reverb & 0x7f) * 127.0) : reverb & 0x7f));
        }

        // Search for loop address of track #0
        if (track_amnt > 1) // If 2 or more track, end of track is before start of track 2
            inGBA.Position = track_ptr[1] - 9;
        else
            // If only a single track, the end is before start of header data
            inGBA.Position = base_address - 9;

        // Read where in track 1 the loop starts
        for (int i = 0; i < 5; i++)
            if (inGBA.ReadUInt8LittleEndian() == 0xb2)
            {
                loop_flag = true;
                loop_adr = get_GBA_pointer();
                break;
            }

        // This is the main loop which will process all channels
        // until they are all inactive
        int x = 100000;
        while (tick(track_amnt))
        {
            if (x-- == 0)
            {
                // Security thing to avoid infinite loop in case things goes wrong
                Console.Write("Time out!");
                break;
            }
        }

        // If a loop was detected this is its end
        if (loop_flag) midi.AddMarker(Encoding.ASCII.GetBytes("loopEnd"));

        Console.WriteLine($" Maximum simultaneous notes: {simultaneous_notes_max}");

        Console.Write("Dump complete. Now outputting MIDI file...");
        midi.Write(outMID);
        // Close files
        inGBA.Dispose();
        outMID.Dispose();
        Console.WriteLine(" Done!");
        return instr_bank_address;
    }
}
