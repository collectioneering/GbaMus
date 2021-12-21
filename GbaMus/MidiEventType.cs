#pragma warning disable CS1591
namespace GbaMus;

/// <summary>
/// Represents MIDI event type.
/// </summary>
public enum MidiEventType
{
    Noteoff = 8,
    Noteon = 9,
    Noteaft = 10,
    Controller = 11,
    Pchange = 12,
    Chnaft = 13,
    Pitchbend = 14
};
