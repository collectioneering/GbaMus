using GbaMus;

try
{
    return SappyDetector.Main(args);
}
catch (EnvironmentExitException e)
{
    return e.Code;
}
