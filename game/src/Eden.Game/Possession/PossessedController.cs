using Eden.Game.Art;
using Eden.Sim;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// M6: the possessed body. A <see cref="CharacterBody3D"/> (capsule) that walks on <see cref="TerrainCollision"/> and
/// carries its own hi-LOD <see cref="NpcHi"/> (the renderer skips the possessed NPC).
/// <para>
/// Movement is predicted locally and the sim is the authority: WASD (relative to the camera yaw, Shift sprints) moves
/// the body at the sim's own possessed speed (<see cref="World.PossessedSpeed"/> × possession tick rate), and every
/// physics frame a <see cref="PossessedMoveCommand"/> asks the sim to walk its NPC toward the body: the command's
/// direction is the gap between the sim position and the body in units of one tick's step (length ≤ 1), so the sim
/// ends each step exactly where the body is and stops when it stops. When the sim refuses (water, the edge of the
/// world) the gap grows and the body is pulled back to the sim position. With no input and no gap the body follows
/// the sim (interpolated between snapshots like the renderer), which is what "work alongside" looks like.
/// </para>
/// </summary>
public partial class PossessedController : CharacterBody3D
{
    [Export] public float Gravity { get; set; } = 20f;
    /// <summary>Body further than this from the sim (beyond what one tick of lag explains) is pulled back.</summary>
    [Export] public float MaxLead { get; set; } = 3.2f;
    /// <summary>Beyond this the body snaps to the sim position (teleports, loads).</summary>
    [Export] public float SnapDistance { get; set; } = 10f;
    /// <summary>Deepest wading the client allows before stopping the body (the sim allows 3 m).</summary>
    [Export] public float MaxWadeDepth { get; set; } = 0.9f;
    /// <summary>A drop deeper than this under the body makes it fall instead of following the ground.</summary>
    [Export] public float StepDown { get; set; } = 1.2f;

    /// <summary>On the ground (following it) rather than falling under physics.</summary>
    public bool Grounded { get; private set; } = true;

    private ISimHost? _host;
    private SimConfig _config = new();
    private NpcHi? _body;
    private NpcView _view;
    private long _lastTick = -1;
    private Vector2 _simPrev, _simCur;
    private double _simPrevAt, _simCurAt, _stuckFor;
    private Vector2 _lastCmd = new(float.NaN, float.NaN);
    private bool _lastSprint;
    private float _heading;
    private bool _following;

    public int NpcId { get; private set; } = -1;
    public bool Active => NpcId >= 0;
    /// <summary>Camera rig whose yaw turns WASD into world directions.</summary>
    public PossessRig? Rig { get; set; }
    /// <summary>Terrain height under a point (sim heights) for the wading check; null skips it.</summary>
    public Func<float, float, float>? SimGround { get; set; }
    /// <summary>Drawn ground height, never fallen through.</summary>
    public Func<float, float, float>? RenderGround { get; set; }
    public float SeaLevel { get; set; }
    /// <summary>Time scale the sim is running at (Paused = 0): the body cannot outrun a stopped world.</summary>
    public Func<TimeScale>? TimeScaleSource { get; set; }
    /// <summary>False while typing (dialogue) or when the window lost the player: WASD does nothing.</summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>Movement input in camera terms (x right, y forward), set by tests or scripts instead of the keyboard.</summary>
    public Vector2? ScriptedInput { get; set; }
    public bool ScriptedSprint { get; set; }

    /// <summary>Last input (x right, y forward), for the HUD and tests.</summary>
    public Vector2 InputVector { get; private set; }
    public bool Sprinting { get; private set; }
    /// <summary>Horizontal gap body − sim, metres.</summary>
    public float Gap { get; private set; }
    public Vector2 SimPosition => _simCur;
    public int CommandsSent { get; private set; }
    public int Corrections { get; private set; }
    /// <summary>True when the last step was refused because the water ahead is too deep.</summary>
    public bool WaterAhead { get; private set; }
    public NpcHi? Body => _body;
    public float Heading => _heading;
    /// <summary>True while the body follows the sim instead of leading it (idle after catching up, work alongside).</summary>
    public bool Following => _following;

    /// <summary>Let the sim move the body until the next movement input (used for "work alongside").</summary>
    public void FollowSim()
    {
        _following = true;
        _stuckFor = 0;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = TerrainCollision.Layer;
        FloorSnapLength = 0.8f;
        FloorMaxAngle = Mathf.DegToRad(60f);
        FloorStopOnSlope = true;
        FloorConstantSpeed = true;
        if (GetNodeOrNull<CollisionShape3D>("Capsule") is null)
            AddChild(new CollisionShape3D
            {
                Name = "Capsule",
                Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.7f },
                Position = new Vector3(0, 0.85f, 0),
            });
        if (_body is null && ResourceLoader.Exists("res://scenes/npc/npc_hi.tscn"))
        {
            _body = GD.Load<PackedScene>("res://scenes/npc/npc_hi.tscn").Instantiate<NpcHi>();
            _body.Name = "Body";
            AddChild(_body);
            _body.Release();
        }
        SetPhysicsProcess(false);
    }

    /// <summary>Takes over <paramref name="view"/>'s body at its snapshot position.</summary>
    public void Begin(ISimHost host, SimConfig config, in NpcView view)
    {
        _host = host;
        _config = config;
        _view = view;
        NpcId = view.Id;
        _heading = view.Heading;
        _simPrev = _simCur = new Vector2(view.X, view.Z);
        _simPrevAt = _simCurAt = Now();
        _lastTick = host.Latest.Tick;
        _lastCmd = new Vector2(float.NaN, float.NaN);
        _stuckFor = 0;
        _following = true;
        float y = RenderGround?.Invoke(view.X, view.Z) ?? view.Y;
        GlobalPosition = new Vector3(view.X, y, view.Z);
        Velocity = Vector3.Zero;
        Grounded = true;
        Visible = true;
        SetPhysicsProcess(true);
    }

    public void End()
    {
        if (_host is not null && NpcId >= 0) _host.Enqueue(new PossessedMoveCommand(0, 0, false));
        NpcId = -1;
        Visible = false;
        _body?.Release();
        SetPhysicsProcess(false);
    }

    /// <summary>Metres per real second at the current time scale for the stage and gait (0 when paused).</summary>
    public float SpeedMetresPerSecond(bool sprint)
    {
        float scale = (float)(int)(TimeScaleSource?.Invoke() ?? TimeScale.X1);
        return World.PossessedSpeed(_config, _view.Stage, sprint) * _config.PossessionTicksPerRealSecond * scale;
    }

    public override void _PhysicsProcess(double delta) => Step(delta);

    /// <summary>One physics step: read input, move the body, reconcile with the sim, send the command (public for tests).</summary>
    public void Step(double delta)
    {
        if (_host is null || NpcId < 0) return;
        float dt = (float)delta;
        ReadSim();

        var input = ScriptedInput ?? (InputEnabled ? KeyboardInput() : Vector2.Zero);
        bool sprint = ScriptedInput is not null ? ScriptedSprint : InputEnabled && Input.IsPhysicalKeyPressed(Key.Shift);
        if (input.LengthSquared() > 1f) input = input.Normalized();
        InputVector = input;
        Sprinting = sprint && input != Vector2.Zero;

        var pos = GlobalPosition;
        var flat = new Vector2(pos.X, pos.Z);
        Vector3 wish = Vector3.Zero;
        if (input != Vector2.Zero)
        {
            var fwd = Rig?.Forward ?? new Vector3(0, 0, -1);
            var right = Rig?.Right ?? new Vector3(1, 0, 0);
            wish = (right * input.X + fwd * input.Y) * SpeedMetresPerSecond(sprint);
            // Do not walk into deep water: the sim would refuse and pull the body back anyway.
            if (SimGround is not null && wish != Vector3.Zero)
            {
                var ahead = pos + wish.Normalized() * 0.6f;
                // Deeper than the wading limit and deeper than here: stop. Toward shallower water is always allowed.
                float there = SimGround(ahead.X, ahead.Z);
                WaterAhead = there < SeaLevel - MaxWadeDepth && there < SimGround(pos.X, pos.Z) - 0.02f;
                if (WaterAhead) wish = Vector3.Zero;
            }
        }

        Gap = (flat - _simCur).Length();
        var velocity = new Vector3(wish.X, Velocity.Y, wish.Z);
        if (wish != Vector3.Zero)
        {
            _following = false;
            _stuckFor = 0;
        }
        else if (!_following)
        {
            // Stopped: the sim takes one more step onto the body, then the body follows the sim again.
            _stuckFor += delta;
            if (Gap <= 0.15f || _stuckFor > 1.0) _following = true;
        }
        if (_following)
        {
            var target = SimInterpolated();
            var pull = (target - flat) * Math.Min(1f, 12f * dt);
            velocity.X = pull.X / Math.Max(dt, 1e-4f);
            velocity.Z = pull.Y / Math.Max(dt, 1e-4f);
        }

        // Authority: the body may lead the sim by about one tick of walking, no more.
        float lead = MaxLead * (sprint ? _config.PossessedSprintFactor : 1f);
        if (Gap > SnapDistance)
        {
            GlobalPosition = new Vector3(_simCur.X, RenderGround?.Invoke(_simCur.X, _simCur.Y) ?? pos.Y, _simCur.Y);
            velocity = Vector3.Zero;
            Corrections++;
        }
        else if (Gap > lead)
        {
            var back = (_simCur - flat).Normalized() * (Gap - lead) / Math.Max(dt, 1e-4f) * 0.5f;
            velocity.X += back.X;
            velocity.Z += back.Y;
            Corrections++;
        }

        // Horizontal: the terrain is a height field with no overhangs and the sim decides where a body may stand, so
        // the body is moved directly (no capsule snagging on terraces and banks). Grounded, it follows the drawn
        // ground; over a drop of more than StepDown it falls: the capsule and MoveAndSlide against the terrain
        // collision take over until it lands (floor contact or the ground sample).
        GlobalPosition += new Vector3(velocity.X, 0, velocity.Z) * dt;
        var p = GlobalPosition;
        float ground = RenderGround?.Invoke(p.X, p.Z) ?? p.Y;
        if (Grounded && p.Y - ground > StepDown) Grounded = false; // walked off a ledge
        if (Grounded)
        {
            GlobalPosition = new Vector3(p.X, ground, p.Z);
            Velocity = new Vector3(velocity.X, 0, velocity.Z);
        }
        else
        {
            Velocity = new Vector3(0, Velocity.Y - Gravity * dt, 0);
            if (IsInsideTree()) MoveAndSlide();
            else GlobalPosition += Velocity * dt;
            float g = RenderGround?.Invoke(GlobalPosition.X, GlobalPosition.Z) ?? GlobalPosition.Y;
            if ((IsInsideTree() && IsOnFloor()) || GlobalPosition.Y <= g + 0.02f)
            {
                Grounded = true;
                GlobalPosition = new Vector3(GlobalPosition.X, MathF.Max(GlobalPosition.Y, g), GlobalPosition.Z);
            }
            Velocity = new Vector3(velocity.X, Grounded ? 0 : Velocity.Y, velocity.Z);
        }

        if (wish != Vector3.Zero)
            _heading = Mathf.LerpAngle(_heading, MathF.Atan2(wish.X, wish.Z), 1f - MathF.Exp(-14f * dt));
        SendCommand();
        DrawBody();
    }

    private static Vector2 KeyboardInput()
    {
        var v = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) v.Y += 1;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) v.Y -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) v.X += 1;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) v.X -= 1;
        return v;
    }

    /// <summary>Picks up a new snapshot of the possessed NPC (position history for interpolation).</summary>
    private void ReadSim()
    {
        var snap = _host!.Latest;
        if (snap.Tick == _lastTick) return;
        _lastTick = snap.Tick;
        foreach (ref readonly var v in snap.Npcs.Span)
        {
            if (v.Id != NpcId) continue;
            _view = v;
            _simPrev = _simCur;
            _simPrevAt = _simCurAt;
            _simCur = new Vector2(v.X, v.Z);
            _simCurAt = Now();
            return;
        }
    }

    private Vector2 SimInterpolated()
    {
        if (_simCurAt <= _simPrevAt) return _simCur;
        float a = (float)Math.Clamp((Now() - _simCurAt) / (_simCurAt - _simPrevAt), 0, 1);
        return _simPrev.Lerp(_simCur, a);
    }

    /// <summary>Asks the sim to step its NPC onto the body: the gap in units of one tick's step, length ≤ 1.</summary>
    private void SendCommand()
    {
        var pos = GlobalPosition;
        var gap = new Vector2(pos.X, pos.Z) - _simCur;
        float walk = World.PossessedSpeed(_config, _view.Stage, false);
        float run = World.PossessedSpeed(_config, _view.Stage, true);
        Vector2 cmd = Vector2.Zero;
        bool sprint = false;
        float len = gap.Length();
        if (!_following && len > 0.05f)
        {
            sprint = len > walk;
            cmd = gap / (sprint ? run : walk);
            if (cmd.Length() > 1f) cmd = cmd.Normalized();
        }
        if (cmd == Vector2.Zero && _lastCmd == Vector2.Zero && !_lastSprint) return; // idle: one stop command is enough
        _lastCmd = cmd;
        _lastSprint = sprint;
        _host!.Enqueue(new PossessedMoveCommand(cmd.X, cmd.Y, sprint));
        CommandsSent++;
    }

    private void KeepAboveGround()
    {
        if (RenderGround is null) return;
        var p = GlobalPosition;
        float ground = RenderGround(p.X, p.Z);
        if (p.Y < ground - 0.05f)
        {
            GlobalPosition = new Vector3(p.X, ground, p.Z);
            Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
        }
    }

    private void DrawBody()
    {
        if (_body is null) return;
        var view = _view;
        float speed = new Vector2(Velocity.X, Velocity.Z).Length();
        var pose = speed > 0.3f ? NpcPose.Walking : view.Pose == NpcPose.Walking ? NpcPose.Idle : view.Pose;
        view = view with { Pose = pose, IsThinking = false };
        var palette = CharacterLibrary.PaletteParams(view.Id, view.SettlementId, 0f);
        float scale = NpcRenderer.StageScale(view.Stage);
        if (Rig is not null) Rig.BodyScale = scale;
        _body.HideModel = Rig?.FirstPerson == true;
        _body.Apply(view, GlobalPosition, _heading, palette, scale, Now()); // world position drives the gait blend
        _body.Position = Vector3.Zero; // the body node sits at the controller origin; only its heading is local

    }

    private static double Now() => Time.GetTicksUsec() / 1_000_000.0;
}
