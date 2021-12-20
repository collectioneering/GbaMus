namespace GbaMus;

/// <summary>
/// Provides access to built-in resources.
/// </summary>
public static class Resources
{
    private static MemoryStream? _psgData;
    private static MemoryStream? _goldenSunSynth;

    /// <summary>
    /// Gets stream for psg_data.raw
    /// </summary>
    /// <returns>psg_data.raw</returns>
    public static Stream GetPsgData()
    {
        if (_psgData != null) return _psgData;
        _psgData = new MemoryStream();
        using Stream s = typeof(Resources).Assembly.GetManifestResourceStream("GbaMus.psg_data.raw") ?? throw new IOException();
        s.CopyTo(_psgData);
        return _psgData;
    }

    /// <summary>
    /// Gets stream for goldensun_synth.raw
    /// </summary>
    /// <returns>goldensun_synth.raw</returns>
    public static Stream GetGoldenSunSynth()
    {
        if (_goldenSunSynth != null) return _goldenSunSynth;
        _goldenSunSynth = new MemoryStream();
        using Stream s = typeof(Resources).Assembly.GetManifestResourceStream("GbaMus.goldensun_synth.raw") ?? throw new IOException();
        s.CopyTo(_goldenSunSynth);
        return _goldenSunSynth;
    }
}
