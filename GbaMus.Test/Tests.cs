using System;
using System.Security.Cryptography;
using NUnit.Framework;

namespace GbaMus.Test;

public class Tests
{
    [Test]
    public void TestEmbeddedResources()
    {
        const string goldenSunSynthSha256 = "0F741B0631C732F77978CA433C817FEE14F106EDF54BF5794784B7FAAC6E2965";
        byte[] goldenSunSynthSha256Bytes = Convert.FromHexString(goldenSunSynthSha256);
        const string psgDataSha256 = "2810C79DD175B27992298060BD4C54B5FC2480A10F44C53AF664E2EBF046550E";
        byte[] psgDataSha256Bytes = Convert.FromHexString(psgDataSha256);
        using var sha256 = SHA256.Create();
        // ensure accessing multiple times works correctly
        Assert.That(sha256.ComputeHash(Resources.GetGoldenSunSynth()).AsSpan().SequenceEqual(goldenSunSynthSha256Bytes), Is.True);
        Assert.That(sha256.ComputeHash(Resources.GetGoldenSunSynth()).AsSpan().SequenceEqual(goldenSunSynthSha256Bytes), Is.True);
        Assert.That(sha256.ComputeHash(Resources.GetPsgData()).AsSpan().SequenceEqual(psgDataSha256Bytes), Is.True);
        Assert.That(sha256.ComputeHash(Resources.GetPsgData()).AsSpan().SequenceEqual(psgDataSha256Bytes), Is.True);
    }
}
