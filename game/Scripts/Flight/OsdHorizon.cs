using Godot;

namespace SimLab.Game.Flight;

/// <summary>
/// FPV artificial horizon, Betaflight style: a fixed crosshair, and a horizon line with a pitch ladder every 10°
/// that moves with pitch and turns with roll. A synthetic indicator, not aligned with the camera image. Clipped to
/// its own box, which is centred on the screen.
/// </summary>
public partial class OsdHorizon : Control
{
    public static readonly Vector2 BoxSize = new(420, 300);
    const float PixelsPerDegree = 8f;
    const float HalfWidth = 180f;
    const float Gap = 34f;
    const float RungHalfWidth = 60f;
    const float Line = 3f;

    float _rollRad;
    float _pitchDeg;
    Font _font = null!;

    /// <summary>The OSD's shared monospace font, set once by the HUD before this control is added to the tree.</summary>
    public void Init(Font font) => _font = font;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        SetAnchorsPreset(LayoutPreset.Center);
        OffsetLeft = -BoxSize.X / 2;
        OffsetRight = BoxSize.X / 2;
        OffsetTop = -BoxSize.Y / 2;
        OffsetBottom = BoxSize.Y / 2;
    }

    public void SetAttitude(double rollDeg, double pitchDeg)
    {
        _rollRad = Mathf.DegToRad((float)rollDeg);
        _pitchDeg = (float)pitchDeg;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var centre = BoxSize / 2;
        // Crosshair, fixed: —(+)—
        Outlined(centre + new Vector2(-44, 0), centre + new Vector2(-14, 0));
        Outlined(centre + new Vector2(14, 0), centre + new Vector2(44, 0));
        DrawArc(centre, 8, 0, Mathf.Tau, 20, Colors.Black, Line + 2);
        DrawArc(centre, 8, 0, Mathf.Tau, 20, Colors.White, Line);

        // Horizon and ladder: a right roll turns the world counter-clockwise on screen (Godot angles are clockwise,
        // y down), and a nose-up pitch moves the horizon down.
        DrawSetTransform(centre, -_rollRad, Vector2.One);
        float y0 = _pitchDeg * PixelsPerDegree;
        Outlined(new Vector2(-HalfWidth, y0), new Vector2(-Gap, y0));
        Outlined(new Vector2(Gap, y0), new Vector2(HalfWidth, y0));
        for (int deg = -30; deg <= 30; deg += 10)
        {
            if (deg == 0) continue;
            float y = y0 - deg * PixelsPerDegree;
            // Positive (nose-up) rungs are solid, negative (nose-down) rungs are dashed, Betaflight style.
            if (deg > 0)
            {
                Outlined(new Vector2(-RungHalfWidth, y), new Vector2(-Gap, y));
                Outlined(new Vector2(Gap, y), new Vector2(RungHalfWidth, y));
            }
            else
            {
                for (float x = -RungHalfWidth; x < -Gap; x += 14) Outlined(new Vector2(x, y), new Vector2(x + 8, y));
                for (float x = Gap; x < RungHalfWidth; x += 14) Outlined(new Vector2(x, y), new Vector2(x + 8, y));
            }
            string label = deg.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DrawStringOutline(_font, new Vector2(RungHalfWidth + 6, y + 6), label, HorizontalAlignment.Left, -1, 16, 4, Colors.Black);
            DrawString(_font, new Vector2(RungHalfWidth + 6, y + 6), label, HorizontalAlignment.Left, -1, 16, Colors.White);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    void Outlined(Vector2 a, Vector2 b)
    {
        DrawLine(a, b, Colors.Black, Line + 2);
        DrawLine(a, b, Colors.White, Line);
    }
}
