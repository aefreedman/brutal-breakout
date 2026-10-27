using Brutal.Numerics;
using BrutalBreakout.Performance;
using BrutalBreakout.Presentation;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;
using Xunit;

namespace BrutalBreakout.Performance.Tests;

public class BakedPresentationTests
{
    private static BakedMeshLimits Limits()=>new(32,2_000_000,2_000_000,4096,16_384);
    [Theory]
    [InlineData(10,10)]
    [InlineData(10,14.125)]
    [InlineData(.125,.15625)]
    public void AllCompoundShapesTurnsAndFractionalPosesMatchSharedEndpointPipeline(double size,double pitch)
    {
        var geometry=new ShmupBrickGeometry(size,pitch);using var meshes=new BakedPresentationMeshes(Limits());
        int instances=0;
        foreach(double fraction in new[]{0,.125,1.0/3,.99999})
        foreach(float scale in new[]{.25f,1f,1.35f})
        foreach(var shape in Enum.GetValues<ShmupBrickShape>())for(int q=0;q<4;q++)
        {
            var p=new double2(100+fraction,200-fraction);var color=new LinearColor(.2f,.6f,.8f,.5f);
            using var expected=new ShapeDrawList();using var actual=new ShapeDrawList();
            var transform=new float3x2(scale,0,0,scale,.125f,.375f);
            expected.BeginFrame(1280,720);actual.BeginFrame(1280,720);expected.SetTransform(transform);actual.SetTransform(transform);
            meshes.Begin(geometry);
            DrawBrick(expected,geometry,shape,q,p,color);
            if(!meshes.Brick(actual,shape,q,p,color))DrawBrick(actual,geometry,shape,q,p,color);
            var frame=meshes.Draw.Finish(actual);instances+=frame.Instances.Length;Compare(expected.Finish(),frame);
            Assert.InRange(meshes.Draw.CpuMeshBytes,8192,Limits().CpuBytes);Assert.InRange(meshes.SelectionBytes,1,8192);
        }
        Assert.True(instances>100);
    }
    [Fact]
    public void AuthoritativeTinyGeometryKeepsReferenceNoOutputInsteadOfThrowingCandidateRejection()
    {
        double tiny=Math.ScaleB(1,-160);var geometry=new ShmupBrickGeometry(tiny,tiny);
        using var owner=new BakedPresentationMeshes(Limits());using var draw=new ShapeDrawList();using var expected=new ShapeDrawList();
        draw.BeginFrame(1280,720);expected.BeginFrame(1280,720);owner.Begin(geometry);var position=new double2(100,100);var color=new LinearColor(.2f,.4f,.6f);
        Assert.False(owner.Brick(draw,ShmupBrickShape.Square,0,position,color));
        DrawBrick(draw,geometry,ShmupBrickShape.Square,0,position,color);DrawBrick(expected,geometry,ShmupBrickShape.Square,0,position,color);
        var result=owner.Draw.Finish(draw);Assert.Empty(result.Instances.ToArray());Assert.Empty(result.Triangles.Indices.ToArray());
        Compare(expected.Finish(),result);Assert.Equal(0,owner.Draw.MeshBakes);
    }
    [Fact]
    public void MovementHealthShapeSwitchAndRevisionChurnDoNotRetainOldCpuOwners()
    {
        using var owner=new BakedPresentationMeshes(Limits());using var draw=new ShapeDrawList();
        var geometry=new ShmupBrickGeometry(10,14);long square=0,l=0;
        for(int frame=0;frame<20;frame++)
        {
            draw.BeginFrame(1280,720);owner.Begin(geometry);
            var shape=frame%2==0?ShmupBrickShape.Square:ShmupBrickShape.L;
            Assert.True(owner.Brick(draw,shape,0,new(100+frame*.125,200),new(.2f,.4f,.6f,frame%2==0?.4f:.8f)));
            var op=Assert.Single(owner.Draw.Finish(draw).Operations.ToArray());Assert.NotNull(op.Mesh);
            if(shape==ShmupBrickShape.Square){if(square==0)square=op.Mesh!.Identity;Assert.Equal(square,op.Mesh!.Identity);}
            else {if(l==0)l=op.Mesh!.Identity;Assert.Equal(l,op.Mesh!.Identity);}
        }
        Assert.NotEqual(square,l);Assert.Equal(2,owner.Draw.MeshBakes);
        for(int i=0;i<1000;i++)
        {
            draw.BeginFrame(1280,720);owner.Begin(new ShmupBrickGeometry(10,14+(i+1)*.0001));
            Assert.True(owner.Brick(draw,ShmupBrickShape.T,i%4,new(100,200),new(.2f,.4f,.6f)));
            owner.Draw.Finish(draw);Assert.Equal(1,owner.Draw.CpuMeshCount);Assert.InRange(owner.Draw.CpuMeshBytes,8192,Limits().CpuBytes);
        }
        owner.Dispose();Assert.Equal(0,owner.Draw.CpuMeshBytes);Assert.Equal(0,owner.SelectionBytes);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletePresentationPreservesOwnedSnapshotTrailsAndOrderedPrimitiveStream(bool trails)
    {
        var manifest=Manifest.Load(Path.Combine(AppContext.BaseDirectory,"study.json"));
        var c=ResolvedCase.Load(new CaseSpec{Id="baked-tests",Target=500},manifest.Policy,Path.Combine(AppContext.BaseDirectory,"Config"),1);
        var fixture=RenderCpuStudy.Fixture(c,new Layout(c.Settings,c.Policy),500,trails);
        var savedBalls=fixture.Snapshot.Balls.ToArray();var savedBricks=fixture.Snapshot.Bricks.ToArray();
        using var expected=new ShapeDrawList();using var actual=new ShapeDrawList();using var meshes=new BakedPresentationMeshes(Limits());
        var reference=ShmupPresentation.Build(expected,fixture.Snapshot,c.Settings,c.Colors,c.Style,1280,720,fixture.History);
        var candidate=ShmupPresentation.BuildBaked(actual,fixture.Snapshot,c.Settings,c.Colors,c.Style,1280,720,fixture.History,meshes);
        Assert.True(candidate.Instances.Length>=500);Compare(reference,candidate);
        int bakes=meshes.Draw.MeshBakes;
        ShmupPresentation.BuildBaked(actual,fixture.Snapshot,c.Settings,c.Colors,c.Style,1280,720,fixture.History,meshes);
        Assert.Equal(bakes,meshes.Draw.MeshBakes);Assert.Equal(savedBalls,fixture.Snapshot.Balls.ToArray());Assert.Equal(savedBricks,fixture.Snapshot.Bricks.ToArray());
    }
    private static void DrawBrick(ShapeDrawList draw,ShmupBrickGeometry geometry,ShmupBrickShape shape,int q,double2 p,LinearColor color)
    {
        foreach(var r in geometry.Rectangles(shape,q))
        {
            var a=p+(r.Center-r.HalfExtents);var b=p+(r.Center+r.HalfExtents);
            var start=new float2((float)a.X,(float)a.Y);var end=new float2((float)b.X,(float)b.Y);draw.FillRect(start,end-start,color);
        }
    }
    private static void Compare(ShapeFrame reference,OrderedShapeFrame candidate)
    {
        int current=0;
        foreach(var op in candidate.Operations.Span)
        {
            if(op.Mesh is null)for(int i=op.First;i<op.First+op.Count;i++)Check(candidate.Triangles.Vertices.Span[(int)candidate.Triangles.Indices.Span[i]]);
            else foreach(var instance in candidate.Instances.Span.Slice(op.First,op.Count))foreach(uint index in op.Mesh.Indices)
            {
                var u=op.Mesh.Vertices[(int)index];var p=new float2(instance.Origin.X+instance.AxisX.X*u.X+instance.AxisY.X*u.Y,instance.Origin.Y+instance.AxisX.Y*u.X+instance.AxisY.Y*u.Y);
                Check(new(p,instance.Color));
            }
        }
        Assert.Equal(reference.Indices.Length,current);
        void Check(ShapeVertex candidateVertex)
        {
            Assert.True(current<reference.Indices.Length);
            var a=reference.Vertices.Span[(int)reference.Indices.Span[current++]];var b=candidateVertex;
            Assert.InRange(Math.Abs((double)a.Position.X-b.Position.X),0,.01);Assert.InRange(Math.Abs((double)a.Position.Y-b.Position.Y),0,.01);Assert.Equal(a.Color,b.Color);
        }
    }
}
