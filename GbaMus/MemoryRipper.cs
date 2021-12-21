namespace GbaMus;

/// <summary>
/// Represents a context object for performing in-memory ripping from a specific stream source stream.
/// </summary>
public class MemoryRipper
{
    /// <summary>
    /// Available songs.
    /// </summary>
    public ISet<int> Songs => _supportedSongs;

    private readonly MemoryStream _cache;
    private readonly GbaMusRipper.Settings _settings;
    private readonly int _sampleRate;
    private readonly int _mainVolume;
    private readonly uint _songTblEndPtr;
    private readonly List<uint> _songList;
    private readonly SortedSet<uint> _soundBankSet;
    private readonly Dictionary<uint, uint> _soundBankSrcDict;
    private readonly Dictionary<uint, int> _soundBankDict;
    private readonly SortedSet<int> _supportedSongs;

    /// <summary>
    /// Initializes a new instance of <see cref="MemoryRipper"/>.
    /// </summary>
    /// <param name="stream">Stream to read from.</param>
    /// <param name="settings">Settings.</param>
    public MemoryRipper(Stream stream, GbaMusRipper.Settings settings)
    {
        _settings = settings;
        if (stream is MemoryStream ms)
        {
            _cache = ms;
        }
        else
        {
            _cache = new MemoryStream();
            stream.CopyTo(_cache);
        }
        GbaMusRipper.Load(_cache, settings, out _sampleRate, out _mainVolume, out _songTblEndPtr,
            out _songList, out _soundBankSet, out _soundBankSrcDict, out _soundBankDict);
        _supportedSongs = new SortedSet<int>();
        for (uint i = 0; i < _songList.Count; i++)
            if (_songList[(int)i] != _songTblEndPtr && _soundBankSrcDict.ContainsKey(i))
                _supportedSongs.Add((int)i);
    }

    /// <summary>
    /// Writes MIDI file to stream.
    /// </summary>
    /// <param name="stream">Stream to write to.</param>
    /// <param name="song">Song index.</param>
    /// <exception cref="ArgumentException">Thrown for bad arguments.</exception>
    public void WriteMidi(Stream stream, int song)
    {
        if (!_supportedSongs.Contains(song) ||
            _songList[song] == _songTblEndPtr ||
            !_soundBankSrcDict.TryGetValue((uint)song, out uint bank) ||
            !_soundBankDict.TryGetValue(bank, out int bankIndex))
            throw new ArgumentException("Invalid song ID");
        SongRipper r = new(_cache, new SongRipper.Settings(
            Rc: _settings.Rc,
            Gs: !_settings.Xg,
            Xg: _settings.Xg,
            Sv: !_settings.Raw,
            Lv: !_settings.Raw,
            BankNumber: bankIndex,
            BaseAddress: _songList[song]));
        r.Write(stream);
    }

    /// <summary>
    /// Writes output for soundbank to stream.
    /// </summary>
    /// <param name="stream">Stream to write to.</param>
    /// <exception cref="IOException">Thrown on an I/O exception.</exception>
    public void WriteSoundFont(Stream stream)
    {
        var r = new SoundFontRipper(_cache, new SoundFontRipper.Settings(GmPresetNames: _settings.Gm, Addresses: _soundBankSet, SampleRate: (uint)_sampleRate, MainVolume: (uint)_mainVolume));
        r.Write(stream);
    }
}
