namespace GbaMus;

/// <summary>
/// Settings for a tool.
/// </summary>
/// <param name="Debug">Debug output.</param>
/// <param name="Error">Error output.</param>
public record ToolSettings(TextWriter? Debug = null, TextWriter? Error = null);
