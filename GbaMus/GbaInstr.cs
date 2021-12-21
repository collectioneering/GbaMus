using System.Text;

namespace GbaMus;

/// <summary>
/// Represents a GBA instrument.
/// </summary>
public class GbaInstr
{
    /// <summary>
    /// Current instrument index.
    /// </summary>
    private int _curInstIndex;

    /// <summary>
    /// Contains pointers to instruments within GBA file, their position is the # of instrument in the SF2
    /// </summary>
    private readonly Dictionary<InstData, int> _instMap;

    /// <summary>
    /// Related sf2 file.
    /// </summary>
    private readonly Sf2 _sf2;

    /// <summary>
    /// Related samples class.
    /// </summary>
    private readonly GbaSamples _samples;

    /// <summary>
    /// Initializes a new instance of <see cref="GbaInstr"/>.
    /// </summary>
    /// <param name="sf2">Related soundfont.</param>
    public GbaInstr(Sf2 sf2)
    {
        _curInstIndex = 0;
        _instMap = new Dictionary<InstData, int>();
        _sf2 = sf2;
        _samples = new GbaSamples(sf2);
    }

    /// <summary>
    /// Applies ADSR envelope on the instrument.
    /// </summary>
    /// <param name="adsr">Attack, decay, sustain, release.</param>
    private void GenerateAdsrGenerators(uint adsr)
    {
        // Get separate components
        int attack = (int)(adsr & 0xFF);
        int decay = (int)((adsr >> 8) & 0xFF);
        int sustain = (int)((adsr >> 16) & 0xFF);
        int release = (int)(adsr >> 24);
        // Add generators for ADSR envelope if required
        if (attack != 0xFF)
        {
            // Compute attack time - the sound engine is called 60 times per second
            // and adds "attack" to envelope every time the engine is called
            double attTime = 256 / 60.0 / attack;
            double att = 1200 * Math.Log(attTime, 2);
            _sf2.AddNewInstGenerator(SfGenerator.AttackVolEnv, (ushort)att);
        }
        if (sustain != 0xFF)
        {
            double sus;
            // Compute attenuation in cB if sustain is non-zero
            if (sustain != 0) sus = 100 * Math.Log(256.0 / sustain);
            // Special case where attenuation is infinite -> use max value
            else sus = 1000;

            _sf2.AddNewInstGenerator(SfGenerator.SustainVolEnv, (ushort)sus);

            double decTime = Math.Log(256.0) / (Math.Log(256) - Math.Log(decay)) / 60.0;
            decTime *= 10 / Math.Log(256);
            double dec = 1200 * Math.Log(decTime, 2);
            _sf2.AddNewInstGenerator(SfGenerator.DecayVolEnv, (ushort)dec);
        }

        if (release != 0x00)
        {
            double relTime = Math.Log(256.0) / (Math.Log(256) - Math.Log(release)) / 60.0;
            double rel = 1200 * Math.Log(relTime, 2);
            _sf2.AddNewInstGenerator(SfGenerator.ReleaseVolEnv, (ushort)rel);
        }
    }

    /// <summary>
    /// Applies PSG ADSR envelope on the instrument.
    /// </summary>
    /// <param name="adsr">Attack, decay, sustain, release.</param>
    private void GeneratePsgAdsrGenerators(uint adsr)
    {
        // Get separate components
        int attack = (int)(adsr & 0xFF);
        int decay = (int)((adsr >> 8) & 0xFF);
        int sustain = (int)((adsr >> 16) & 0xFF);
        int release = (int)(adsr >> 24);

        // Reject instrument if invalid values !
        if (attack > 15 || decay > 15 || sustain > 15 || release > 15)
            throw new ArgumentException("Invalid instrument adsr value");

        // Add generators for ADSR envelope if required
        if (attack != 0)
        {
            // Compute attack time - the sound engine is called 60 times per second
            // and adds "attack" to envelope every time the engine is called
            double attTime = attack / 5.0;
            double att = 1200 * Math.Log(attTime, 2);
            _sf2.AddNewInstGenerator(SfGenerator.AttackVolEnv, (ushort)att);
        }

        if (sustain != 15)
        {
            double sus;
            // Compute attenuation in cB if sustain is non-zero
            if (sustain != 0) sus = 100 * Math.Log(15.0 / sustain);
            // Special case where attenuation is infinite -> use max value
            else sus = 1000;

            _sf2.AddNewInstGenerator(SfGenerator.SustainVolEnv, (ushort)sus);

            double decTime = decay / 5.0;
            double dec = 1200 * Math.Log(decTime + 1, 2);
            _sf2.AddNewInstGenerator(SfGenerator.DecayVolEnv, (ushort)dec);
        }

        if (release != 0)
        {
            double relTime = release / 5.0;
            double rel = 1200 * Math.Log(relTime, 2);
            _sf2.AddNewInstGenerator(SfGenerator.ReleaseVolEnv, (ushort)rel);
        }
    }

    /// <summary>
    /// Builds a SF2 instrument from a GBA sampled instrument.
    /// </summary>
    /// <param name="stream">Stream to read from.</param>
    /// <param name="inst">Instrument to build.</param>
    /// <returns>Index of instrument.</returns>
    public int BuildSampledInstrument(Stream stream, InstData inst)
    {
        // Do nothing if this instrument already exists !
        if (_instMap.TryGetValue(inst, out int v)) return v;

        // The flag is set if no scaling should be done if the instrument type is 8
        bool noScale = (inst.Word0 & 0xff) == 0x08;

        // Get sample pointer
        uint samplePointer = inst.Word1 & 0x3ffffff;

        // Determine if loop is enabled (it's dumb but we have to seek just for this)
        stream.Position = samplePointer | 3;
        bool loopFlag = stream.ReadByte() == 0x40;

        // Build pointed sample
        int sampleIndex = _samples.BuildSample(stream, samplePointer);

        // Instrument's name
        string name = $"sample @0x{samplePointer:x}";

        // Create instrument bag
        _sf2.AddNewInstrument(Encoding.ASCII.GetBytes(name));
        _sf2.AddNewInstBag();

        // Add generator to prevent scaling if required
        if (noScale)
            _sf2.AddNewInstGenerator(SfGenerator.ScaleTuning, 0);

        GenerateAdsrGenerators(inst.Word2);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, (ushort)(loopFlag ? 1 : 0));
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)sampleIndex);

        // Add instrument to list
        _instMap[inst] = _curInstIndex;
        return _curInstIndex++;
    }

    /// <summary>
    /// Creates new SF2 from every key split GBA instrument.
    /// </summary>
    /// <param name="stream">Stream to read from.</param>
    /// <param name="inst">Instrument to build.</param>
    /// <returns>Instrument index.</returns>
    public int BuildEveryKeysplitInstrument(Stream stream, InstData inst)
    {
        // Do nothing if this instrument already exists !
        if (_instMap.TryGetValue(inst, out int v)) return v;

        // I'm sorry for doing a dumb copy/pase of the routine right above
        // But there was too much differences to handles to practically handle it with flags
        // therefore I didn't really had a choice.
        uint baseAddress = inst.Word1 & 0x3ffffff;
        string name = $"EveryKeySplit @0x{baseAddress:x}";
        _sf2.AddNewInstrument(Encoding.ASCII.GetBytes(name));

        // Loop through all keys
        for (int key = 0; key < 128; key++)
        {
            try
            {
                // Seek at the key's instrument
                stream.Position = baseAddress + 12 * key;

                // Read instrument data
                int instrType = stream.ReadByte(); // Instrument type
                int keynum = stream.ReadByte(); // Key (every key split instrument only)
                /*  int unused_byte =*/
                stream.ReadByte(); // Unknown/unused byte
                int panning = stream.ReadByte(); // Panning (every key split instrument only)

                // The flag is set if no scaling should be done on the sample
                bool noScale = false;

                uint mainWord = stream.ReadUInt32LittleEndian();
                // Get ADSR envelope
                uint adsr = stream.ReadUInt32LittleEndian();

                int sampleIndex;
                bool loopFlag = true;

                int instrTypeMasked = instrType & 0x0f;
                switch (instrTypeMasked)
                {
                    case 8:
                    case 0:
                        {
                            if (instrTypeMasked == 8)
                                noScale = true;
                            // Determine if loop is enabled and read sample's pitch
                            uint samplePointer = mainWord & 0x3ffffff;
                            stream.Position = samplePointer | 3;
                            loopFlag = stream.ReadByte() == 0x40;

                            uint pitch = stream.ReadUInt32LittleEndian();

                            // Build pointed sample
                            sampleIndex = _samples.BuildSample(stream, samplePointer);

                            // Add a bag for this key
                            _sf2.AddNewInstBag();
                            GenerateAdsrGenerators(adsr);
                            // Add generator to prevent scaling if required
                            if (noScale)
                                _sf2.AddNewInstGenerator(SfGenerator.ScaleTuning, 0);

                            // Compute base note and fine tune from pitch
                            double deltaNote = 12.0 * Math.Log(_sf2.DefaultSampleRate * 1024.0 / pitch, 2);
                            int rootkey = (int)(60 + Math.Round(deltaNote));

                            // Override root key with the value we need
                            _sf2.AddNewInstGenerator(SfGenerator.OverridingRootKey, (ushort)(rootkey - keynum + key));
                            // Key range is only a single key (obviously)
                            _sf2.AddNewInstGenerator(SfGenerator.KeyRange, (byte)key, (byte)key);
                        }
                        break;

                    case 4:
                    case 12:
                        {
                            // Determine whenever the note is metallic noise, normal noise, or invalid
                            bool metalFlag;
                            if (mainWord == 0x1000000)
                                metalFlag = true;
                            else if (mainWord == 0)
                                metalFlag = false;
                            else
                                throw new InvalidDataException("Invalid note kind");

                            // Build corresponding sample
                            sampleIndex = _samples.BuildNoiseSample(metalFlag, keynum);
                            _sf2.AddNewInstBag();
                            GeneratePsgAdsrGenerators(adsr);
                            _sf2.AddNewInstGenerator(SfGenerator.OverridingRootKey, (ushort)key);
                            _sf2.AddNewInstGenerator(SfGenerator.KeyRange, (byte)key, (byte)key);
                        }
                        break;

                    // Ignore other kind of instruments
                    default: throw new InvalidDataException("Unsupported instrument kind");
                }

                if (panning != 0)
                    _sf2.AddNewInstGenerator(SfGenerator.Pan, (ushort)((panning - 192) * (500 / 128.0)));
                // Same as a normal sample
                _sf2.AddNewInstGenerator(SfGenerator.SampleModes, (ushort)(loopFlag ? 1 : 0));
                _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)sampleIndex);
            }
            catch
            {
                // Continue to next key when there is a major problem
            }
        }
        // Add instrument to list
        _instMap[inst] = _curInstIndex;
        return _curInstIndex++;
    }

    /// <summary>
    /// Builds a SF2 instrument from a GBA key split instrument.
    /// </summary>
    /// <param name="stream">Stream to read from.</param>
    /// <param name="inst">Instrument to build.</param>
    /// <returns>Instrument index.</returns>
    public int BuildKeysplitInstrument(Stream stream, InstData inst)
    {
        // Do nothing if this instrument already exists !
        if (_instMap.TryGetValue(inst, out int v)) return v;

        uint basePointer = inst.Word1 & 0x3ffffff;
        uint keyTable = inst.Word2 & 0x3ffffff;

        // Decode key-table in usable data
        List<sbyte> splitList = new(), indexList = new();

        sbyte key = 0;
        int prevIndex = -1;
        int currentIndex;

        stream.Position = keyTable;

        // Add instrument to list
        string name = $"0x{basePointer:x} key split";
        _sf2.AddNewInstrument(Encoding.ASCII.GetBytes(name));

        do
        {
            int index = stream.ReadByte();

            // Detect where there is changes in the index table
            currentIndex = index;
            if (prevIndex != currentIndex)
            {
                splitList.Add(key);
                indexList.Add((sbyte)currentIndex);
                prevIndex = currentIndex;
            }
        } while (++key > 0);

        // Final entry for the last split
        splitList.Add(sbyte.MinValue);

        for (uint i = 0; i < indexList.Count; i++)
        {
            try
            {
                // Seek to pointed instrument
                stream.Position = basePointer + 12 * indexList[(int)i];

                // Once again I'm sorry for the dumb copy/pase
                // but doing it all with flags would have been quite complex

                int instType = stream.ReadInt32LittleEndian(); // Instrument type
                /* int keynum = */
                stream.ReadInt32LittleEndian(); // Key (every key split instrument only)
                /* int unused_byte = */
                stream.ReadInt32LittleEndian(); // Unknown/unused byte
                /* int panning = */
                stream.ReadInt32LittleEndian(); // Panning (every key split instrument only)

                // The flag is set if no scaling should be done on the sample
                bool noScale = instType == 8;

                // Get sample pointer
                uint samplePointer = stream.ReadUInt32LittleEndian();
                samplePointer &= 0x3ffffff;

                // Get ADSR envelope
                uint adsr = stream.ReadUInt32LittleEndian();

                // For now GameBoy instruments aren't supported
                // (I wonder if any game ever used this)
                if ((instType & 0x07) != 0) continue;

                // Determine if loop is enabled (it's dumb but we have to seek just for this)
                stream.Position = samplePointer | 3;
                bool loopFlag = stream.ReadByte() == 0x40;

                // Build pointed sample
                int sampleIndex = _samples.BuildSample(stream, samplePointer);

                // Create instrument bag
                _sf2.AddNewInstBag();

                // Add generator to prevent scaling if required
                if (noScale)
                    _sf2.AddNewInstGenerator(SfGenerator.ScaleTuning, 0);

                GenerateAdsrGenerators(adsr);
                // Particularity here : An additional bag to select the key range
                _sf2.AddNewInstGenerator(SfGenerator.KeyRange, (byte)splitList[(int)i], (byte)(splitList[(int)(i + 1)] - 1));
                _sf2.AddNewInstGenerator(SfGenerator.SampleModes, (ushort)(loopFlag ? 1 : 0));
                _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)sampleIndex);
            }
            catch
            {
                // Silently continue to next key if anything bad happens
            }
        }
        _instMap[inst] = _curInstIndex;
        return _curInstIndex++;
    }

    /// <summary>
    /// Builds gameboy channel 3 instrument.
    /// </summary>
    /// <param name="stream">Stream to read from.</param>
    /// <param name="inst">Instrument to build.</param>
    /// <returns>Instrument index.</returns>
    public int BuildGb3Instrument(Stream stream, InstData inst)
    {
        // Do nothing if this instrument already exists !
        if (_instMap.TryGetValue(inst, out int v)) return v;

        // Get sample pointer
        uint samplePointer = inst.Word1 & 0x3ffffff;

        // Try to seek to see if the pointer is valid, if it's not then abort
        stream.Position = samplePointer;

        int sample = _samples.BuildGb3Samples(stream, samplePointer);

        string name = $"GB3 @0x{samplePointer:x}";
        _sf2.AddNewInstrument(Encoding.ASCII.GetBytes(name));

        // Global zone
        _sf2.AddNewInstBag();
        GeneratePsgAdsrGenerators(inst.Word2);

        _sf2.AddNewInstBag();
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 0, 52);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)(sample - 3));
        _sf2.AddNewInstBag();
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 53, 64);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)(sample - 2));
        _sf2.AddNewInstBag();
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 65, 76);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)(sample - 1));
        _sf2.AddNewInstBag();
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 77, 127);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)sample);

        _instMap[inst] = _curInstIndex;
        return _curInstIndex++;
    }

    /// <summary>
    /// Builds GameBoy pulse wave instrument.
    /// </summary>
    /// <param name="inst">Instrument to build.</param>
    /// <returns>Instrument index.</returns>
    public int BuildPulseInstrument(InstData inst)
    {
        // Do nothing if this instrument already exists !
        if (_instMap.TryGetValue(inst, out int v)) return v;

        uint dutyCycle = inst.Word1;
        // The difference between 75% and 25% duty cycles is inaudible therefore
        // I simply replace 75% duty cycles by 25%
        if (dutyCycle == 3) dutyCycle = 1;
        if (dutyCycle > 3) throw new ArgumentException("Invalid duty cycle value");

        int sample = _samples.BuildPulseSamples(dutyCycle);
        string name = $"pulse {dutyCycle}";
        _sf2.AddNewInstrument(Encoding.ASCII.GetBytes(name));

        // Global zone
        _sf2.AddNewInstBag();
        GeneratePsgAdsrGenerators(inst.Word2);

        _sf2.AddNewInstBag();
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 0, 45);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)(sample - 4));
        _sf2.AddNewInstBag();
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 46, 57);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)(sample - 3));
        _sf2.AddNewInstBag();
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 58, 69);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)(sample - 2));
        _sf2.AddNewInstBag();
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 70, 81);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)(sample - 1));
        _sf2.AddNewInstBag();
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 82, 127);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)sample);

        _instMap[inst] = _curInstIndex;
        return _curInstIndex++;
    }

    /// <summary>
    /// Builds GameBoy white noise instrument.
    /// </summary>
    /// <param name="inst">Instrument to build.</param>
    /// <returns>Instrument index.</returns>
    public int BuildNoiseInstrument(InstData inst)
    {
        // Do nothing if this instrument already exists !
        if (_instMap.TryGetValue(inst, out int v)) return v;

        // 0 = normal, 1 = metallic, anything else = invalid
        if (inst.Word1 > 1) throw new ArgumentException("Invalid mode");
        bool metallic = inst.Word1 != 0;

        string name = metallic ? "GB metallic noise" : "GB noise";
        _sf2.AddNewInstrument(Encoding.ASCII.GetBytes(name));

        // Global zone
        _sf2.AddNewInstBag();
        GeneratePsgAdsrGenerators(inst.Word2);

        _sf2.AddNewInstBag();
        int sample42 = _samples.BuildNoiseSample(metallic, 42);
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 0, 42);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)sample42);

        for (int key = 43; key <= 77; key++)
        {
            _sf2.AddNewInstBag();
            int sample = _samples.BuildNoiseSample(metallic, key);
            _sf2.AddNewInstGenerator(SfGenerator.KeyRange, (byte)key, (byte)key);
            _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
            _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)sample);
        }

        _sf2.AddNewInstBag();
        int sample78 = _samples.BuildNoiseSample(metallic, 78);
        _sf2.AddNewInstGenerator(SfGenerator.KeyRange, 78, 127);
        _sf2.AddNewInstGenerator(SfGenerator.SampleModes, 1);
        _sf2.AddNewInstGenerator(SfGenerator.ScaleTuning, 0);
        _sf2.AddNewInstGenerator(SfGenerator.SampleId, (ushort)sample78);

        _instMap[inst] = _curInstIndex;
        return _curInstIndex++;
    }
}
