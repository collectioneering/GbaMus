namespace GbaMus;

/// <summary>
/// Represents an exception thrown in place of a <see cref="Environment.Exit"/> call.
/// </summary>
public class EnvironmentExitException : Exception
{
    /// <summary>
    /// Exit code.
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="EnvironmentExitException"/>.
    /// </summary>
    /// <param name="code">Exit code.</param>
    public EnvironmentExitException(int code) => Code = code;
}
