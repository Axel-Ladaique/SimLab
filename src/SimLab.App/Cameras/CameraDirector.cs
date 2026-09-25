namespace SimLab.App.Cameras;

public enum CameraView { Ground, Fpv, Chase }

/// <summary>Owns one rig per view and drives the current one. Switching resets the new rig so it starts on target.</summary>
public sealed class CameraDirector
{
    readonly ICameraRig[] _rigs;

    public CameraDirector(ICameraRig ground, ICameraRig fpv, ICameraRig chase, CameraView initial)
    {
        _rigs = [ground, fpv, chase];
        Current = Enum.IsDefined(initial) ? initial : CameraView.Ground;
    }

    /// <summary>The view currently driving the camera.</summary>
    public CameraView Current { get; private set; }

    ICameraRig Rig => _rigs[(int)Current];

    /// <summary>Ground → FPV → chase → ground.</summary>
    public void Next(in CameraContext ctx) => Select((CameraView)(((int)Current + 1) % _rigs.Length), ctx);

    /// <summary>Jumps to <paramref name="view"/> and resets its rig; an undefined value is ignored and the
    /// current view is kept (guards a malformed command-line or radio value).</summary>
    public void Select(CameraView view, in CameraContext ctx)
    {
        if (!Enum.IsDefined(view)) return;
        Current = view;
        Rig.Reset(ctx);
    }

    /// <summary>Snaps the current rig back onto its target with no smoothing (flight reset).</summary>
    public void Reset(in CameraContext ctx) => Rig.Reset(ctx);

    /// <summary>Advances the current rig and returns the pose to render this frame.</summary>
    public CameraPose Update(double dt, in CameraContext ctx) => Rig.Update(dt, ctx);
}
