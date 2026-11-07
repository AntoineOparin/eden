using Godot;

namespace Eden.Game.Art;

/// <summary>
/// A2 "less chibi": re-proportions a Kenney Mini Character template once, in place, before anything instances or bakes
/// it. The rig is root → legs, torso → arms, head with identity rest rotations, and the animations only rotate bones
/// (plus root translation), so the reshape is a change of bone rests, skin bind poses and bind-pose vertices:
/// legs stretched down from the hip by <see cref="Legs"/>, the torso up by <see cref="Torso"/>, arms along their own
/// axis by <see cref="Arms"/> and turned down by <see cref="ArmDrop"/>, torso and legs narrowed, and the head scaled about
/// the neck by <see cref="Head"/>. Joints blend by skin weight. The head goes from about half the model's height (1:2)
/// to about a fifth (0.19–0.24 by model, 1:4.2–1:5.3); the renderer still scales the whole model to 1.75 m, so the
/// height rule holds. Pure data edit, no scene tree needed.
/// </summary>
public static class CharacterProportions
{
    public const float Legs = 2.3f, Torso = 1.8f, Arms = 1.3f, Head = 0.52f;

    /// <summary>Torso and hips narrowed (x) so the shoulders sit closer; legs pulled in under the hips.</summary>
    public const float TorsoWidth = 0.86f, LegWidth = 0.9f;

    /// <summary>The rig's rest pose holds the arms almost level; they are turned down this far (degrees) about the shoulder.</summary>
    public const float ArmDrop = 35f;

    /// <summary>Bumps when the numbers above change, so caches keyed on the character set (impostor atlas) refresh.</summary>
    public const int Version = 1;

    private static readonly HashSet<ulong> Done = new();

    /// <summary>Head height over total standing height (rest pose) of a template, for tests and the lineup.</summary>
    public static float HeadRatio(Node3D template)
    {
        float minY = float.MaxValue, maxY = float.MinValue, headMin = float.MaxValue, headMax = float.MinValue;
        foreach (var surface in Skinning.Pose(template, null, 0))
        {
            bool head = surface.Instance.Name.ToString().Contains("head", StringComparison.OrdinalIgnoreCase);
            foreach (var p in surface.Positions)
            {
                minY = MathF.Min(minY, p.Y);
                maxY = MathF.Max(maxY, p.Y);
                if (!head) continue;
                headMin = MathF.Min(headMin, p.Y);
                headMax = MathF.Max(headMax, p.Y);
            }
        }
        return headMax > headMin && maxY > minY ? (headMax - headMin) / (maxY - minY) : 0f;
    }

    /// <summary>Reshapes the template (once per template instance). Returns false when the rig is not the expected one.</summary>
    public static bool Apply(Node3D template)
    {
        if (!Done.Add(template.GetInstanceId())) return true;
        return ApplyCore(template);
    }

    private static bool ApplyCore(Node3D template)
    {
        var sk = Skinning.FindSkeleton(template);
        if (sk is null) return false;
        int root = sk.FindBone("root"), torso = sk.FindBone("torso"), head = sk.FindBone("head");
        int legL = sk.FindBone("leg-left"), legR = sk.FindBone("leg-right"), armL = sk.FindBone("arm-left"), armR = sk.FindBone("arm-right");
        if (root < 0 || torso < 0 || head < 0 || legL < 0 || legR < 0 || armL < 0 || armR < 0)
        {
            var names = new List<string>();
            for (int b = 0; b < sk.GetBoneCount(); b++) names.Add(sk.GetBoneName(b));
            GD.PrintErr($"[art] unexpected rig: {string.Join(", ", names)}");
            return false;
        }

        // Old global rest positions (rotations are identity in this rig).
        Vector3 G(int b) => sk.GetBoneGlobalRest(b).Origin;
        var hip = G(torso);
        var oldShoulderL = G(armL);
        var oldShoulderR = G(armR);
        var oldNeck = G(head);
        float hipY = hip.Y;

        // New joints: legs grow downward so the hip rises by hipY * (Legs - 1); the torso stretches above it.
        float newHipY = hipY * Legs;
        Vector3 TorsoMap(Vector3 p) => new(hip.X + (p.X - hip.X) * TorsoWidth, newHipY + (p.Y - hipY) * Torso, p.Z);
        var newShoulderL = TorsoMap(oldShoulderL);
        var newShoulderR = TorsoMap(oldShoulderR);
        var newNeck = TorsoMap(oldNeck);

        // Arm axis per side: from the shoulder to the far end of the arm's vertices (measured below).
        var armAxis = new Dictionary<int, Vector3> { [armL] = Vector3.Right, [armR] = Vector3.Left };
        // Arms turn down about the forward axis: the left arm (+X) clockwise seen from the front, the right arm the other way.
        var dropL = new Basis(Vector3.Back, -Mathf.DegToRad(ArmDrop));
        var dropR = new Basis(Vector3.Back, Mathf.DegToRad(ArmDrop));

        var maps = new Dictionary<int, Func<Vector3, Vector3>>
        {
            [legL] = p => new Vector3(hip.X + (p.X - hip.X) * LegWidth, p.Y * Legs, p.Z),
            [legR] = p => new Vector3(hip.X + (p.X - hip.X) * LegWidth, p.Y * Legs, p.Z),
            [root] = TorsoMap,
            [torso] = TorsoMap,
            [armL] = p => newShoulderL + dropL * Stretch(p - oldShoulderL, armAxis[armL], Arms),
            [armR] = p => newShoulderR + dropR * Stretch(p - oldShoulderR, armAxis[armR], Arms),
            [head] = p => newNeck + (p - oldNeck) * Head,
        };

        var meshes = CharacterLibrary.MeshInstances(template).Where(m => m.Mesh is not null && m.Skin is not null).ToList();
        // Measure the arm axes from the vertices that follow each arm most.
        foreach (var arm in new[] { armL, armR })
        {
            var shoulder = arm == armL ? oldShoulderL : oldShoulderR;
            var far = Vector3.Zero;
            float best = -1;
            foreach (var mi in meshes)
            {
                var binds = BoneOfBind(mi.Skin!, sk);
                for (int s = 0; s < mi.Mesh!.GetSurfaceCount(); s++)
                {
                    var arr = mi.Mesh.SurfaceGetArrays(s);
                    var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var bi = arr[(int)Mesh.ArrayType.Bones].AsInt32Array();
                    var bw = arr[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                    int per = v.Length > 0 ? bi.Length / v.Length : 4;
                    for (int k = 0; k < v.Length; k++)
                    {
                        if (Dominant(bi, bw, k, per, binds) != arm) continue;
                        float d = (v[k] - shoulder).Length();
                        if (d > best) { best = d; far = v[k]; }
                    }
                }
            }
            if (best > 0) armAxis[arm] = (far - shoulder).Normalized();
        }

        // Rewrite each skinned mesh's vertices and normals.
        foreach (var mi in meshes)
        {
            var binds = BoneOfBind(mi.Skin!, sk);
            var mesh = mi.Mesh!;
            var rebuilt = new ArrayMesh();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arr = mesh.SurfaceGetArrays(s);
                var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var n = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var bi = arr[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var bw = arr[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                int per = v.Length > 0 ? bi.Length / v.Length : 4;
                for (int k = 0; k < v.Length; k++)
                {
                    var p = Vector3.Zero;
                    float wsum = 0;
                    var dominant = -1;
                    float dw = -1;
                    for (int j = 0; j < per; j++)
                    {
                        float w = bw[k * per + j];
                        int bind = bi[k * per + j];
                        if (w <= 0 || bind < 0 || bind >= binds.Length) continue;
                        int bone = binds[bind];
                        if (!maps.TryGetValue(bone, out var map)) map = TorsoMap;
                        p += map(v[k]) * w;
                        wsum += w;
                        if (w > dw) { dw = w; dominant = bone; }
                    }
                    if (wsum > 0) v[k] = p / wsum;
                    // Normals follow the dominant region's stretch (inverse-transpose of a per-axis scale).
                    if (n.Length > k)
                    {
                        var inv = dominant == legL || dominant == legR ? new Vector3(1 / LegWidth, 1 / Legs, 1)
                            : dominant == torso || dominant == root ? new Vector3(1 / TorsoWidth, 1 / Torso, 1) : Vector3.One;
                        var nn = n[k] * inv;
                        if (dominant == armL) nn = dropL * nn;
                        else if (dominant == armR) nn = dropR * nn;
                        n[k] = nn.Normalized();
                    }
                }
                arr[(int)Mesh.ArrayType.Vertex] = v;
                if (n.Length > 0) arr[(int)Mesh.ArrayType.Normal] = n;
                var am = mesh as ArrayMesh;
                var flags = am is null ? 0 : am.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights;
                rebuilt.AddSurfaceFromArrays(am?.SurfaceGetPrimitiveType(s) ?? Mesh.PrimitiveType.Triangles, arr, null, null, flags);
                rebuilt.SurfaceSetMaterial(s, mesh.SurfaceGetMaterial(s));
            }
            rebuilt.ResourceName = mesh.ResourceName;
            mi.Mesh = rebuilt;
        }

        // New rests (local, relative to the parent) and bind poses (inverse global rest).
        var newGlobal = new Dictionary<int, Vector3>
        {
            [root] = G(root),
            [legL] = new(hip.X + (G(legL).X - hip.X) * LegWidth, newHipY, G(legL).Z),
            [legR] = new(hip.X + (G(legR).X - hip.X) * LegWidth, newHipY, G(legR).Z),
            [torso] = new(hip.X, newHipY, hip.Z),
            [armL] = newShoulderL,
            [armR] = newShoulderR,
            [head] = newNeck,
        };
        foreach (var (bone, pos) in newGlobal)
        {
            int parent = sk.GetBoneParent(bone);
            var parentPos = parent >= 0 && newGlobal.TryGetValue(parent, out var pp) ? pp : Vector3.Zero;
            var rest = sk.GetBoneRest(bone);
            rest.Origin = pos - parentPos;
            sk.SetBoneRest(bone, rest);
            sk.SetBonePosePosition(bone, rest.Origin);
        }
        var skins = new HashSet<Skin>();
        foreach (var mi in meshes) skins.Add(mi.Skin!);
        foreach (var skin in skins)
        {
            for (int i = 0; i < skin.GetBindCount(); i++)
            {
                string name = skin.GetBindName(i);
                int bone = string.IsNullOrEmpty(name) ? skin.GetBindBone(i) : sk.FindBone(name);
                if (bone < 0 || !newGlobal.TryGetValue(bone, out var pos)) continue;
                var bind = skin.GetBindPose(i);
                bind.Origin = Vector3.Zero;
                // Bind = inverse(global rest); with identity rest rotations that is the old bind's basis and -pos.
                bind.Origin = -(bind.Basis * pos);
                skin.SetBindPose(i, bind);
            }
        }
        return true;
    }

    private static Vector3 Stretch(Vector3 offset, Vector3 axis, float factor)
    {
        float along = offset.Dot(axis);
        return offset + axis * along * (factor - 1f);
    }

    private static int[] BoneOfBind(Skin skin, Skeleton3D sk)
    {
        var result = new int[skin.GetBindCount()];
        for (int i = 0; i < result.Length; i++)
        {
            string name = skin.GetBindName(i);
            result[i] = string.IsNullOrEmpty(name) ? skin.GetBindBone(i) : sk.FindBone(name);
        }
        return result;
    }

    private static int Dominant(int[] bi, float[] bw, int k, int per, int[] binds)
    {
        int best = -1;
        float bestW = 0;
        for (int j = 0; j < per; j++)
        {
            float w = bw[k * per + j];
            int bind = bi[k * per + j];
            if (w > bestW && bind >= 0 && bind < binds.Length) { bestW = w; best = binds[bind]; }
        }
        return best;
    }
}
