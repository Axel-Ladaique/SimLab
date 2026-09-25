using Godot;
using SimLab.App.Audio;

namespace SimLab.Game.Audio;

/// <summary>Renders an <see cref="EngineSynth"/> and pushes it to an <see cref="AudioStreamGeneratorPlayback"/> in
/// fixed-size chunks, so a steady-state per-frame update does no allocation. Shared by <see cref="AircraftAudio"/>
/// (the in-flight voice) and the sound screen's "Listen" preview.</summary>
public sealed class GeneratorFeeder
{
    public const int ChunkFrames = 256;

    readonly EngineSynth _synth;
    readonly float[] _scratch = new float[ChunkFrames];
    readonly Vector2[] _stereo = new Vector2[ChunkFrames];

    public GeneratorFeeder(EngineSynth synth) => _synth = synth;

    /// <summary>Renders and pushes as many chunks as the playback has room for, targeting the given parameters.
    /// EngineSynth ramps from its last sample to the target on every Render call, so pushing the same target across
    /// several chunks in one call (or one per <c>_Process</c>) is correct — the parameters just haven't changed
    /// since the last chunk.</summary>
    public void Push(AudioStreamGeneratorPlayback? playback, in SynthParams target)
    {
        while (playback is not null && playback.GetFramesAvailable() >= ChunkFrames)
        {
            _synth.Render(_scratch, target);
            for (int i = 0; i < ChunkFrames; i++) _stereo[i] = new Vector2(_scratch[i], _scratch[i]);
            playback.PushBuffer(_stereo);
        }
    }
}
