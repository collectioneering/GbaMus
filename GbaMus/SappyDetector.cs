using System.Buffers.Binary;

namespace GbaMus;

/// <summary>
/// Provides sappy_detector functionality.
/// </summary>
public static class SappyDetector
{
    private static readonly string[] s_lookup = { "invalid", "5734 Hz", "7884 Hz", "10512 Hz", "13379 Hz", "15768 Hz", "18157 Hz", "21024 Hz", "26758 Hz", "31536 Hz", "36314 Hz", "40137 Hz", "42048 Hz", "invalid", "invalid", "invalid" };

    private static ReadOnlySpan<byte> M4ABinSelectsong => new byte[] { 0x00, 0xB5, 0x00, 0x04, 0x07, 0x4A, 0x08, 0x49, 0x40, 0x0B, 0x40, 0x18, 0x83, 0x88, 0x59, 0x00, 0xC9, 0x18, 0x89, 0x00, 0x89, 0x18, 0x0A, 0x68, 0x01, 0x68, 0x10, 0x1C, 0x00, 0xF0, };

    // we need to check 2 tables because the m4a library was recompiled for some games.
    private static ReadOnlySpan<byte> M4ABinSelectsongNew => new byte[] { 0x00, 0xB5, 0x00, 0x04, 0x07, 0x4B, 0x08, 0x49, 0x40, 0x0B, 0x40, 0x18, 0x82, 0x88, 0x51, 0x00, 0x89, 0x18, 0x89, 0x00, 0xC9, 0x18, 0x0A, 0x68, 0x01, 0x68, 0x10, 0x1C, 0x00, 0xF0, };

    private const int M4A_MAIN_PATT_COUNT = 1;
    private const int M4A_MAIN_LEN = 2;
    private static ReadOnlySpan<byte> M4ABinMain => new byte[] { 0x00, 0xB5 };

    private const int M4A_INIT_PATT_COUNT = 2;
    private const int M4A_INIT_LEN = 2;

    private static unsafe long memsearch(ReadOnlySpan<byte> destination, ReadOnlySpan<byte> source, int dst_offset, int alignment, int diff_threshold)
    {
        int dstsize = destination.Length, srcsize = source.Length;
        fixed (byte* dst = destination, src = source)
        {
            if (alignment == 0)
            {
                return -1;
            }

            // alignment
            if (dst_offset % alignment != 0)
            {
                dst_offset += alignment - (dst_offset % alignment);
            }

            for (int offset = dst_offset; (offset + srcsize) <= dstsize; offset += alignment)
            {
                // memcmp(&dst[offset], src, srcsize)
                int diff = 0;
                for (int i = 0; i < srcsize; i++)
                {
                    if (dst[offset + i] != src[i])
                    {
                        diff++;
                    }
                    if (diff > diff_threshold)
                    {
                        break;
                    }
                }
                if (diff <= diff_threshold)
                {
                    return offset;
                }
            }
            return -1;
        }
    }

    private static bool is_valid_offset(uint offset, uint romsize)
    {
        return offset < romsize;
    }

    private static bool is_gba_rom_address(uint address)
    {
        byte region = (byte)((address >> 24) & 0xFE);
        return region == 8 || region == 9;
    }

    private static uint gba_address_to_offset(uint address)
    {
        if (!is_gba_rom_address(address))
        {
            Console.WriteLine($"Warning: the address {address} is not a valid ROM address.\n");
        }
        return address & 0x01FFFFFF;
    }

/* Thanks to loveeemu for this routine, more accurate than mine ! Slightly adapted. */
    private const int M4A_OFFSET_SONGTABLE = 40;

    private static long m4a_searchblock(ReadOnlySpan<byte> gbarom)
    {
        long m4a_selectsong_offset = -1;
        long m4a_main_offset = -1;

        long m4a_selectsong_search_offset = 0;
        while (m4a_selectsong_search_offset != -1)
        {
            m4a_selectsong_offset = memsearch(gbarom, M4ABinSelectsong, (int)m4a_selectsong_search_offset, 1, 0);

            if (m4a_selectsong_offset == -1)
            {
                // we didn't find the first library, so attempt to find the newer m4a library.
                m4a_selectsong_offset = memsearch(gbarom, M4ABinSelectsongNew, (int)m4a_selectsong_search_offset, 1, 0);
            }

            if (m4a_selectsong_offset != -1)
            {
#if DEBUG
                Console.WriteLine($"Selectsong candidate: {m4a_selectsong_offset:x8}");
#endif

                // obtain song table address
                uint m4a_songtable_address = BinaryPrimitives.ReadUInt32LittleEndian(gbarom.Slice((int)(m4a_selectsong_offset + M4A_OFFSET_SONGTABLE)));
                if (!is_gba_rom_address(m4a_songtable_address))
                {
#if DEBUG
                    Console.WriteLine($"Song table address error: not a ROM address {m4a_songtable_address:x8}");
#endif
                    m4a_selectsong_search_offset = m4a_selectsong_offset + 1;
                    continue;
                }
                uint m4a_songtable_offset_tmp = gba_address_to_offset(m4a_songtable_address);
                if (!is_valid_offset(m4a_songtable_offset_tmp + 4 - 1, (uint)gbarom.Length))
                {
#if DEBUG
                    Console.WriteLine($"Song table address error: address out of range {m4a_songtable_address:x8}");
#endif
                    m4a_selectsong_search_offset = m4a_selectsong_offset + 1;
                    continue;
                }

                // song table must have more than one song
                int validsongcount = 0;
                for (int songindex = 0; validsongcount < 1; songindex++)
                {
                    uint songaddroffset = (uint)(m4a_songtable_offset_tmp + (songindex * 8));
                    if (!is_valid_offset(songaddroffset + 4 - 1, (uint)gbarom.Length))
                    {
                        break;
                    }

                    uint songaddr = BinaryPrimitives.ReadUInt32LittleEndian(gbarom.Slice((int)songaddroffset));
                    if (songaddr == 0)
                    {
                        continue;
                    }

                    if (!is_gba_rom_address(songaddr))
                    {
#if DEBUG
                        Console.WriteLine($"Song address error: not a ROM address {songaddr:x8}");
#endif
                        break;
                    }
                    if (!is_valid_offset(gba_address_to_offset(songaddr) + 4 - 1, (uint)gbarom.Length))
                    {
#if DEBUG
                        Console.WriteLine($"Song address error: address out of range {songaddr:x8}");
#endif
                        break;
                    }
                    validsongcount++;
                }
                if (validsongcount < 1)
                {
                    m4a_selectsong_search_offset = m4a_selectsong_offset + 1;
                    continue;
                }
                break;
            }
            else
            {
                m4a_selectsong_search_offset = -1;
            }
        }
        if (m4a_selectsong_offset == -1)
        {
            return -1;
        }

        uint m4a_main_offset_tmp = (uint)m4a_selectsong_offset;
        if (!is_valid_offset(m4a_main_offset_tmp + M4A_MAIN_LEN - 1, (uint)gbarom.Length))
        {
            return -1;
        }
        while (m4a_main_offset_tmp > 0 && m4a_main_offset_tmp > (uint)m4a_selectsong_offset - 0x20)
        {
            for (int mainpattern = 0; mainpattern < M4A_MAIN_PATT_COUNT; mainpattern++)
            {
                if (gbarom.Slice((int)m4a_main_offset_tmp, M4A_INIT_LEN).SequenceEqual(M4ABinMain.Slice(mainpattern * M4A_MAIN_LEN, M4A_INIT_LEN)))
                {
                    m4a_main_offset = m4a_main_offset_tmp;
                    break;
                }
            }
            m4a_main_offset_tmp--;
        }
        return m4a_main_offset;
    }

    /// <summary>
    /// Sound engine parameters.
    /// </summary>
    public struct SoundEngineParam
    {
        /// <summary>
        /// Polyphony.
        /// </summary>
        public uint Polyphony;

        /// <summary>
        /// Main volume.
        /// </summary>
        public uint MainVol;

        /// <summary>
        /// Sampling rate index.
        /// </summary>
        public uint SamplingRateIndex;

        /// <summary>
        /// DAC bits.
        /// </summary>
        public uint DacBits;

        /// <summary>
        /// Initializes a new instance of <see cref="SoundEngineParam"/>.
        /// </summary>
        /// <param name="data">Data.</param>
        public SoundEngineParam(uint data)
        {
            Polyphony = (data & 0x000F00) >> 8;
            MainVol = (data & 0x00F000) >> 12;
            SamplingRateIndex = (data & 0x0F0000) >> 16;
            DacBits = 17 - ((data & 0xF00000) >> 20);
        }
    }

    // Test if an area of ROM is eligible to be the base pointer
    private static bool test_pointer_validity(ReadOnlySpan<byte> data)
    {
        SoundEngineParam p = new SoundEngineParam(BinaryPrimitives.ReadUInt32LittleEndian(data));

        /* Compute (supposed ?) address of song table */
        uint song_tbl_adr = (BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8)) & 0x3FFFFFF) + 12 * BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));

        /* Prevent illegal values for all fields */
        return p.MainVol != 0
               && p.Polyphony <= 12
               && p.DacBits >= 6
               && p.DacBits <= 9
               && p.SamplingRateIndex >= 1
               && p.SamplingRateIndex <= 12
               && song_tbl_adr < data.Length
               && BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4)) < 256
               && ((data[0] & 0xff000000) == 0);
    }


    static void print_instructions()
    {
        Console.WriteLine("GBA Sappy Engine Detector (c) 2015 by Bregalad and loveemu");
        Console.WriteLine("Usage: sappy_detector game.gba");
        Console.WriteLine();
    }

    /// <summary>
    /// Main execution function for sappy detector.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>Nonzero for sappy info position.</returns>
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            print_instructions();
            return 0;
        }
        Console.WriteLine("Sappy sound engine detector (c) 2015 by Bregalad and loveemu");
        Console.WriteLine();

        byte[] inGBA;
        try
        {
            inGBA = File.ReadAllBytes(args[0]);
        }
        catch
        {
            Console.Error.WriteLine($"Error: File {args[0]} can't be opened for reading.");
            return 0;
        }

        long offset = m4a_searchblock(inGBA);

        if (offset < 0)
        {
            /* If no address were told manually and nothing was detected.... */
            Console.WriteLine("No sound engine was found.");
            return 0;
        }
        Console.WriteLine($"Sound engine detected at offset 0x{offset:x}");

        /* Test validity of engine offset with -16 and -32 */
        bool valid_m16 = test_pointer_validity(inGBA.AsSpan((int)(offset - 16)));
        bool valid_m32 = test_pointer_validity(inGBA.AsSpan((int)(offset - 32)));

        /* If neither is found there is an error */
        if (!valid_m16 && !valid_m32)
        {
            Console.WriteLine("Only a partial sound engine was found.");
            return 0;
        }
        offset -= valid_m16 ? 16 : 32;

        Span<byte> data = inGBA.AsSpan((int)offset);
        uint song_tbl_adr = (BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8)) & 0x3FFFFFF) + 12 * BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
        SoundEngineParam p = new(BinaryPrimitives.ReadUInt32LittleEndian(data));

        //Read # of song levels
        Console.WriteLine($"# of song levels: {BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4))}");

        // At this point we can be certain we detected the real thing.
        Console.WriteLine("Engine parameters:");
        Console.WriteLine($"Main Volume: {p.MainVol} Polyphony: {p.Polyphony} channels, Dac: {17 - p.DacBits} bits, Sampling rate: {s_lookup[p.SamplingRateIndex]}");
        Console.WriteLine($"Song table located at: 0x{song_tbl_adr:x}");
        /* Return the offset of sappy info to the operating system */
        return (int)offset;
    }
}
