#pragma warning disable CS1591
namespace GbaMus;

/// <summary>
/// Represents MIDI event type.
/// </summary>
public enum MidiEventType
{
    NOTEOFF = 8,
    NOTEON = 9,
    NOTEAFT = 10,
    CONTROLLER = 11,
    PCHANGE = 12,
    CHNAFT = 13,
    PITCHBEND = 14
};
