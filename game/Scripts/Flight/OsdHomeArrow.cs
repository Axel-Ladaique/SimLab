using Godot;

namespace SimLab.Game.Flight;

/// <summary>Arrow pointing at the pilot: straight up means ahead, turned by the relative bearing (right positive).</summary>
public partial class OsdHomeArrow : Control
{
    float _bearingRad;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(36, 36);
        Size = CustomMinimumSize;
    }

    public void SetBearing(double relativeDeg)
    {
        _bearingRad = Mathf.DegToRad((float)relativeDeg);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawSetTransform(Size / 2, _bearingRad, Vector2.One);
        Vector2[] arrow = [new(0, -15), new(10, 10), new(0, 4), new(-10, 10)];
        DrawColoredPolygon(arrow, Colors.White);
        DrawPolyline([.. arrow, arrow[0]], Colors.Black, 2f, true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
