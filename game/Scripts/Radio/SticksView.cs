using Godot;
using SimLab.App.Ui;

namespace SimLab.Game.Radio;

/// <summary>
/// Two stick gimbals side by side, drawn like a radio seen from above: a rounded square with a cross hair and a live
/// blue dot per stick. During calibration a <see cref="StickGesture"/> adds the target (an orange ring, or a circling
/// arrow) on the gimbals it names and dims the other one.
/// </summary>
public partial class SticksView : Control
{
    const float Gap = 40, DotRadius = 9, RingRadius = 13, Dim = 0.35f;
    static readonly Color Frame = new(1, 1, 1, 0.25f), Back = new(1, 1, 1, 0.05f), Cross = new(1, 1, 1, 0.12f);

    StickPoint _left, _right;
    StickGesture? _gesture;

    public void Init(Vector2 size)
    {
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(StickPoint left, StickPoint right, StickGesture? gesture)
    {
        if (left == _left && right == _right && gesture == _gesture) return;
        (_left, _right, _gesture) = (left, right, gesture);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float side = Mathf.Max(0, Mathf.Min(Size.Y, (Size.X - Gap) / 2));
        if (side <= 0) return;
        float x0 = (Size.X - (2 * side + Gap)) / 2, y0 = (Size.Y - side) / 2;
        var g = _gesture;
        DrawGimbal(new Rect2(x0, y0, side, side), _left, g is { } a && !a.Left, g is { Left: true } l ? l : null);
        DrawGimbal(new Rect2(x0 + side + Gap, y0, side, side), _right, g is { } b && !b.Right, g is { Right: true } r ? r : null);
    }

    void DrawGimbal(Rect2 box, StickPoint stick, bool dim, StickGesture? gesture)
    {
        float alpha = dim ? Dim : 1;
        var frame = new StyleBoxFlat { BgColor = Faded(Back, alpha), BorderColor = Faded(Frame, alpha) };
        frame.SetBorderWidthAll(2);
        frame.SetCornerRadiusAll(12);
        DrawStyleBox(frame, box);

        var c = box.GetCenter();
        float half = box.Size.X / 2, travel = half - RingRadius - 6;
        var cross = Faded(Cross, alpha);
        DrawLine(new Vector2(box.Position.X + 10, c.Y), new Vector2(box.End.X - 10, c.Y), cross, 1, true);
        DrawLine(new Vector2(c.X, box.Position.Y + 10), new Vector2(c.X, box.End.Y - 10), cross, 1, true);

        if (gesture is { } gs)
        {
            if (gs.Circle) DrawCircleArrow(c, half * 0.8f);
            else DrawArc(c + At(gs.Target, travel), RingRadius, 0, Mathf.Tau, 40, Ui.Orange, 3, true);
        }
        DrawCircle(c + At(stick, travel), DotRadius, Faded(Ui.Accent, alpha), true, -1, true);
    }

    /// <summary>A clockwise arc of about 300° ending in an arrow head: "sweep the stick around its extremes".</summary>
    void DrawCircleArrow(Vector2 center, float radius)
    {
        const float start = -Mathf.Pi / 2 + 0.35f, end = start + Mathf.Tau - 0.9f;
        DrawArc(center, radius, start, end, 64, Ui.Orange, 3, true);
        var tip = center + radius * new Vector2(Mathf.Cos(end), Mathf.Sin(end));
        var tangent = new Vector2(-Mathf.Sin(end), Mathf.Cos(end));
        var normal = new Vector2(tangent.Y, -tangent.X);
        const float head = 11;
        DrawColoredPolygon([tip + tangent * head, tip - tangent * head * 0.2f + normal * head * 0.7f,
            tip - tangent * head * 0.2f - normal * head * 0.7f], Ui.Orange);
    }

    /// <summary>Stick position on screen: −1..1 each way, clamped, y up.</summary>
    static Vector2 At(StickPoint p, float travel) =>
        new((float)System.Math.Clamp(p.X, -1, 1) * travel, -(float)System.Math.Clamp(p.Y, -1, 1) * travel);

    static Color Faded(Color color, float alpha) => new(color, color.A * alpha);
}
