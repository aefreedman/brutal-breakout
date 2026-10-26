using Brutal.Numerics;
using BrutalBreakout.Simulation;
using CrushKit.Brutal.Draw2D;

namespace BrutalBreakout.Presentation;

/// <summary>Explicit comparison-path owner, not a production mode/default or simulation entity cache.</summary>
internal sealed class BakedPresentationMeshes : IDisposable
{
    internal OrderedShapeDrawList Draw { get; }
    private ShmupBrickGeometry? geometry;
    private MeshRectangle[][] rectangles=[];
    private double[] bounds=[];
    // Exactly one geometry revision and twenty small source decompositions. No per-life/revision dictionaries.
    internal long SelectionBytes=>geometry is null?0:20*64+20*16+rectangles.Sum(r=>r.Length*32L);
    internal BakedPresentationMeshes(BakedMeshLimits limits)
    {
        // Includes the fixed twenty decompositions, arrays, one settings geometry reference and
        // bounded current+staged replacement. No historical revision collection exists.
        if(limits.CpuBytes<8192)throw new ArgumentOutOfRangeException(nameof(limits));
        Draw=new(limits);Draw.ReserveExternalCpuBytes(8192);
    }
    internal void Begin(ShmupBrickGeometry next)
    {
        Draw.BeginFrame();
        if(ReferenceEquals(geometry,next))return;
        for(int k=1;k<=20;k++)Draw.InvalidateMesh(k);
        var sources=new MeshRectangle[20][];var maximums=new double[20];
        for(int s=0;s<5;s++)for(int q=0;q<4;q++)
        {
            int k=s*4+q;var parts=next.Rectangles((ShmupBrickShape)s,q);var source=new MeshRectangle[parts.Length];
            for(int i=0;i<parts.Length;i++)
            {
                var a=parts[i].Center-parts[i].HalfExtents;var b=parts[i].Center+parts[i].HalfExtents;
                source[i]=new(a.X,a.Y,b.X,b.Y);
                maximums[k]=Math.Max(maximums[k],Math.Max(Math.Max(Math.Abs(a.X),Math.Abs(a.Y)),Math.Max(Math.Abs(b.X),Math.Abs(b.Y))));
            }
            sources[k]=source;
        }
        rectangles=sources;bounds=maximums;geometry=next;
    }
    internal bool Brick(ShapeDrawList draw,ShmupBrickShape shape,int turns,double2 position,LinearColor color)
    {
        int k=(int)shape*4+turns;
        if((uint)shape>4 || (uint)turns>3)throw new ArgumentOutOfRangeException(nameof(shape));
        var p=new float2((float)position.X,(float)position.Y);
        const double u=1.0/(1<<24);
        double magnitude=Math.Max(Math.Abs(position.X),Math.Abs(position.Y))+bounds[k];
        // Source: min/max=(double pose + local endpoint), each cast to float; size=fl(end-start).
        // Reconstructing a high endpoint as start+size contributes at most 3*castError+subtractError.
        double castError=Math.BitIncrement(u*magnitude+8*2.220446049250313e-16*Math.Max(1,magnitude)+1.1754943508222875e-38);
        double subtractError=Math.BitIncrement(u*(2*bounds[k]+2*castError)+1.1754943508222875e-38);
        double positionError=Math.Max(Math.Abs((double)p.X-position.X),Math.Abs((double)p.Y-position.Y));
        double error=Math.BitIncrement(positionError+3*castError+subtractError);
        return Draw.TryRectangles(draw,k+1,rectangles[k],p,error,color);
    }
    public void Dispose(){Draw.Dispose();geometry=null;rectangles=[];bounds=[];}
}
