using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/npc/npc_hi.tscn: the close-up NPC. Pooled by <see cref="NpcRenderer"/> and re-targeted at whichever
/// NPC owns the slot. Shows the rigged Kenney Mini Character picked for the NPC (models are created on first use and
/// kept per slot), driven by an <see cref="AnimationTree"/>: a locomotion blend space (idle / walk / sprint by speed)
/// under a transition (move / work / fight / down) from <see cref="NpcPose"/>. Colours come from the palette params
/// instance uniform; elders get a stooped torso bone, the renderer scales children.
/// </summary>
public partial class NpcHi : Node3D
{
    private const string StateParam = "parameters/state/transition_request";
    private const string SpeedParam = "parameters/loco/blend_position";
    /// <summary>Sim walking speed (m/s at 1×) that maps to the walk animation at its authored rate.</summary>
    [Export] public float WalkSpeed { get; set; } = 1.8f;
    [Export] public float ElderStoop { get; set; } = 0.38f;

    private sealed class Rig
    {
        public required Node3D Root;
        public required AnimationTree Tree;
        public Skeleton3D? Skeleton;
        public int Torso = -1;
        public string State = "";
        public readonly List<MeshInstance3D> Meshes = new();
    }

    private readonly Dictionary<CharacterModel, Rig> _rigs = new();
    private Rig? _active;
    private Node3D? _thinking;
    private Vector3 _lastPos;
    private double _lastTime;
    private float _speed;
    private Color _params = new(-1, -1, -1, -1);

    public int NpcId { get; private set; } = -1;
    public CharacterModel? Model { get; private set; }
    public AnimationTree? AnimationTree => _active?.Tree;
    public string CurrentState => _active?.State ?? "";

    public override void _Ready() => _thinking = GetNodeOrNull<Node3D>("Thinking");

    public void Release()
    {
        NpcId = -1;
        Visible = false;
    }

    public void Apply(in NpcView view, Vector3 position, float heading, Color paletteParams, float scale, double time)
    {
        bool retarget = NpcId != view.Id;
        NpcId = view.Id;
        Visible = true;
        Position = position;
        Rotation = new Vector3(0, heading, 0);
        Scale = new Vector3(scale, scale, scale);
        if (_thinking is not null) _thinking.Visible = view.IsThinking;

        var model = CharacterLibrary.ModelFor(view.Id, view.Sex);
        if (model != Model || _active is null) Activate(model);
        if (_active is null) return;

        double dt = retarget ? 0 : Math.Clamp(time - _lastTime, 0, 0.25);
        if (dt > 0)
        {
            float moved = new Vector2(position.X - _lastPos.X, position.Z - _lastPos.Z).Length() / (float)dt;
            _speed = Mathf.Lerp(_speed, moved, 1f - MathF.Exp(-8f * (float)dt));
        }
        else if (retarget)
        {
            _speed = view.Pose == NpcPose.Walking ? WalkSpeed : 0f;
        }
        _lastPos = position;
        _lastTime = time;

        if (paletteParams != _params)
        {
            _params = paletteParams;
            foreach (var mi in _active.Meshes) mi.SetInstanceShaderParameter("npc_params", paletteParams);
        }

        string state = view.Pose switch
        {
            NpcPose.Working => "work",
            NpcPose.Fighting => "fight",
            NpcPose.Sleeping or NpcPose.Dead => "down",
            _ => "move",
        };
        var tree = _active.Tree;
        if (state != _active.State)
        {
            _active.State = state;
            tree.Set(StateParam, state);
        }
        float blend = view.Pose == NpcPose.Walking ? Math.Clamp(_speed / WalkSpeed, 0.6f, 2.5f) : 0f;
        tree.Set(SpeedParam, blend);
        tree.Advance(retarget ? 0.0 : dt);

        if (view.Stage == LifeStage.Elder && _active.Skeleton is { } sk && _active.Torso >= 0 && state == "move")
        {
            var q = sk.GetBonePoseRotation(_active.Torso);
            sk.SetBonePoseRotation(_active.Torso, q * new Quaternion(Vector3.Right, ElderStoop));
        }
    }

    private void Activate(CharacterModel model)
    {
        if (_active is not null) _active.Root.Visible = false;
        Model = model;
        if (!_rigs.TryGetValue(model, out var rig))
        {
            rig = Build(model);
            if (rig is null)
            {
                _active = null;
                return;
            }
            _rigs[model] = rig;
        }
        rig.Root.Visible = true;
        rig.State = "";
        _params = new Color(-1, -1, -1, -1);
        _active = rig;
    }

    private Rig? Build(CharacterModel model)
    {
        var root = CharacterLibrary.Instantiate(model);
        if (root is null || CharacterLibrary.Player(root) is not { } player) return null;
        root.Name = "Model_" + model.Path.GetFile().GetBaseName();
        AddChild(root);

        var loco = new AnimationNodeBlendSpace1D { MinSpace = 0f, MaxSpace = 3f };
        loco.AddBlendPoint(new AnimationNodeAnimation { Animation = CharacterLibrary.AnimIdle }, 0f, -1, "idle");
        loco.AddBlendPoint(new AnimationNodeAnimation { Animation = CharacterLibrary.AnimWalk }, 1f, -1, "walk");
        loco.AddBlendPoint(new AnimationNodeAnimation { Animation = CharacterLibrary.AnimRun }, 2.5f, -1, "run");
        var transition = new AnimationNodeTransition { XfadeTime = 0.2 };
        transition.AddInput("move");
        transition.AddInput("work");
        transition.AddInput("fight");
        transition.AddInput("down");
        var blend = new AnimationNodeBlendTree();
        blend.AddNode("loco", loco);
        blend.AddNode("work", new AnimationNodeAnimation { Animation = CharacterLibrary.AnimWork });
        blend.AddNode("fight", new AnimationNodeAnimation { Animation = CharacterLibrary.AnimFight });
        blend.AddNode("down", new AnimationNodeAnimation { Animation = CharacterLibrary.AnimDie });
        blend.AddNode("state", transition);
        blend.ConnectNode("state", 0, "loco");
        blend.ConnectNode("state", 1, "work");
        blend.ConnectNode("state", 2, "fight");
        blend.ConnectNode("state", 3, "down");
        blend.ConnectNode("output", 0, "state");

        var tree = new AnimationTree
        {
            Name = "AnimationTree",
            TreeRoot = blend,
            CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
        };
        root.AddChild(tree);
        tree.AnimPlayer = tree.GetPathTo(player);
        tree.Active = true;

        var rig = new Rig { Root = root, Tree = tree };
        foreach (var mi in CharacterLibrary.MeshInstances(root))
        {
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            rig.Meshes.Add(mi);
            if (mi.GetParent() is Skeleton3D sk) rig.Skeleton = sk;
        }
        if (rig.Skeleton is not null) rig.Torso = rig.Skeleton.FindBone("torso");
        return rig;
    }
}
