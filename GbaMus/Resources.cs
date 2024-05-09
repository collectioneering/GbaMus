namespace GbaMus;

/// <summary>
/// Provides access to built-in resources.
/// </summary>
public static class Resources
{
    private static byte[]? s_psgData;
    private static byte[]? s_goldenSunSynth;

    /// <summary>
    /// Gets stream for psg_data.raw
    /// </summary>
    /// <returns>psg_data.raw</returns>
    public static Stream GetPsgData()
    {
        return new MemoryStream(s_psgData ??= LoadResource("GbaMus.psg_data.raw"), false);
    }

    /// <summary>
    /// Gets stream for goldensun_synth.raw
    /// </summary>
    /// <returns>goldensun_synth.raw</returns>
    public static Stream GetGoldenSunSynth()
    {
        return new MemoryStream(s_goldenSunSynth ??= LoadResource("GbaMus.goldensun_synth.raw"), false);
    }

    private static byte[] LoadResource(string name)
    {
        using Stream s = typeof(Resources).Assembly.GetManifestResourceStream(name) ?? throw new IOException($"Failed to load manifest resource [{name}]");
        if (s.CanSeek)
        {
            long l = s.Length;
            if (l > int.MaxValue)
            {
                throw new InvalidOperationException($"Manifest resource [{name}] has length {l} which exceeds supported length");
            }
            byte[] result = new byte[l];
            s.ForceRead(result, 0, result.Length);
            return result;
        }
        var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
