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
            out _songList, out _soundBankSet, out _soundBankDict);
        _supportedSongs = new SortedSet<int>();
        for (uint i = 0; i < _songList.Count; i++)
            if (_songList[(int)i] != _songTblEndPtr && _soundBankDict.ContainsKey(i))
                _supportedSongs.Add((int)i);
    }

    /// <summary>
    /// Counts the number of tracks in a song.
    /// </summary>
    /// <param name="song">Song id.</param>
    /// <returns>Number of tracks.</returns>
    /// <exception cref="ArgumentException">Thrown for invalid song id.</exception>
    public int GetTrackCount(int song)
    {
        return GetSongRipper(song, true).TrackCount;
    }

    /// <summary>
    /// Writes MIDI file to stream.
    /// </summary>
    /// <param name="stream">Stream to write to.</param>
    /// <param name="song">Song index.</param>
    /// <exception cref="ArgumentException">Thrown for invalid song id.</exception>
    public void WriteMidi(Stream stream, int song)
    {
        GetSongRipper(song).Write(stream);
    }

    /// <summary>
    /// Gets song ripper for the specified song.
    /// </summary>
    /// <param name="song">Song to rip.</param>
    /// <param name="metadataOnly">If true, metadata export will be disabled on the created ripper (this can be changed later with <see cref="SongRipper.ChangeSettings"/>).</param>
    /// <returns>Song ripper.</returns>
    /// <exception cref="ArgumentException">Thrown for invalid song id.</exception>
    public SongRipper GetSongRipper(int song, bool metadataOnly = false)
    {
        if (!_supportedSongs.Contains(song) ||
            _songList[song] == _songTblEndPtr ||
            !_soundBankDict.TryGetValue((uint)song, out int bankIndex))
            throw new ArgumentException("Invalid song ID");
        return new SongRipper(_cache, new SongRipper.Settings(
            Rc: _settings.Rc,
            Gs: !_settings.Xg,
            Xg: _settings.Xg,
            Sv: !_settings.Raw,
            Lv: !_settings.Raw,
            BankNumber: bankIndex,
            BaseAddress: _songList[song],
            MetadataOnly: metadataOnly));
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
