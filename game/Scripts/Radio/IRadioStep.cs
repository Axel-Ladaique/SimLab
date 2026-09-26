using SimLab.App.Session;
using SimLab.Flight.Airframe;
using SimLab.Flight.Controls;
using SimLab.Input;

namespace SimLab.Game.Radio;

/// <summary>What the radio screen polled this frame, handed to the current step.</summary>
/// <param name="Pads">Every radio seen (the demo pad alone in demo mode).</param>
/// <param name="Pad">The selected radio, null when none is plugged in.</param>
/// <param name="Profile">Its calibration and switches, null when it is not calibrated.</param>
/// <param name="Inputs">The live controls (the forced ones in screenshot modes).</param>
/// <param name="Board">The selected radio's switch positions, null when it is not calibrated.</param>
/// <param name="Aircraft">The aircraft of the live view, for the control-check readouts.</param>
public readonly record struct RadioFrame(double Delta, IReadOnlyList<JoypadSnapshot> Pads, JoypadSnapshot? Pad,
    RadioProfile? Profile, ControlInputs Inputs, SwitchBoard? Board, Aircraft? Aircraft);

/// <summary>One step of the radio screen's guided setup: its content control plus the one primary action it offers.</summary>
public interface IRadioStep
{
    /// <summary>Text of the orange primary button under the step; null hides it.</summary>
    string? PrimaryText { get; }

    void Primary();

    /// <summary>Called every frame while the step is shown.</summary>
    void Refresh(in RadioFrame frame);

    /// <summary>The screen moves to another step: stop whatever runs (calibration, learning).</summary>
    void Leave();
}
