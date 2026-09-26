namespace SimLab.App.Audio;

/// <summary>
/// Aircraft voice synthesized sample by sample: propeller (blade-pass fundamental plus decaying harmonics, amplitude
/// modulated at the shaft rate), brushless whine (electrical frequency and its second harmonic), piston exhaust (one
/// combustion pop per turn: a ringing muffler resonance plus a noise burst over a low pulse-train body, with cycle-to-cycle
/// loudness and timing scatter that grows towards idle), turbine spool (a wavering whistle at the shaft rate over an intake
/// hiss), turbine roar (noise that brightens with thrust, over a tearing low rumble), wind (low-passed noise) and rolling (band-limited noise with
/// random grain). Exhaust, spool and roar share the motor level of the voice mix. Parameters ramp linearly across each buffer and
/// oscillator phases carry over, so parameter changes never click.
/// </summary>
public sealed class EngineSynth
{
    // Mix levels, tuned by ear.
    const double PropLevel = 0.35, WhineLevel = 0.08, WindLevel = 0.25, RollLevel = 0.2;
    const int PropHarmonics = 5, ExhaustHarmonics = 8;
    const double ShaftModulation = 0.15;

    // Piston exhaust. Each firing rings the muffler (a damped sine whose pitch rises with load) and spits a short noise
    // burst; a cosine pulse train underneath keeps a solid fundamental. ExhaustGain runs from the idle share (~0.3) to 1
    // at full power, and doubles as the load: the lower it is, the more the firings scatter and the more often they miss.
    const double ExhaustBodyLevel = 0.12, PopLevel = 0.8, BurstLevel = 0.5;
    const double PopDecay = 0.0045, BurstDecay = 0.0015, PopAttack = 0.0002;
    const double PopResonanceIdleHz = 160, PopResonanceRangeHz = 260, BurstCutoffHz = 3500;
    const double IdleExhaustGain = 0.3, TimingScatter = 0.1, LoudnessScatter = 0.6, IdleMissChance = 0.08;

    // Turbine. The spool whistle is a nearly pure tone at the shaft rate with a slow random pitch waver; the intake adds
    // a high hiss. The roar is noise whose low-pass opens with RoarGain (thrust), plus a deep rumble whose loudness is
    // torn by a slow random envelope.
    const double SpoolLevel = 0.22, SpoolHissLevel = 0.12, SpoolWaver = 0.004, SpoolWaverHz = 2.5, SpoolHissCornerHz = 5000;
    const double RoarLevel = 0.55, RoarIdleCutoffHz = 350, RoarCutoffRangeHz = 2400;
    const double RumbleLevel = 3.5, RumbleCutoffHz = 110, TearDepth = 0.45, TearHz = 7;

    readonly int _seed;
    SynthParams _last = SynthParams.Silent;
    VoiceMix _lastMix = VoiceMix.Full;
    double _bladePhase, _shaftPhase, _elecPhase;
    double _windState, _rollLow, _rollBand, _grain, _roarState;
    double _firePhase, _fireStretch = 1, _popAge = double.MaxValue, _popAmp, _burstState;
    double _spoolPhase, _waver, _hissLow, _rumble, _tear;
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
        _firePhase = 0; _fireStretch = 1; _popAge = double.MaxValue; _popAmp = 0; _burstState = 0;
        _spoolPhase = _waver = _hissLow = _rumble = _tear = 0;
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
            double spool = EngineMuted ? 0 : Lerp(from.SpoolGain, target.SpoolGain, t);
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
                // Low body: equal-weight cosine harmonics of the firing rate, gently rolled off, below Nyquist.
                double pulses = 0;
                for (int k = 1; k <= ExhaustHarmonics && k * shaft < 0.5 * SampleRate; k++) pulses += Math.Cos(k * _shaftPhase) / Math.Sqrt(k);
                sample += ExhaustBodyLevel * exhaust * pulses * motorMix;

                double roughness = Math.Clamp((1 - exhaust) / (1 - IdleExhaustGain), 0, 1);
                _firePhase += shaft * dt / _fireStretch;
                if (_firePhase >= 1)
                {
                    _firePhase -= Math.Floor(_firePhase);
                    _popAge = 0;
                    bool miss = Noise() * 0.5 + 0.5 < IdleMissChance * roughness;
                    _popAmp = miss ? 0.25 : 1 + LoudnessScatter * roughness * 0.5 * Noise();
                    _fireStretch = 1 + TimingScatter * roughness * Noise();
                }
                if (_popAge < 12 * PopDecay)
                {
                    double onset = 1 - Math.Exp(-_popAge / PopAttack);
                    double ring = Math.Sin(2 * Math.PI * (PopResonanceIdleHz + PopResonanceRangeHz * exhaust) * _popAge) * Math.Exp(-_popAge / PopDecay);
                    _burstState += (1 - Math.Exp(-2 * Math.PI * BurstCutoffHz * dt)) * (Noise() - _burstState);
                    double burst = _burstState * Math.Exp(-_popAge / BurstDecay);
                    sample += exhaust * _popAmp * onset * (PopLevel * ring + BurstLevel * burst) * motorMix;
                    _popAge += dt;
                }
            }
            if (spool > 0)
            {
                double waver = SlowNoise(ref _waver, SpoolWaverHz, dt);
                _spoolPhase = Wrap(_spoolPhase + 2 * Math.PI * shaft * (1 + SpoolWaver * waver) * dt);
                double tone = 0;
                for (int k = 1; k <= 3 && k * shaft < 0.5 * SampleRate; k++) tone += Math.Sin(k * _spoolPhase) * (k == 1 ? 1 : 0.35 / k);
                double noise = Noise();
                _hissLow += (1 - Math.Exp(-2 * Math.PI * SpoolHissCornerHz * dt)) * (noise - _hissLow);
                sample += spool * (SpoolLevel * tone + SpoolHissLevel * (noise - _hissLow)) * motorMix;
            }
            if (roar > 0)
            {
                double roarCutoff = RoarIdleCutoffHz + RoarCutoffRangeHz * roar;
                _roarState += (1 - Math.Exp(-2 * Math.PI * roarCutoff * dt)) * (Noise() - _roarState);
                _rumble += (1 - Math.Exp(-2 * Math.PI * RumbleCutoffHz * dt)) * (Noise() - _rumble);
                double torn = Math.Max(0, 1 + TearDepth * SlowNoise(ref _tear, TearHz, dt));
                sample += roar * (RoarLevel * _roarState + RumbleLevel * _rumble * torn) * motorMix;
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
        _last = EngineMuted ? target with { PropGain = 0, WhineGain = 0, ExhaustGain = 0, RoarGain = 0, SpoolGain = 0 } : target;
        _lastMix = toMix;
    }

    double Noise()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return _rng / (double)uint.MaxValue * 2 - 1;
    }

    /// <summary>White noise through a one-pole low-pass at <paramref name="hz"/>, rescaled to unit standard deviation
    /// (a low corner would otherwise leave it far quieter than the input).</summary>
    double SlowNoise(ref double state, double hz, double dt)
    {
        double a = 1 - Math.Exp(-2 * Math.PI * hz * dt);
        state += a * (Noise() - state);
        return state * Math.Sqrt(3 * (2 - a) / a);
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static double Wrap(double phase) => phase >= 2 * Math.PI ? phase - 2 * Math.PI * Math.Floor(phase / (2 * Math.PI)) : phase;
}
