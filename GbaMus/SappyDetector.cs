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

    private const int M4AMainPattCount = 1;
    private const int M4AMainLen = 2;
    private static ReadOnlySpan<byte> M4ABinMain => new byte[] { 0x00, 0xB5 };

    //private const int M4AInitPattCount = 2;
    private const int M4AInitLen = 2;

    private static unsafe long Memsearch(ReadOnlySpan<byte> destination, ReadOnlySpan<byte> source, int dstOffset, int alignment, int diffThreshold)
    {
        int dstsize = destination.Length, srcsize = source.Length;
        fixed (byte* dst = destination, src = source)
        {
            if (alignment == 0)
            {
                return -1;
            }

            // alignment
            if (dstOffset % alignment != 0)
            {
                dstOffset += alignment - dstOffset % alignment;
            }

            for (int offset = dstOffset; offset + srcsize <= dstsize; offset += alignment)
            {
                // memcmp(&dst[offset], src, srcsize)
                int diff = 0;
                for (int i = 0; i < srcsize; i++)
                {
                    if (dst[offset + i] != src[i])
                    {
                        diff++;
                    }
                    if (diff > diffThreshold)
                    {
                        break;
                    }
                }
                if (diff <= diffThreshold)
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
        return region is 8 or 9;
    }

    private static uint gba_address_to_offset(uint address, TextWriter? textWriter)
    {
        if (!is_gba_rom_address(address))
        {
            textWriter?.WriteLine($"Warning: the address {address} is not a valid ROM address.\n");
        }
        return address & 0x01FFFFFF;
    }

/* Thanks to loveeemu for this routine, more accurate than mine ! Slightly adapted. */
    private const int M4AOffsetSongtable = 40;

    private static long M4ASearchBlock(ReadOnlySpan<byte> gbarom, TextWriter? textWriter)
    {
        long m4ASelectsongOffset = -1;
        long m4AMainOffset = -1;

        long m4ASelectsongSearchOffset = 0;
        while (m4ASelectsongSearchOffset != -1)
        {
            m4ASelectsongOffset = Memsearch(gbarom, M4ABinSelectsong, (int)m4ASelectsongSearchOffset, 1, 0);

            if (m4ASelectsongOffset == -1)
            {
                // we didn't find the first library, so attempt to find the newer m4a library.
                m4ASelectsongOffset = Memsearch(gbarom, M4ABinSelectsongNew, (int)m4ASelectsongSearchOffset, 1, 0);
            }

            if (m4ASelectsongOffset != -1)
            {
                textWriter?.WriteLine($"Selectsong candidate: 0x{m4ASelectsongOffset:x8}");

                // obtain song table address
                uint m4ASongtableAddress = BinaryPrimitives.ReadUInt32LittleEndian(gbarom.Slice((int)(m4ASelectsongOffset + M4AOffsetSongtable)));
                if (!is_gba_rom_address(m4ASongtableAddress))
                {
                    textWriter?.WriteLine($"Song table address error: not a ROM address 0x{m4ASongtableAddress:x8}");
                    m4ASelectsongSearchOffset = m4ASelectsongOffset + 1;
                    continue;
                }
                uint m4ASongtableOffsetTmp = gba_address_to_offset(m4ASongtableAddress, textWriter);
                if (!is_valid_offset(m4ASongtableOffsetTmp + 4 - 1, (uint)gbarom.Length))
                {
                    textWriter?.WriteLine($"Song table address error: address out of range 0x{m4ASongtableAddress:x8}");
                    m4ASelectsongSearchOffset = m4ASelectsongOffset + 1;
                    continue;
                }

                // song table must have more than one song
                int validsongcount = 0;
                for (int songindex = 0; validsongcount < 1; songindex++)
                {
                    uint songaddroffset = (uint)(m4ASongtableOffsetTmp + songindex * 8);
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
                        textWriter?.WriteLine($"Song address error: not a ROM address 0x{songaddr:x8}");
                        break;
                    }
                    if (!is_valid_offset(gba_address_to_offset(songaddr, textWriter) + 4 - 1, (uint)gbarom.Length))
                    {
                        textWriter?.WriteLine($"Song address error: address out of range 0x{songaddr:x8}");
                        break;
                    }
                    validsongcount++;
                }
                if (validsongcount < 1)
                {
                    m4ASelectsongSearchOffset = m4ASelectsongOffset + 1;
                    continue;
                }
                break;
            }
            else
            {
                m4ASelectsongSearchOffset = -1;
            }
        }
        if (m4ASelectsongOffset == -1)
        {
            return -1;
        }

        uint m4AMainOffsetTmp = (uint)m4ASelectsongOffset;
        if (!is_valid_offset(m4AMainOffsetTmp + M4AMainLen - 1, (uint)gbarom.Length))
        {
            return -1;
        }
        while (m4AMainOffsetTmp > 0 && m4AMainOffsetTmp > (uint)m4ASelectsongOffset - 0x20)
        {
            for (int mainpattern = 0; mainpattern < M4AMainPattCount; mainpattern++)
            {
                if (gbarom.Slice((int)m4AMainOffsetTmp, M4AInitLen).SequenceEqual(M4ABinMain.Slice(mainpattern * M4AMainLen, M4AInitLen)))
                {
                    m4AMainOffset = m4AMainOffsetTmp;
                    break;
                }
            }
            m4AMainOffsetTmp--;
        }
        return m4AMainOffset;
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
    private static bool TestPointerValidity(ReadOnlySpan<byte> data)
    {
        SoundEngineParam p = new SoundEngineParam(BinaryPrimitives.ReadUInt32LittleEndian(data));

        /* Compute (supposed ?) address of song table */
        uint songTblAdr = (BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8)) & 0x3FFFFFF) + 12 * BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));

        /* Prevent illegal values for all fields */
        return p.MainVol != 0
               && p.Polyphony <= 12
               && p.DacBits >= 6
               && p.DacBits <= 9
               && p.SamplingRateIndex >= 1
               && p.SamplingRateIndex <= 12
               && songTblAdr < data.Length
               && BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4)) < 256
               && (data[0] & 0xff000000) == 0;
    }


    private static void PrintInstructions(TextWriter? textWriter)
    {
        if (textWriter == null) return;
        textWriter.WriteLine("GBA Sappy Engine Detector (c) 2015 by Bregalad and loveemu");
        textWriter.WriteLine("Usage: sappy_detector game.gba");
        textWriter.WriteLine();
    }

    /// <summary>
    /// Main execution function for sappy detector.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>Nonzero for sappy info position.</returns>
    public static int Main(string[] args)
    {
        ToolSettings settings = new(Console.Out, Console.Error);
        if (args.Length != 1)
        {
            PrintInstructions(settings.Debug);
            return 0;
        }
        settings.Debug?.WriteLine("Sappy sound engine detector (c) 2015 by Bregalad and loveemu");
        settings.Debug?.WriteLine();

        byte[] buf;
        try
        {
            buf = File.ReadAllBytes(args[0]);
        }
        catch
        {
            settings.Error?.WriteLine($"Error: File {args[0]} can't be opened for reading.");
            return 0;
        }
        return Find(buf, settings);
    }

    /// <summary>
    /// Finds offset of sappy data.
    /// </summary>
    /// <param name="buffer">Buffer to search.</param>
    /// <param name="settings">Settings.</param>
    /// <returns>Found offset, or 0.</returns>
    public static int Find(ReadOnlySpan<byte> buffer, ToolSettings settings)
    {
        long offset = M4ASearchBlock(buffer, settings.Debug);

        if (offset < 0)
        {
            /* If no address were told manually and nothing was detected.... */
            settings.Debug?.WriteLine("No sound engine was found.");
            return 0;
        }
        settings.Debug?.WriteLine($"Sound engine detected at offset 0x{offset:x}");

        /* Test validity of engine offset with -16 and -32 */
        bool validM16 = TestPointerValidity(buffer.Slice((int)(offset - 16)));
        bool validM32 = TestPointerValidity(buffer.Slice((int)(offset - 32)));

        /* If neither is found there is an error */
        if (!validM16 && !validM32)
        {
            settings.Debug?.WriteLine("Only a partial sound engine was found.");
            return 0;
        }
        offset -= validM16 ? 16 : 32;

        ReadOnlySpan<byte> data = buffer.Slice((int)offset);
        uint songTblAdr = (BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8)) & 0x3FFFFFF) + 12 * BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
        SoundEngineParam p = new(BinaryPrimitives.ReadUInt32LittleEndian(data));

        //Read # of song levels
        settings.Debug?.WriteLine($"# of song levels: {BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4))}");

        // At this point we can be certain we detected the real thing.
        settings.Debug?.WriteLine("Engine parameters:");
        settings.Debug?.WriteLine($"Main Volume: {p.MainVol} Polyphony: {p.Polyphony} channels, Dac: {17 - p.DacBits} bits, Sampling rate: {s_lookup[p.SamplingRateIndex]}");
        settings.Debug?.WriteLine($"Song table located at: 0x{songTblAdr:x}");
        /* Return the offset of sappy info to the operating system */
        return (int)offset;
    }
}
