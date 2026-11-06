namespace Eden.Game.Tests;

using Eden.Game.Dev;
using Eden.Sim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
public class TerrainBuilderTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void Chunk_32x32_has_33x33_vertices_and_two_triangles_per_cell()
    {
        var info = new WorldInfo(1, 64, 64, 2f, 0f, 32);
        var heights = new float[64 * 64];
        var biomes = new byte[64 * 64];
        for (int i = 0; i < heights.Length; i++) { heights[i] = i % 7; biomes[i] = (byte)(i % 6); }

        var mesh = TerrainBuilder.BuildChunkMesh(heights, biomes, info, 0, 0);

        AssertThat(mesh).IsNotNull();
        AssertThat(mesh!.GetSurfaceCount()).IsEqual(1);
        AssertThat(mesh.SurfaceGetArrayLen(0)).IsEqual(TerrainBuilder.ChunkVertexCount(32));
        AssertThat(TerrainBuilder.ChunkVertexCount(32)).IsEqual(33 * 33);
        AssertThat(mesh.SurfaceGetArrayIndexLen(0)).IsEqual(32 * 32 * 6);
        var arrays = mesh.SurfaceGetArrays(0);
        AssertThat(arrays[(int)Mesh.ArrayType.Normal].AsVector3Array().Length).IsEqual(33 * 33);
        AssertThat(arrays[(int)Mesh.ArrayType.Color].AsColorArray().Length).IsEqual(33 * 33);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Chunk_vertices_sit_on_the_heightmap_in_world_metres()
    {
        var info = new WorldInfo(1, 64, 64, 2f, 0f, 32);
        var heights = new float[64 * 64];
        heights[40 * 64 + 33] = 5f; // cell (33, 40) lives in chunk (1, 1) at local (1, 8)
        var mesh = TerrainBuilder.BuildChunkMesh(heights, new byte[64 * 64], info, 1, 1)!;
        var vertices = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        // Edge chunk is clamped to the map: cells 32..63 -> 32 x 32 vertices.
        AssertThat(vertices.Length).IsEqual(32 * 32);
        AssertThat(vertices[8 * 32 + 1]).IsEqual(new Vector3(66f, 5f, 80f));
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Builds_one_chunk_per_32_cells_for_the_fake_island()
    {
        using var host = new FakeSimHost(10);
        var terrain = new TerrainBuilder();
        terrain.Build(host.Latest);
        AssertThat(terrain.ChunkCount).IsEqual(64);
        AssertThat(terrain.GetChildCount()).IsEqual(64);
        terrain.EnsureBuilt(host.Latest); // same heightmap: no rebuild
        AssertThat(terrain.GetChildCount()).IsEqual(64);
        terrain.Free();
    }
}
