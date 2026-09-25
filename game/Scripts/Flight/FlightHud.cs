using System.Globalization;
using Godot;
using SimLab.App.Cameras;
using SimLab.App.Session;
using SimLab.App.Ui;

namespace SimLab.Game.Flight;

/// <summary>
/// Betaflight-style OSD: outlined monospace figures around the edges (speed left, height and vario right, home arrow
/// and heading on top, battery bottom left, throttle and timer bottom right, input and keys at the bottom) and, in
/// the FPV view only, the artificial horizon. The PAUSE banner stays visible when the OSD is off.
/// </summary>
public partial class FlightHud : CanvasLayer
{
    const int FontSize = 22;
    const int SmallSize = 15;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    Control _osd = null!;
    OsdHorizon _horizon = null!;
    OsdHomeArrow _homeArrow = null!;
    Label _home = null!, _heading = null!, _speed = null!, _height = null!, _vario = null!;
    Label _battery = null!, _throttle = null!, _timer = null!, _help = null!, _banner = null!;

    /// <summary>The OSD's monospace font, with fallbacks for macOS, Windows and Linux.</summary>
    public static Font MonoFont() => new SystemFont { FontNames = ["Menlo", "Consolas", "DejaVu Sans Mono", "monospace"] };

    public override void _Ready()
    {
        var big = new LabelSettings { Font = MonoFont(), FontSize = FontSize, FontColor = Colors.White, OutlineSize = 5, OutlineColor = Colors.Black };
        var small = new LabelSettings { Font = big.Font, FontSize = SmallSize, FontColor = Colors.White, OutlineSize = 4, OutlineColor = Colors.Black };

        _osd = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _osd.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_osd);

        _horizon = new OsdHorizon();
        _osd.AddChild(_horizon);

        _homeArrow = new OsdHomeArrow();
        _homeArrow.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _homeArrow.OffsetLeft = -18;
        _homeArrow.OffsetRight = 18;
        _homeArrow.OffsetTop = 22;
        _homeArrow.OffsetBottom = 58;
        _osd.AddChild(_homeArrow);
        _home = Add(big, 0.5f, 0f, 0, 78, HorizontalAlignment.Center);
        _heading = Add(big, 0.5f, 0f, 0, 110, HorizontalAlignment.Center);
        _speed = Add(big, 0f, 0.5f, 40, 0, HorizontalAlignment.Left);
        _height = Add(big, 1f, 0.5f, -40, 0, HorizontalAlignment.Right);
        _vario = Add(big, 1f, 0.5f, -40, 32, HorizontalAlignment.Right);
        _battery = Add(big, 0f, 1f, 40, -70, HorizontalAlignment.Left);
        _throttle = Add(big, 1f, 1f, -40, -102, HorizontalAlignment.Right);
        _timer = Add(big, 1f, 1f, -40, -70, HorizontalAlignment.Right);
        _help = Add(small, 0f, 1f, 24, -24, HorizontalAlignment.Left);

        _banner = Ui.Text("", 44);
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center);
        _banner.OffsetLeft = -150;
        _banner.OffsetRight = 150;
        _banner.OffsetTop = -120;
        _banner.OffsetBottom = -60;
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_banner);
    }

    /// <summary>
    /// A one-line label anchored at a fraction (fx, fy) of the screen and shifted by (dx, dy) px: its left edge,
    /// centre or right edge sits there depending on the alignment, and it is vertically centred on it.
    /// </summary>
    Label Add(LabelSettings settings, float fx, float fy, float dx, float dy, HorizontalAlignment align)
    {
        const float Width = 700, HalfHeight = 18;
        var label = new Label
        {
            LabelSettings = settings,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = fx, AnchorRight = fx, AnchorTop = fy, AnchorBottom = fy,
            OffsetTop = dy - HalfHeight, OffsetBottom = dy + HalfHeight,
        };
        (label.OffsetLeft, label.OffsetRight) = align switch
        {
            HorizontalAlignment.Left => (dx, dx + Width),
            HorizontalAlignment.Right => (dx - Width, dx),
            _ => (dx - Width / 2, dx + Width / 2),
        };
        _osd.AddChild(label);
        return label;
    }

    public void UpdateHud(FlightSession session, RouterOutput input, OsdData osd, bool shown, CameraView view)
    {
        _banner.Text = session.Paused ? Ui.T("HUD_PAUSED") : "";
        _osd.Visible = shown;
        if (!shown) return;

        _horizon.Visible = view == CameraView.Fpv;
        if (_horizon.Visible) _horizon.SetAttitude(osd.RollDeg, osd.PitchDeg);
        _homeArrow.SetBearing(osd.HomeRelativeBearingDeg);
        _home.Text = $"{osd.HomeDistanceM.ToString("0", Inv)} m";
        _heading.Text = $"{((int)System.Math.Round(osd.HeadingDeg) % 360).ToString("000", Inv)}°";
        _speed.Text = $"{osd.AirspeedKmh.ToString("0", Inv)} km/h";
        _height.Text = $"{osd.HeightM.ToString("0", Inv)} m";
        _vario.Text = $"{(osd.VarioMs >= 0 ? "↑" : "↓")} {System.Math.Abs(osd.VarioMs).ToString("0.0", Inv)} m/s";
        _battery.Text = osd.BatteryVolts is { } v
            ? $"{v.ToString("0.0", Inv)} V  {osd.CurrentAmps!.Value.ToString("0.0", Inv)} A  {osd.ConsumedMah!.Value.ToString("0", Inv)} mAh"
            : "— V";
        _throttle.Text = $"{Ui.T("OSD_THROTTLE")} {osd.ThrottlePercent.ToString("0", Inv)} %";
        int seconds = (int)System.Math.Max(0, osd.FlightTimeSeconds);
        _timer.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        string source = input.Source == InputSource.Radio ? $"{Ui.T("HUD_INPUT")} : {input.DeviceName}" : Ui.T("HUD_KEYBOARD");
        _help.Text = session.WindEnabled ? source : $"{source}   —   {Ui.T("WIND_OFF")}";
    }
}
