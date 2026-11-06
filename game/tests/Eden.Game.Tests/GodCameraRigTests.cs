namespace Eden.Game.Tests;

using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
public class GodCameraRigTests
{
    [TestCase]
    [RequireGodotRuntime]
    public async Task Zoom_is_clamped_between_20_and_400_metres()
    {
        var runner = ISceneRunner.Load("res://scenes/cameras/god_rig.tscn", true);
        var rig = (GodCameraRig)runner.Scene()!;

        rig.ZoomBy(200);
        AssertThat(rig.TargetZoom).IsEqual(400f);
        rig.SetZoom(1f, immediate: true);
        AssertThat(rig.Zoom).IsEqual(20f);
        AssertThat(rig.PitchDegrees).IsEqualApprox(50f, 0.01f);
        rig.SetZoom(10_000f, immediate: true);
        AssertThat(rig.Zoom).IsEqual(400f);
        AssertThat(rig.PitchDegrees).IsEqualApprox(65f, 0.01f);

        // Wheel input goes through the same clamp.
        rig.SetZoom(25f, immediate: true);
        for (int i = 0; i < 10; i++)
        {
            runner.SimulateMouseButtonPressed(MouseButton.WheelUp);
            await runner.AwaitInputProcessed();
        }
        AssertThat(rig.TargetZoom).IsEqual(20f);
        await runner.SimulateFrames(60);
        AssertThat(rig.Zoom).IsGreaterEqual(20f);
        AssertThat(rig.Zoom).IsLessEqual(25f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Never_changes_yaw_while_panning_and_zooming()
    {
        var runner = ISceneRunner.Load("res://scenes/cameras/god_rig.tscn", true);
        var rig = (GodCameraRig)runner.Scene()!;
        rig.SetBounds(new Rect2(0, 0, 512, 512));
        rig.Focus(new Vector2(256, 256));

        rig.Pan(new Vector2(1, -1), 0.5);
        rig.ZoomBy(3);
        await runner.SimulateFrames(20);
        rig.Pan(new Vector2(-1, 0.3f), 0.5);
        rig.ZoomBy(-5);
        await runner.SimulateFrames(20);

        AssertThat(rig.Rotation.Y).IsEqual(0f);
        AssertThat(rig.Camera!.Rotation.Y).IsEqual(0f);
        AssertThat(rig.Camera.Rotation.Z).IsEqual(0f);
        AssertThat(Mathf.Abs(rig.Camera.GlobalBasis.Z.X)).IsLess(1e-5f); // looks straight down -Z, no yaw
        var pitch = Mathf.RadToDeg(-rig.Camera.Rotation.X);
        AssertThat(pitch).IsBetween(50f, 65f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Focus_is_clamped_to_the_world_bounds()
    {
        var rig = GD.Load<PackedScene>("res://scenes/cameras/god_rig.tscn").Instantiate<GodCameraRig>();
        rig.SetBounds(new Rect2(0, 0, 512, 512));
        rig.Focus(new Vector2(-100, 900));
        AssertThat(rig.Position.X).IsEqual(0f);
        AssertThat(rig.Position.Z).IsEqual(512f);
        rig.Free();
    }
}
