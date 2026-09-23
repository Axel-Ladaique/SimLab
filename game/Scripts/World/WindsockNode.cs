using Godot;
using Symlab.App.Field;

namespace Symlab.Game.World;

/// <summary>6 m pole with an orange sock that points downwind and droops in light air.</summary>
public partial class WindsockNode : Node3D
{
    const float PoleHeight = 6f;
    Node3D _sock = null!;

    public override void _Ready()
    {
        var pole = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = PoleHeight },
            Position = new Vector3(0, PoleHeight / 2, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.85f, 0.85f) },
        };
        AddChild(pole);

        _sock = new Node3D { Position = new Vector3(0, PoleHeight, 0) };
        var cone = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.30f, BottomRadius = 0.12f, Height = 1.6f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.45f, 0.05f) },
            Position = new Vector3(0, 0, -0.8f),
            RotationDegrees = new Vector3(90, 0, 0),
        };
        _sock.AddChild(cone);
        AddChild(_sock);
    }

    public void Apply(WindsockPose pose) =>
        _sock.Rotation = new Vector3(-Mathf.DegToRad((float)pose.DroopDeg), -Mathf.DegToRad((float)pose.HeadingDeg), 0);
}
