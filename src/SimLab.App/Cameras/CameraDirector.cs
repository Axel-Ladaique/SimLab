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

    public CameraView Current { get; private set; }

    ICameraRig Rig => _rigs[(int)Current];

    /// <summary>Ground → FPV → chase → ground.</summary>
    public void Next(in CameraContext ctx) => Select((CameraView)(((int)Current + 1) % _rigs.Length), ctx);

    public void Select(CameraView view, in CameraContext ctx)
    {
        Current = view;
        Rig.Reset(ctx);
    }

    public void Reset(in CameraContext ctx) => Rig.Reset(ctx);

    public CameraPose Update(double dt, in CameraContext ctx) => Rig.Update(dt, ctx);
}
