using System.IO;
using NUnit.Framework;

namespace GbaMus.Test;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void TestEmbeddedResources()
    {
        Assert.That(() =>
        {
            using Stream stream = Resources.GetPsgData();
            stream.ReadByte();
        }, Throws.Nothing);
        Assert.That(() =>
        {
            using Stream stream = Resources.GetGoldenSunSynth();
            stream.ReadByte();
        }, Throws.Nothing);
        Assert.Pass();
    }
}
