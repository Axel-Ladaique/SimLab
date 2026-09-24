using SimLab.App.Session;
using SimLab.Flight.Controls;

namespace SimLab.App.Audio;

public static class WavWriter
{
    public static void Write(Stream stream, ReadOnlySpan<float> samples, int sampleRate)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var s in samples) w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
    }
}

/// <summary>Renders the aircraft voice (no 3D attenuation) of a scripted flight, for listening and spectral checks.</summary>
public static class OfflineAudio
{
    const double FrameDt = 1.0 / 60;

    /// <summary>Idle, a 3 s throttle ramp, a rotation, a climb, then motor off for a glide from 14 s.</summary>
    public static ControlInputs TakeoffScript(double t) => new(
        Throttle: t < 1 ? 0 : t < 14 ? Math.Min((t - 1) / 3, 1) : 0,
        Aileron: 0,
        Elevator: t > 5 && t < 6.5 ? 0.25 : 0.05,
        Rudder: 0);

    public static float[] Render(FlightSession session, AircraftSound sound, double seconds, int sampleRate, Func<double, ControlInputs> script)
    {
        var synth = new EngineSynth(sampleRate);
        var output = new List<float>((int)(seconds * sampleRate) + sampleRate);
        double produced = 0;
        for (double t = 0; t < seconds - 1e-9; t += FrameDt)
        {
            session.Tick(FrameDt, script(session.Simulation.Time));
            var frame = sound.Update(FrameDt);
            produced += FrameDt * sampleRate;
            int count = (int)Math.Round(produced) - output.Count;
            var buffer = new float[count];
            synth.Render(buffer, frame.Synth);
            output.AddRange(buffer);
        }
        return output.ToArray();
    }
}
