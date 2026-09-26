using SimLab.App.Audio;
using SimLab.App.Maps;
using SimLab.App.Session;
using SimLab.App.Settings;

namespace SimLab.App.Tests.Audio;

public class OfflineAudioTests
{
    [Fact]
    public void Wav_header_and_samples_are_16_bit_pcm_mono()
    {
        using var ms = new MemoryStream();
        WavWriter.Write(ms, new float[] { 0f, 1f, -1f, 0.5f }, 44100);
        var b = ms.ToArray();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(b, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(b, 22));
        Assert.Equal(44100, BitConverter.ToInt32(b, 24));
        Assert.Equal(16, BitConverter.ToInt16(b, 34));
        Assert.Equal(8, BitConverter.ToInt32(b, 40));
        Assert.Equal(44 + 8, b.Length);
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(b, 46));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(b, 48));
    }

    [Fact]
    public void Takeoff_render_has_the_expected_length_and_gets_loud()
    {
        using var session = new FlightSession(TestData.Aircraft("trainer"), new FlightConditions(WindSpeed: 0), FieldCatalog.Load("club"));
        var sound = new AircraftSound(session, SoundSpec.Default);
        var samples = OfflineAudio.Render(session, sound, 4, 22050, OfflineAudio.TakeoffScript);
        Assert.InRange(samples.Length, 4 * 22050 - 400, 4 * 22050 + 400);
        double rmsStart = Rms(samples.AsSpan(0, 2205));
        double rmsEnd = Rms(samples.AsSpan(samples.Length - 22050));
        Assert.True(rmsEnd > 5 * rmsStart + 1e-3, $"start {rmsStart:F4} end {rmsEnd:F4}");
    }

    static double Rms(ReadOnlySpan<float> x)
    {
        double s = 0;
        foreach (var v in x) s += v * v;
        return Math.Sqrt(s / x.Length);
    }
}
