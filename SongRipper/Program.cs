using GbaMus;

try
{
    return SongRipper.Main(args);
}
catch (EnvironmentExitException e)
{
    return e.Code;
}
