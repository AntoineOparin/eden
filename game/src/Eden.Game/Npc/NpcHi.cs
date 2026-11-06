using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/npc/npc_hi.tscn: the close-up NPC. Pooled by <see cref="NpcRenderer"/> and re-targeted at
/// whichever NPC currently owns the slot. M2 placeholder body (capsule + head + nose) with procedural pose
/// animation; the skinned glTF + AnimationTree replaces the children later without changing this API.
/// </summary>
public partial class NpcHi : Node3D
{
    private MeshInstance3D? _body;
    private MeshInstance3D? _head;
    private Node3D? _thinking;
    private StandardMaterial3D? _material;
    private Color _color = Colors.White;

    public int NpcId { get; private set; } = -1;

    public override void _Ready()
    {
        _body = GetNodeOrNull<MeshInstance3D>("Body");
        _head = GetNodeOrNull<MeshInstance3D>("Body/Head");
        _thinking = GetNodeOrNull<Node3D>("Thinking");
        // Own material per pooled instance so each can carry its settlement colour.
        _material = new StandardMaterial3D { AlbedoColor = _color, Roughness = 0.8f };
        if (_body is not null) _body.MaterialOverride = _material;
        if (_head is not null) _head.MaterialOverride = _material;
    }

    public void Release()
    {
        NpcId = -1;
        Visible = false;
    }

    public void Apply(in NpcView view, Vector3 position, float heading, Color color, float scale, double time)
    {
        NpcId = view.Id;
        Visible = true;
        Position = position;
        Rotation = new Vector3(0, heading, 0);
        Scale = new Vector3(scale, scale, scale);
        if (_material is not null && _color != color)
        {
            _color = color;
            _material.AlbedoColor = color;
        }
        if (_thinking is not null) _thinking.Visible = view.IsThinking;
        if (_body is null) return;

        float phase = (float)(time * 9.0 + view.Id * 0.37);
        switch (view.Pose)
        {
            case NpcPose.Walking:
                _body.Position = new Vector3(0, 0.85f + MathF.Abs(MathF.Sin(phase)) * 0.08f, 0);
                _body.Rotation = new Vector3(0.08f, 0, MathF.Sin(phase) * 0.06f);
                break;
            case NpcPose.Working:
                _body.Position = new Vector3(0, 0.85f, 0);
                _body.Rotation = new Vector3(0.25f + MathF.Sin(phase * 0.6f) * 0.25f, 0, 0);
                break;
            case NpcPose.Fighting:
                _body.Position = new Vector3(0, 0.85f, 0);
                _body.Rotation = new Vector3(0.15f, MathF.Sin(phase * 1.5f) * 0.4f, 0);
                break;
            case NpcPose.Sleeping:
            case NpcPose.Dead:
                _body.Position = new Vector3(0, 0.3f, 0);
                _body.Rotation = new Vector3(MathF.PI * 0.5f, 0, 0);
                break;
            default:
                _body.Position = new Vector3(0, 0.85f + MathF.Sin(phase * 0.25f) * 0.01f, 0);
                _body.Rotation = Vector3.Zero;
                break;
        }
    }
}
