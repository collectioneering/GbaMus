using System.Text;

namespace GbaMus;

/// <summary>
/// Represents a group of sound font samples.
/// </summary>
public class GbaSamples
{
    /// <summary>
    /// List of pointers to samples within the .gba file, position is # of sample in .sf2
    /// </summary>
    public readonly List<uint> SamplesList;
    /// <summary>
    /// Related Sf2 class.
    /// </summary>
    public readonly Sf2 Sf2;

    /// <summary>
    /// Initializes a new instance of <see cref="GbaSamples"/>.
    /// </summary>
    /// <param name="sf2">Related sf2 class.</param>
    public GbaSamples(Sf2 sf2)
    {
        SamplesList = new List<uint>();
        Sf2 = sf2;
    }

    /// <summary>
    /// Converts a normal sample to SoundFont format.
    /// </summary>
    /// <param name="stream">Stream to read from.</param>
    /// <param name="pointer">Sample offset.</param>
    /// <returns>Sample index.</returns>
    public int BuildSample(Stream stream, uint pointer)
    {
        // Do nothing if sample already exists
        for (int i = SamplesList.Count - 1; i >= 0; --i)
            if (SamplesList[i] == pointer)
                return i;

        // Read sample data
        stream.Position = pointer;

        uint loop = stream.ReadUInt32LittleEndian();
        uint pitch = stream.ReadUInt32LittleEndian();
        uint loopPos = stream.ReadUInt32LittleEndian();
        uint len = stream.ReadUInt32LittleEndian();

        //Now we should make sure the data is coherent, and reject
        //the samples if errors are suspected

        //Detect invalid samples
        bool loopEn;
        bool bdpcmEn = false;

        if (loop == 0x40000000)
            loopEn = true;
        else if (loop == 0x00000000)
            loopEn = false;
        else if (loop == 0x1)
        {
            bdpcmEn = true; // Detect compressed samples
            loopEn = false;
        }
        else
            throw new InvalidDataException("Invalid loop"); // Invalid loop -> return error

        // Compute SF2 base note and fine tune from GBA pitch
        // GBA pitch is 1024 * Mid_C frequency
        double deltaNote = 12 * Math.Log(Sf2.DefaultSampleRate * 1024.0 / pitch, 2);
        double intDeltaNote = Math.Round(deltaNote);
        uint pitchCorrection = (uint)((intDeltaNote - deltaNote) * 100);
        uint originalPitch = (uint)(60 + intDeltaNote);

        // Detect Golden Sun samples
        if (len == 0 && loopPos == 0)
        {
            if (stream.ReadByte() != 0x80) throw new InvalidDataException();
            byte type = stream.ReadUInt8LittleEndian();
            switch (type)
            {
                case 0: // Square wave
                    {
                        string name = $"Square @0x{pointer:X}";
                        byte dutyCycle = stream.ReadUInt8LittleEndian();
                        byte changeSpeed = stream.ReadUInt8LittleEndian();
                        if (changeSpeed == 0)
                        {
                            // Square wave with constant duty cycle
                            uint basePointer = (uint)(128 + 64 * (dutyCycle >> 2));
                            Sf2.AddNewSample(Resources.GetGoldenSunSynth(), SampleType.UNSIGNED_8, Encoding.ASCII.GetBytes(name), basePointer, 64, true, 0, (sbyte)originalPitch, (sbyte)pitchCorrection);
                        }
                        else
                        {
                            // Sqaure wave with variable duty cycle, not exact, but sounds close enough
                            Sf2.AddNewSample(Resources.GetGoldenSunSynth(), SampleType.UNSIGNED_8, Encoding.ASCII.GetBytes(name), 128, 8192, true, 0, (sbyte)originalPitch, (sbyte)pitchCorrection);
                        }
                    }
                    break;

                case 1: // Saw wave
                    {
                        string name = $"Saw @0x{pointer:X}";
                        Sf2.AddNewSample(Resources.GetGoldenSunSynth(), SampleType.UNSIGNED_8, Encoding.ASCII.GetBytes(name), 0, 64, true, 0, (sbyte)originalPitch, (sbyte)pitchCorrection);
                    }
                    break;

                case 2: // Triangle wave
                    {
                        string name = $"Triangle @0x{pointer:X}";
                        Sf2.AddNewSample(Resources.GetGoldenSunSynth(), SampleType.UNSIGNED_8, Encoding.ASCII.GetBytes(name), 64, 64, true, 0, (sbyte)originalPitch, (sbyte)pitchCorrection);
                    }
                    break;

                default:
                    throw new InvalidDataException();
            }
        }
        else
        {
            //Prevent samples which are way too long or too short
            if (len < 16 || len > 0x3FFFFF) throw new InvalidDataException();

            //Prevent samples with illegal loop point from happening
            if (loopPos > len - 8)
            {
                // Warning : Illegal loop point detected
                loopPos = 0;
            }

            // Create (poetic) instrument name
            string name = bdpcmEn ? $"BDPCM @0x{pointer:X}" : $"Sample @0x{pointer:X}";

            // Add the sample to output
            Sf2.AddNewSample(stream, bdpcmEn ? SampleType.BDPCM : SampleType.SIGNED_8, Encoding.ASCII.GetBytes(name), pointer + 16, len, loopEn, loopPos, (sbyte)originalPitch, (sbyte)pitchCorrection);
        }
        SamplesList.Add(pointer);
        return SamplesList.Count - 1;
    }

    /// <summary>
    /// Converts a Game Boy channel 3 sample to SoundFont format.
    /// </summary>
    /// <param name="stream">Stream to read from.</param>
    /// <param name="pointer">Sample offset.</param>
    /// <returns>Sample index.</returns>
    public int BuildGb3Samples(Stream stream, uint pointer)
    {
        // Do nothing if sample already exists
        for (int i = SamplesList.Count - 1; i >= 0; --i)
            if (SamplesList[i] == pointer)
                return i;

        string name = $"GB3 @0x{pointer:X}";

        Sf2.AddNewSample(stream, SampleType.GAMEBOY_CH3, Encoding.ASCII.GetBytes(name + 'A'), pointer, 256, true, 0, 53, 24, 22050);
        Sf2.AddNewSample(stream, SampleType.GAMEBOY_CH3, Encoding.ASCII.GetBytes(name + 'B'), pointer, 128, true, 0, 65, 24, 22050);
        Sf2.AddNewSample(stream, SampleType.GAMEBOY_CH3, Encoding.ASCII.GetBytes(name + 'C'), pointer, 64, true, 0, 77, 24, 22050);
        Sf2.AddNewSample(stream, SampleType.GAMEBOY_CH3, Encoding.ASCII.GetBytes(name + 'D'), pointer, 32, true, 0, 89, 24, 22050);

        // We have to to add multiple entries to have the size of the list in sync
        // with the numeric indexes of samples....
        for (int i = 4; i != 0; --i) SamplesList.Add(pointer);
        return SamplesList.Count - 1;
    }


    //This data is referenced to my set of recordings
    //stored in "psg_data.raw"
    private static readonly int[] s_pulsePointerTbl = { 0x0000, 0x2166, 0x3c88, 0x4bd2, 0x698a, 0x7798, 0x903e, 0xa15e, 0xb12c, 0xbf4a, 0xc958, 0xe200, 0xf4ec, 0x10534, 0x11360 };
    private static readonly int[] s_pulseSizeTbl = { 0x10b3, 0xd91, 0x7a5, 0xdec, 0x707, 0xc53, 0x890, 0x7e7, 0x70f, 0x507, 0xc54, 0x976, 0x824, 0x716, 0x36b };
    private static readonly int[] s_pulseLoopSize = { 689, 344, 172, 86, 43 };

    /// <summary>
    /// Converts a Game Boy pulse (channels 1, 2) sample.
    /// </summary>
    /// <param name="dutyCycle">Duty cycle.</param>
    /// <returns>Sample index.</returns>
    public int BuildPulseSamples(uint dutyCycle)
    {
        // Do nothing if sample already exists
        for (int i = SamplesList.Count - 1; i >= 0; --i)
            if (SamplesList[i] == dutyCycle)
                return i;

        string name = "square ";
        switch (dutyCycle)
        {
            case 0:
                name += "12.5%";
                break;

            default:
                name += "25%";
                break;

            case 2:
                name += "50%";
                break;
        }

        for (int i = 0; i < 5; i++)
        {
            Sf2.AddNewSample(Resources.GetPsgData(), SampleType.SIGNED_16, Encoding.ASCII.GetBytes(name + (char)('A' + i)),
                (uint)s_pulsePointerTbl[dutyCycle * 5 + i], (uint)s_pulseSizeTbl[dutyCycle * 5 + i], true,
                (uint)(s_pulseSizeTbl[dutyCycle * 5 + i] - s_pulseLoopSize[i]), (sbyte)(36 + 12 * i), 38, 44100);
            SamplesList.Add(dutyCycle);
        }
        return SamplesList.Count - 1;
    }

    private static readonly int[] s_noisePointerTbl = { 72246, 160446, 248646, 336846, 425046, 513246, 601446, 689646, 777846, 866046, 954246, 1042446, 1130646, 1218846, 1307046, 1395246, 1483446, 1571646, 1659846, 1748046, 1836246, 1924446, 2012646, 2100846, 2189046, 2277246, 2387493, 2475690, 2552863, 2619011, 2674134, 2718233, 2756819, 2789893, 2817455, 2839504, 2856041, 2867066, 2872578 };

    private static readonly int[] s_noiseNormalLenTbl = { 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 88200, 110247, 88197, 77173, 66148, 55123, 44099, 38586, 33074, 27562, 22049, 16537, 11025, 5512, 2756 };

    private static readonly int[] s_noiseMetallicLenTbl = { 43755, 38286, 32817, 27347, 21878, 19143, 16408, 13674, 10939, 9572, 8204, 6837, 5469, 4786, 4102, 3418, 2735, 2393, 2051, 1709, 1367, 1196, 1026, 855, 684, 598, 513, 427, 342, 299, 256, 214, 171, 150, 128, 107, 85, 64 };


    /// <summary>
    /// Converts a Game Boy noise (channel 4) sample.
    /// </summary>
    /// <param name="metallic">Metallic flag.</param>
    /// <param name="key">Key.</param>
    /// <returns>Sample index.</returns>
    public int BuildNoiseSample(bool metallic, int key)
    {
        //prevent out of range keys
        if (key < 42) key = 42;
        if (key > 77) key = 76;

        uint num = (uint)(metallic ? 3 + (key - 42) : 80 + (key - 42));

        // Do nothing if sample already exists
        for (int i = SamplesList.Count - 1; i >= 0; --i)
            if (SamplesList[i] == num)
                return i;

        string name = "Noise " + (metallic ? "metallic " : "normal ") + key;

        Sf2.AddNewSample(Resources.GetPsgData(), SampleType.UNSIGNED_8, Encoding.ASCII.GetBytes(name), (uint)s_noisePointerTbl[key - 42],
            (uint)(metallic ? s_noiseMetallicLenTbl[key - 42] : s_noiseNormalLenTbl[key - 42]), true, 0, (sbyte)key, 0, 44100);

        SamplesList.Add(num);
        return SamplesList.Count - 1;
    }
}
