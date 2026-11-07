using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/cameras/possess_rig.tscn: the M6 possession camera. The rig sits on the possessed body
/// (<see cref="Target"/>, smoothed); <c>Yaw/Pitch</c> pivots turn with the mouse; a <see cref="SpringArm3D"/> holds the
/// third-person camera target behind the shoulder and pulls it in when terrain is in the way (collision mask =
/// <see cref="TerrainCollision.Layer"/>). <see cref="FirstPerson"/> (F) shortens the arm to zero at eye height. The
/// actual <see cref="Camera3D"/> is top-level and follows the arm's end, gliding in from the god camera on
/// <see cref="Activate"/> (the cross-fade between the two rigs).
/// </summary>
public partial class PossessRig : Node3D
{
    [Export] public float ArmLength { get; set; } = 4.2f;
    [Export] public float ShoulderHeight { get; set; } = 1.55f;
    [Export] public float ShoulderOffset { get; set; } = 0.45f;
    [Export] public float EyeHeight { get; set; } = 1.62f;
    /// <summary>Radians per pixel of mouse motion.</summary>
    [Export] public float MouseSensitivity { get; set; } = 0.0035f;
    [Export] public float MinPitchDegrees { get; set; } = -70f;
    [Export] public float MaxPitchDegrees { get; set; } = 55f;
    [Export] public float FollowSharpness { get; set; } = 18f;
    [Export] public double GlideSeconds { get; set; } = 0.7;

    private Node3D _yaw = null!, _pitch = null!, _end = null!;
    private SpringArm3D _arm = null!;
    private Camera3D _camera = null!;
    private Transform3D _glideFrom;
    private double _glideTime, _glideDuration;
    private bool _snap = true;

    /// <summary>Body position (feet) the rig follows.</summary>
    public Vector3 Target { get; set; }
    /// <summary>Height scale of the body (children are shorter): moves the shoulder and eye.</summary>
    public float BodyScale { get; set; } = 1f;
    public bool Active { get; private set; }
    public bool FirstPerson { get; private set; }
    /// <summary>Yaw of the view in radians (0 looks toward -Z).</summary>
    public float Yaw { get; set; }
    /// <summary>Pitch in radians, negative looks down.</summary>
    public float Pitch { get; set; } = -0.25f;
    public Camera3D Camera => _camera;
    public SpringArm3D Arm => _arm;
    public bool Gliding => _glideTime < _glideDuration;

    /// <summary>Horizontal forward of the view (where W walks), unit length, Y = 0.</summary>
    public Vector3 Forward => new(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
    /// <summary>Horizontal right of the view (where D walks).</summary>
    public Vector3 Right => new(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));

    public override void _Ready() => Initialise();

    /// <summary>Finds or builds the pivot chain. Idempotent; tests may call it without a scene tree.</summary>
    public void Initialise()
    {
        if (_camera is not null) return;
        _yaw = GetNodeOrNull<Node3D>("Yaw") ?? Add(this, new Node3D { Name = "Yaw" });
        _pitch = _yaw.GetNodeOrNull<Node3D>("Pitch") ?? Add(_yaw, new Node3D { Name = "Pitch" });
        _arm = _pitch.GetNodeOrNull<SpringArm3D>("SpringArm3D") ?? Add(_pitch, new SpringArm3D { Name = "SpringArm3D" });
        _end = _arm.GetNodeOrNull<Node3D>("End") ?? Add(_arm, new Node3D { Name = "End" });
        _camera = GetNodeOrNull<Camera3D>("Camera3D") ?? Add(this, new Camera3D { Name = "Camera3D", Fov = 70, Near = 0.08f, Far = 5000 });
        _camera.TopLevel = true;
        _camera.Current = false;
        _arm.CollisionMask = TerrainCollision.Layer;
        _arm.Margin = 0.25f;
        _arm.Shape ??= new SphereShape3D { Radius = 0.25f };
        Visible = true;
        ApplyPivots();
    }

    private static T Add<T>(Node parent, T child) where T : Node
    {
        parent.AddChild(child);
        return child;
    }

    /// <summary>Takes over the view: the camera glides from <paramref name="from"/> (the god camera) to behind the body.</summary>
    public void Activate(Vector3 target, float yaw, Transform3D from)
    {
        Initialise();
        Target = target;
        Position = target;
        Yaw = yaw;
        Pitch = -0.25f;
        Active = true;
        _snap = true;
        _glideFrom = from;
        _glideTime = 0;
        _glideDuration = GlideSeconds;
        ApplyPivots();
        if (_camera.IsInsideTree())
        {
            _camera.GlobalTransform = from;
            _camera.MakeCurrent();
        }
    }

    public void Deactivate()
    {
        Active = false;
        if (_camera is not null && _camera.IsInsideTree() && _camera.Current) _camera.ClearCurrent(false);
    }

    public void SetFirstPerson(bool on)
    {
        FirstPerson = on;
        ApplyPivots();
    }

    public void ToggleFirstPerson() => SetFirstPerson(!FirstPerson);

    /// <summary>Mouse look in pixels of motion (x right, y down).</summary>
    public void Look(Vector2 relative)
    {
        Yaw -= relative.X * MouseSensitivity;
        Yaw = Mathf.Wrap(Yaw, -MathF.PI, MathF.PI);
        Pitch = Math.Clamp(Pitch - relative.Y * MouseSensitivity, Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));
        ApplyPivots();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Active || Input.MouseMode != Input.MouseModeEnum.Captured) return;
        if (@event is InputEventMouseMotion motion)
        {
            Look(motion.ScreenRelative);
            GetViewport()?.SetInputAsHandled();
        }
    }

    public override void _Process(double delta) => Step(delta);

    /// <summary>Follows the target and places the camera (public for tests).</summary>
    public void Step(double delta)
    {
        if (!Active) return;
        float k = _snap ? 1f : 1f - MathF.Exp(-FollowSharpness * (float)delta);
        _snap = false;
        Position = Position.Lerp(Target, k);
        ApplyPivots();
        if (!_camera.IsInsideTree()) return;
        var wanted = new Transform3D(_pitch.GlobalBasis, _end.GlobalPosition);
        if (_glideTime < _glideDuration)
        {
            _glideTime += delta;
            float t = Mathf.SmoothStep(0f, 1f, (float)Math.Min(1.0, _glideTime / _glideDuration));
            wanted = _glideFrom.InterpolateWith(wanted, t);
        }
        _camera.GlobalTransform = wanted;
    }

    private void ApplyPivots()
    {
        if (_yaw is null) return;
        _yaw.Rotation = new Vector3(0, Yaw, 0);
        _pitch.Rotation = new Vector3(Pitch, 0, 0);
        if (FirstPerson)
        {
            _pitch.Position = new Vector3(0, EyeHeight * BodyScale, 0);
            _arm.Position = Vector3.Zero;
            _arm.SpringLength = 0f;
        }
        else
        {
            _pitch.Position = new Vector3(0, ShoulderHeight * BodyScale, 0);
            _arm.Position = new Vector3(ShoulderOffset, 0, 0);
            _arm.SpringLength = ArmLength;
        }
    }
}
