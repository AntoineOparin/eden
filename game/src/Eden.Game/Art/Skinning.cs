using Godot;

namespace Eden.Game.Art;

/// <summary>
/// CPU skinning of a glTF template at one animation time, without a scene tree (A1 used it for the mid-LOD poses;
/// A2 shares it with the animal meshes). Every skinned surface is posed from the animation's bone tracks (rest pose
/// where a bone has no track) and handed to the caller in the template root's space.
/// </summary>
public static class Skinning
{
    /// <summary>One posed surface: vertex positions and normals in root space plus the source arrays (UVs, indices, colours).</summary>
    public readonly record struct Surface(MeshInstance3D Instance, int Index, Vector3[] Positions, Vector3[] Normals, Godot.Collections.Array Arrays);

    /// <summary>Poses every skinned mesh under <paramref name="root"/> at <paramref name="time"/> of <paramref name="anim"/> (null: rest pose).</summary>
    public static List<Surface> Pose(Node3D root, Animation? anim, float time)
    {
        var result = new List<Surface>();
        var skeleton = FindSkeleton(root);
        if (skeleton is null) return result;
        int bones = skeleton.GetBoneCount();
        var local = new Transform3D[bones];
        for (int b = 0; b < bones; b++) local[b] = skeleton.GetBoneRest(b);
        if (anim is not null)
        {
            time = Math.Min(time, (float)anim.Length);
            for (int t = 0; t < anim.GetTrackCount(); t++)
            {
                var path = anim.TrackGetPath(t);
                if (path.GetSubNameCount() == 0) continue;
                int b = skeleton.FindBone(path.GetSubName(0));
                if (b < 0) continue;
                var x = local[b];
                switch (anim.TrackGetType(t))
                {
                    case Animation.TrackType.Position3D:
                        x.Origin = anim.PositionTrackInterpolate(t, time);
                        break;
                    case Animation.TrackType.Rotation3D:
                    {
                        var boneScale = x.Basis.Scale;
                        x.Basis = new Basis(anim.RotationTrackInterpolate(t, time)) * Basis.FromScale(boneScale);
                        break;
                    }
                    case Animation.TrackType.Scale3D:
                        x.Basis = new Basis(x.Basis.GetRotationQuaternion()) * Basis.FromScale(anim.ScaleTrackInterpolate(t, time));
                        break;
                }
                local[b] = x;
            }
        }
        var global = new Transform3D[bones];
        var done = new bool[bones];
        Transform3D Global(int b)
        {
            if (done[b]) return global[b];
            int parent = skeleton.GetBoneParent(b);
            global[b] = parent >= 0 ? Global(parent) * local[b] : local[b];
            done[b] = true;
            return global[b];
        }

        var skeletonXform = RelativeTransform(root, skeleton);
        foreach (var mi in CharacterLibrary.MeshInstances(root))
        {
            if (mi.Mesh is null) continue;
            if (mi.Skin is null)
            {
                // Unskinned parts (antlers parented to a bone attachment, props): take the node transform as is.
                var nodeXform = RelativeTransform(root, mi);
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                {
                    var arr = mi.Mesh.SurfaceGetArrays(s);
                    var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var n = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    var pos = new Vector3[v.Length];
                    var nrm = new Vector3[v.Length];
                    for (int k = 0; k < v.Length; k++)
                    {
                        pos[k] = nodeXform * v[k];
                        nrm[k] = n.Length > k ? (nodeXform.Basis * n[k]).Normalized() : Vector3.Up;
                    }
                    result.Add(new Surface(mi, s, pos, nrm, arr));
                }
                continue;
            }
            var skin = mi.Skin;
            var bindMatrix = new Transform3D[skin.GetBindCount()];
            for (int i = 0; i < bindMatrix.Length; i++)
            {
                string name = skin.GetBindName(i);
                int bone = string.IsNullOrEmpty(name) ? skin.GetBindBone(i) : skeleton.FindBone(name);
                bindMatrix[i] = (bone >= 0 ? Global(bone) : Transform3D.Identity) * skin.GetBindPose(i);
            }
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var arr = mi.Mesh.SurfaceGetArrays(s);
                var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var n = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var bi = arr[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var bw = arr[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                int per = v.Length > 0 ? bi.Length / v.Length : 4;
                var pos = new Vector3[v.Length];
                var nrm = new Vector3[v.Length];
                for (int k = 0; k < v.Length; k++)
                {
                    var p = Vector3.Zero;
                    var nn = Vector3.Zero;
                    float wsum = 0;
                    for (int j = 0; j < per; j++)
                    {
                        float w = bw[k * per + j];
                        if (w <= 0) continue;
                        int bind = bi[k * per + j];
                        if (bind < 0 || bind >= bindMatrix.Length) continue;
                        var m = bindMatrix[bind];
                        p += (m * v[k]) * w;
                        nn += (m.Basis * (n.Length > k ? n[k] : Vector3.Up)) * w;
                        wsum += w;
                    }
                    if (wsum <= 0) { p = v[k]; nn = n.Length > k ? n[k] : Vector3.Up; }
                    else p /= wsum;
                    pos[k] = skeletonXform * p;
                    nrm[k] = (skeletonXform.Basis * nn).Normalized();
                }
                result.Add(new Surface(mi, s, pos, nrm, arr));
            }
        }
        return result;
    }

    public static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D s) return s;
        foreach (var c in node.GetChildren())
            if (FindSkeleton(c) is { } found) return found;
        return null;
    }

    public static Transform3D RelativeTransform(Node3D root, Node3D target)
    {
        var x = Transform3D.Identity;
        for (Node? n = target; n is not null && n != root; n = n.GetParent())
            if (n is Node3D n3) x = n3.Transform * x;
        return x;
    }
}
