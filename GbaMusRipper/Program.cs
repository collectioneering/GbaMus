using GbaMus;

try
{
    return GbaMusRipper.Main(args);
}
catch (EnvironmentExitException e)
{
    return e.Code;
}
