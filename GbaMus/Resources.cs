namespace GbaMus;

/// <summary>
/// Provides access to built-in resources.
/// </summary>
public static class Resources
{
    private static MemoryStream? s_psgData;
    private static MemoryStream? s_goldenSunSynth;

    /// <summary>
    /// Gets stream for psg_data.raw
    /// </summary>
    /// <returns>psg_data.raw</returns>
    public static Stream GetPsgData()
    {
        if (s_psgData != null) return s_psgData;
        s_psgData = new MemoryStream();
        using Stream s = typeof(Resources).Assembly.GetManifestResourceStream("GbaMus.psg_data.raw") ?? throw new IOException();
        s.CopyTo(s_psgData);
        return s_psgData;
    }

    /// <summary>
    /// Gets stream for goldensun_synth.raw
    /// </summary>
    /// <returns>goldensun_synth.raw</returns>
    public static Stream GetGoldenSunSynth()
    {
        if (s_goldenSunSynth != null) return s_goldenSunSynth;
        s_goldenSunSynth = new MemoryStream();
        using Stream s = typeof(Resources).Assembly.GetManifestResourceStream("GbaMus.goldensun_synth.raw") ?? throw new IOException();
        s.CopyTo(s_goldenSunSynth);
        return s_goldenSunSynth;
    }
}
