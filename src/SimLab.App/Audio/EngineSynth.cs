namespace SimLab.App.Audio;

/// <summary>
/// Aircraft voice synthesized sample by sample: propeller (blade-pass fundamental plus decaying harmonics, amplitude
/// modulated at the shaft rate), brushless whine (electrical frequency and its second harmonic), piston exhaust (a pulse
/// train at the shaft rate), turbine roar (low-passed noise), wind (low-passed noise) and rolling (band-limited noise with
/// random grain). Exhaust and roar share the motor level of the voice mix. Parameters ramp linearly across each buffer and
/// oscillator phases carry over, so parameter changes never click.
/// </summary>
public sealed class EngineSynth
{
    // Mix levels, tuned by ear.
    const double PropLevel = 0.35, WhineLevel = 0.08, WindLevel = 0.25, RollLevel = 0.2;
    const double ExhaustLevel = 0.12, RoarLevel = 0.5, RoarCutoffHz = 1800;
    const int PropHarmonics = 5, ExhaustHarmonics = 12;
    const double ShaftModulation = 0.15;

    readonly int _seed;
    SynthParams _last = SynthParams.Silent;
    VoiceMix _lastMix = VoiceMix.Full;
    double _bladePhase, _shaftPhase, _elecPhase;
    double _windState, _rollLow, _rollBand, _grain, _roarState;
    uint _rng;

    public EngineSynth(int sampleRate, int seed = 1)
    {
        SampleRate = sampleRate;
        _seed = seed;
        Reset();
    }

    public int SampleRate { get; }
    public bool EngineMuted { get; set; }

    /// <summary>Per-voice level multipliers, applied on top of the physics-driven gains. A change ramps across the
    /// next <see cref="Render"/> buffer like every other parameter, so it never clicks.</summary>
    public VoiceMix Mix { get; set; } = VoiceMix.Full;

    public void Reset()
    {
        _last = SynthParams.Silent;
        _lastMix = Mix;
        _bladePhase = _shaftPhase = _elecPhase = 0;
        _windState = _rollLow = _rollBand = _grain = _roarState = 0;
        _rng = (uint)_seed * 2654435761u | 1u;
    }

    public void Render(Span<float> buffer, in SynthParams target)
    {
        var from = _last;
        var fromMix = _lastMix;
        var toMix = Mix;
        int n = buffer.Length;
        double dt = 1.0 / SampleRate;
        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 1 : (double)(i + 1) / n;
            double blade = Lerp(from.BladePassHz, target.BladePassHz, t);
            double shaft = Lerp(from.ShaftHz, target.ShaftHz, t);
            double elec = Lerp(from.ElectricalHz, target.ElectricalHz, t);
            double prop = EngineMuted ? 0 : Lerp(from.PropGain, target.PropGain, t);
            double whine = EngineMuted ? 0 : Lerp(from.WhineGain, target.WhineGain, t);
            double wind = Lerp(from.WindGain, target.WindGain, t);
            double cutoff = Lerp(from.WindCutoffHz, target.WindCutoffHz, t);
            double roll = Lerp(from.RollGain, target.RollGain, t);
            double exhaust = EngineMuted ? 0 : Lerp(from.ExhaustGain, target.ExhaustGain, t);
            double roar = EngineMuted ? 0 : Lerp(from.RoarGain, target.RoarGain, t);
            double propMix = Lerp(fromMix.Propeller, toMix.Propeller, t);
            double motorMix = Lerp(fromMix.Motor, toMix.Motor, t);
            double windMix = Lerp(fromMix.Wind, toMix.Wind, t);
            double rollMix = Lerp(fromMix.Rolling, toMix.Rolling, t);

            _bladePhase = Wrap(_bladePhase + 2 * Math.PI * blade * dt);
            _shaftPhase = Wrap(_shaftPhase + 2 * Math.PI * shaft * dt);
            _elecPhase = Wrap(_elecPhase + 2 * Math.PI * elec * dt);

            double sample = 0;
            if (prop > 0)
            {
                double harmonics = 0;
                // Harmonics at or above Nyquist would fold back as spurious tones (a many-bladed fan passes several kHz).
                for (int k = 1; k <= PropHarmonics && k * blade < 0.5 * SampleRate; k++) harmonics += Math.Sin(k * _bladePhase) / Math.Pow(k, 1.2);
                sample += PropLevel * prop * harmonics * (1 + ShaftModulation * Math.Sin(_shaftPhase)) * propMix;
            }
            if (whine > 0)
                sample += WhineLevel * whine * (Math.Sin(_elecPhase) + 0.3 * Math.Sin(2 * _elecPhase)) * motorMix;
            if (exhaust > 0)
            {
                // Sharp pulses once per turn: equal-weight cosine harmonics up to Nyquist, gently rolled off.
                double pulses = 0;
                for (int k = 1; k <= ExhaustHarmonics && k * shaft < 0.5 * SampleRate; k++) pulses += Math.Cos(k * _shaftPhase) / Math.Sqrt(k);
                sample += ExhaustLevel * exhaust * pulses * motorMix;
            }
            if (roar > 0)
            {
                _roarState += (1 - Math.Exp(-2 * Math.PI * RoarCutoffHz * dt)) * (Noise() - _roarState);
                sample += RoarLevel * roar * _roarState * motorMix;
            }
            if (wind > 0 || roll > 0)
            {
                double noise = Noise();
                double a = 1 - Math.Exp(-2 * Math.PI * cutoff * dt);
                _windState += a * (noise - _windState);
                sample += WindLevel * wind * _windState * 2 * windMix;

                _rollLow += 0.02 * (noise - _rollLow);
                _rollBand += 0.3 * ((noise - _rollLow) - _rollBand);
                if (((_rng >> 8) & 0x3FF) == 0) _grain = 0.5 + 0.5 * Noise();
                _grain *= 0.9995;
                sample += RollLevel * roll * _rollBand * (0.6 + _grain) * rollMix;
            }
            buffer[i] = (float)Math.Tanh(sample);
        }
        _last = EngineMuted ? target with { PropGain = 0, WhineGain = 0 } : target;
        _lastMix = toMix;
    }

    double Noise()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return _rng / (double)uint.MaxValue * 2 - 1;
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static double Wrap(double phase) => phase >= 2 * Math.PI ? phase - 2 * Math.PI * Math.Floor(phase / (2 * Math.PI)) : phase;
}
