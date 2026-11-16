using System.Numerics;
using PropGenerator.Desktop.View;
using Xunit;

namespace PropGenerator.Desktop.Tests;

public class OrbitCameraTests
{
    [Fact]
    public void OrbitLimitsAndDamping()
    {
        Assert.Equal(45f * MathF.PI / 180f, OrbitCamera.FieldOfView);
        Assert.Equal(0.05f, OrbitCamera.Near);
        Assert.Equal(500f, OrbitCamera.Far);
        Assert.Equal(0.08f, OrbitCamera.Damping);

        var c = new OrbitCamera();

        // A big upward drag can never take the camera under the ground, nor past straight up.
        c.Rotate(0, -100000, 800);
        for (int i = 0; i < 400; i++)
        {
            c.Step();
            Assert.InRange(c.Phi, 0.05f, 1.52f);
        }

        Assert.Equal(1.52f, c.Phi, 5);
        c.Rotate(0, 100000, 800);
        while (c.Step())
        {
        }

        Assert.Equal(0.05f, c.Phi, 5);

        c.Zoom(0.0001f);
        Assert.Equal(1.2f, c.Distance);
        c.Zoom(10000f);
        Assert.Equal(40f, c.Distance);
        c.Wheel(1);
        Assert.Equal(40f * 0.95f, c.Distance, 4);

        // Damping: the first frame makes 8 % of the turn, the rest follows, and the whole turn is made in the end.
        var d = new OrbitCamera();
        float start = d.Theta;
        d.Rotate(-100, 0, 800);
        Assert.Equal(start, d.Theta);
        Assert.True(d.Step());
        float whole = 2 * MathF.PI * 100 / 800;
        Assert.Equal(start + (whole * 0.08f), d.Theta, 5);
        Assert.True(d.Step());
        Assert.Equal(start + (whole * 0.08f) + (whole * 0.92f * 0.08f), d.Theta, 5);
        int frames = 0;
        while (d.Step())
        {
            frames++;
        }

        Assert.InRange(frames, 50, 400);
        Assert.Equal(start + whole, d.Theta, 3);
        Assert.False(d.Moving);
    }

    [Fact]
    public void BindingsMoveTheRightWay()
    {
        var c = new OrbitCamera { Target = Vector3.Zero };
        c.LookFrom(new Vector3(0, -1, 0.5f));
        Vector3 eye = c.Eye;

        // Dragging right turns the camera to the left of the piece (three.js turns the camera against the hand).
        c.Rotate(50, 0, 800);
        while (c.Step())
        {
        }

        Assert.True(c.Eye.X < eye.X - 0.1f, $"{c.Eye}");

        // Right drag pans: the target moves left as the hand moves right, so the piece follows the hand.
        var p = new OrbitCamera { Target = Vector3.Zero };
        p.LookFrom(new Vector3(0, -1, 0));
        p.Pan(100, 0, 800);
        while (p.Step())
        {
        }

        Assert.True(p.Target.X < -0.1f, $"{p.Target}");
        Assert.Equal(0f, p.Target.Z, 3);

        // Middle drag down zooms out; up zooms in.
        var z = new OrbitCamera();
        float d = z.Distance;
        z.DragZoom(100);
        Assert.Equal(d / 0.95f, z.Distance, 4);
        z.DragZoom(-100);
        Assert.Equal(d, z.Distance, 4);
    }

    [Fact]
    public void FramingRule()
    {
        var c = new OrbitCamera();
        c.FrameNew(new Vector3(-1, -0.5f, 0), new Vector3(1, 0.5f, 1.5f));
        Assert.Equal(new Vector3(0, 0, 1.5f * 0.58f), c.Target);
        Assert.Equal(8f, c.Distance);
        var back = Vector3.Normalize(c.Eye - c.Target);
        Assert.True(Vector3.Distance(OrbitCamera.Perspective, back) < 1e-4f, $"{back}");

        Assert.Equal(2.7f * 5, OrbitCamera.NewPieceDistance(5, 1), 4);
        Assert.Equal(2.4f * 6, OrbitCamera.NewPieceDistance(2, 6), 4);
        Assert.Equal(8f, OrbitCamera.NewPieceDistance(1, 1));

        // Framed by its parameters, as easy-props' frameScene: a tree of Height 8 and no Width, however far its branches
        // spread, sits at 2.7 × 8 m with the target 0.58 of its height over its pivot; a rock that has a Width needs only 2 m.
        c.FrameNew(new Vector3(-6, -5, 0), new Vector3(5, 4, 7), new PieceFrame(8, null));
        Assert.Equal(new Vector3(0, 0, 8 * 0.58f), c.Target);
        Assert.Equal(21.6f, c.Distance, 4);
        c.FrameNew(new Vector3(-0.3f, -0.3f, 0), new Vector3(0.3f, 0.3f, 0.5f), new PieceFrame(0.5f, 0.6f));
        Assert.Equal(2f, c.Distance, 4);

        // A rock taller than 40 m / 2.7 still fits: the farthest distance grows to its frame.
        c.FrameNew(new Vector3(-5, -5, 0), new Vector3(5, 5, 30));
        Assert.Equal(81f, c.Distance, 3);
        Assert.Equal(81f, c.MaxDistance, 3);

        // Frame (F): the bounding sphere, keeping the direction.
        var f = new OrbitCamera();
        f.LookFrom(new Vector3(1, 0, 1));
        var before = Vector3.Normalize(f.Eye - f.Target);
        f.FrameFit(new Vector3(-1, -1, 0), new Vector3(1, 1, 2));
        Assert.Equal(new Vector3(0, 0, 1), f.Target);
        Assert.Equal(MathF.Sqrt(3) * 1.25f / MathF.Sin(OrbitCamera.FieldOfView / 2), f.Distance, 4);
        Assert.True(Vector3.Distance(before, Vector3.Normalize(f.Eye - f.Target)) < 1e-5f);
    }

    [Fact]
    public void ReframeOnlyWhenHeightChangesOverAFifth()
    {
        Assert.False(OrbitCamera.ShouldReframe(1f, 1.19f));
        Assert.False(OrbitCamera.ShouldReframe(1f, 0.81f));
        Assert.False(OrbitCamera.ShouldReframe(2f, 2f));
        Assert.True(OrbitCamera.ShouldReframe(1f, 1.21f));
        Assert.True(OrbitCamera.ShouldReframe(1f, 0.79f));
        Assert.True(OrbitCamera.ShouldReframe(0f, 0.1f));
    }

    [Fact]
    public void ClickIsAtMostFivePixels()
    {
        Assert.True(OrbitCamera.IsClick(0, 0));
        Assert.True(OrbitCamera.IsClick(3, 4));
        Assert.True(OrbitCamera.IsClick(-5, 0));
        Assert.False(OrbitCamera.IsClick(4, 4));
        Assert.False(OrbitCamera.IsClick(0, 5.01));
    }

    [Fact]
    public void LookFromKeepsTargetAndDistanceAndTheLimits()
    {
        var c = new OrbitCamera { Target = new Vector3(1, 2, 3) };
        c.Zoom(0.5f);
        float d = c.Distance;
        c.LookFrom(new Vector3(0, 0, 1));
        Assert.Equal(0.05f, c.Phi, 5);
        Assert.Equal(d, c.Distance);
        Assert.Equal(new Vector3(1, 2, 3), c.Target);
        c.LookFrom(new Vector3(1, 0, 1));
        Assert.Equal(MathF.PI / 4, c.Phi, 5);
        Assert.True(Vector3.Distance(Vector3.Normalize(new Vector3(1, 0, 1)), Vector3.Normalize(c.Eye - c.Target)) < 1e-5f);

        // Level with the ground is just above it: 1.52 rad from straight up.
        c.LookFrom(new Vector3(1, 0, 0));
        Assert.Equal(1.52f, c.Phi, 5);
        c.LookFrom(new Vector3(0, 0, -1));
        Assert.Equal(1.52f, c.Phi, 5);
    }
}
