using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/cameras/god_rig.tscn. The rig node sits on the ground at the focus point and never rotates
/// (no yaw, ever); the child Camera3D orbits it in the rig's YZ plane at <see cref="Zoom"/> metres with a pitch
/// eased between <see cref="PitchAtMinZoom"/> and <see cref="PitchAtMaxZoom"/>. WASD/arrow keys and the
/// screen edges pan, the wheel zooms, a left click that no UI consumed emits <see cref="SignalName.PickRequested"/>.
/// </summary>
public partial class GodCameraRig : Node3D
{
    [Signal] public delegate void PickRequestedEventHandler(Vector2 screenPosition);

    [Export] public float MinZoom { get; set; } = 20f;
    [Export] public float MaxZoom { get; set; } = 400f;
    [Export] public float PitchAtMinZoom { get; set; } = 50f;
    [Export] public float PitchAtMaxZoom { get; set; } = 65f;
    [Export] public float ZoomStep { get; set; } = 1.15f;
    [Export] public float ZoomSharpness { get; set; } = 10f;
    /// <summary>Pan speed in screen-heights per second; scales with zoom so it feels the same at any height.</summary>
    [Export] public float PanSpeed { get; set; } = 1.1f;
    [Export] public int EdgePanMargin { get; set; } = 8;
    [Export] public bool EdgePan { get; set; } = true;

    private Camera3D? _camera;
    private float _targetZoom = 140f;
    private Rect2 _bounds = new(0, 0, 512, 512);
    private float _groundY;

    public float Zoom { get; private set; } = 140f;
    public float TargetZoom => _targetZoom;
    public float PitchDegrees => Mathf.Lerp(PitchAtMinZoom, PitchAtMaxZoom, (Zoom - MinZoom) / Math.Max(1e-3f, MaxZoom - MinZoom));
    public Camera3D? Camera => _camera;

    /// <summary>Called by Main with the snapshot ground height under the focus point.</summary>
    public Func<float, float, float>? GroundHeight { get; set; }

    public override void _Ready()
    {
        _camera = GetNodeOrNull<Camera3D>("Camera3D");
        Rotation = Vector3.Zero;
        ApplyCamera();
    }

    /// <summary>Area (world XZ, metres) the focus point is clamped to.</summary>
    public void SetBounds(Rect2 bounds)
    {
        _bounds = bounds;
        Focus(new Vector2(Position.X, Position.Z));
    }

    public void Focus(Vector2 worldXZ)
    {
        var clamped = new Vector2(
            Math.Clamp(worldXZ.X, _bounds.Position.X, _bounds.End.X),
            Math.Clamp(worldXZ.Y, _bounds.Position.Y, _bounds.End.Y));
        Position = new Vector3(clamped.X, _groundY, clamped.Y);
    }

    /// <summary>Sets the zoom target (clamped) and snaps to it when <paramref name="immediate"/>.</summary>
    public void SetZoom(float metres, bool immediate = false)
    {
        _targetZoom = Math.Clamp(metres, MinZoom, MaxZoom);
        if (immediate) Zoom = _targetZoom;
        ApplyCamera();
    }

    /// <summary>Positive steps zoom out, negative zoom in.</summary>
    public void ZoomBy(float steps) => SetZoom(_targetZoom * MathF.Pow(ZoomStep, steps));

    /// <summary>Pans by a direction in screen terms (x right, y down) for <paramref name="delta"/> seconds.</summary>
    public void Pan(Vector2 direction, double delta)
    {
        if (direction == Vector2.Zero) return;
        float speed = PanSpeed * Zoom * (float)delta;
        // No yaw: screen right is +X and screen up is -Z.
        Focus(new Vector2(Position.X, Position.Z) + direction.Normalized() * speed);
    }

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        bool typing = GetViewport()?.GuiGetFocusOwner() is LineEdit or TextEdit; // WASD in the dialogue box must not pan
        if (!typing)
        {
            if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) dir.Y -= 1;
            if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) dir.Y += 1;
            if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) dir.X -= 1;
            if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) dir.X += 1;
        }
        if (EdgePan && DisplayServer.GetName() != "headless" && GetWindow() is { } window && window.HasFocus())
        {
            var vp = GetViewport();
            var size = vp.GetVisibleRect().Size;
            var mouse = vp.GetMousePosition();
            if (mouse.X >= 0 && mouse.Y >= 0 && mouse.X <= size.X && mouse.Y <= size.Y)
            {
                if (mouse.X < EdgePanMargin) dir.X -= 1;
                else if (mouse.X > size.X - EdgePanMargin) dir.X += 1;
                if (mouse.Y < EdgePanMargin) dir.Y -= 1;
                else if (mouse.Y > size.Y - EdgePanMargin) dir.Y += 1;
            }
        }
        Pan(dir, delta);

        Zoom = Mathf.Lerp(Zoom, _targetZoom, 1f - MathF.Exp(-ZoomSharpness * (float)delta));
        if (MathF.Abs(Zoom - _targetZoom) < 0.01f) Zoom = _targetZoom;

        if (GroundHeight is not null)
        {
            float ground = GroundHeight(Position.X, Position.Z);
            _groundY = Mathf.Lerp(_groundY, ground, 1f - MathF.Exp(-4f * (float)delta));
            Position = new Vector3(Position.X, _groundY, Position.Z);
        }
        ApplyCamera();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mb) return;
        switch (mb.ButtonIndex)
        {
            case MouseButton.WheelUp:
                ZoomBy(-(mb.Factor > 0 ? mb.Factor : 1));
                break;
            case MouseButton.WheelDown:
                ZoomBy(mb.Factor > 0 ? mb.Factor : 1);
                break;
            case MouseButton.Left:
                EmitSignal(SignalName.PickRequested, mb.Position);
                break;
            default:
                return;
        }
        GetViewport()?.SetInputAsHandled();
    }

    private void ApplyCamera()
    {
        Rotation = Vector3.Zero; // the rig never yaws
        if (_camera is null) return;
        float pitch = Mathf.DegToRad(PitchDegrees);
        _camera.Position = new Vector3(0, MathF.Sin(pitch) * Zoom, MathF.Cos(pitch) * Zoom);
        _camera.Rotation = new Vector3(-pitch, 0, 0);
    }
}
