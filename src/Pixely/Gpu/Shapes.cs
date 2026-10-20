using System.Collections.Immutable;
using System.Numerics;

namespace Pixely.Gpu;

public readonly struct Shape<TVertexType> where TVertexType : unmanaged, IVertexType
{
    internal readonly ImmutableArray<TVertexType> Vertices;

    public Shape(ImmutableArray<TVertexType> vertices)
    {
        if (vertices.IsDefaultOrEmpty)
        {
            throw new ArgumentException($"'{nameof(vertices)}' cannot be empty or default.");
        }
        Vertices = vertices;
    }

    public static implicit operator ReadOnlySpan<TVertexType>(Shape<TVertexType> shape)
    {
        return shape.Vertices.AsSpan();
    }
    
    public static implicit operator Shape<TVertexType>(ImmutableArray<TVertexType> immutableArray)
    {
        return new Shape<TVertexType>(immutableArray);
    }

    public int Length => Vertices.Length;
}

public static class Shapes
{
    public static Shape<TVertexType> Reshape<TVertexType>(this Shape<TVertexType> shape, Vector3 offset = default, float scale = 1) where TVertexType : unmanaged, IVertexType, IPositionable
    {
        return shape.Reshape(offset, new Vector3(scale));
    }
    
    public static Shape<TVertexType> Reshape<TVertexType>(this Shape<TVertexType> shape, Vector3 offset, Vector3 scale) where TVertexType : unmanaged, IVertexType, IPositionable
    {
        if (scale == Vector3.One && offset == default)
        {
            return shape;
        }

        ReadOnlySpan<TVertexType> span = shape;
        var builder = ImmutableArray.CreateBuilder<TVertexType>();

        foreach (TVertexType positionTextureNormalVertex in span)
        {
            builder.Add(positionTextureNormalVertex with { Position = (positionTextureNormalVertex.Position + offset) * scale });
        }
        
        return new Shape<TVertexType>(builder.ToImmutable());
    }
    
    public static Shape<TTargetVertexType> Reshape<TSourceVertexType, TTargetVertexType>(this Shape<TSourceVertexType> shape, Func<ImmutableArray<TSourceVertexType>, ImmutableArray<TTargetVertexType>> reshapeFunction)
        where TSourceVertexType : unmanaged, IVertexType
        where TTargetVertexType : unmanaged, IVertexType
    {
        var result = reshapeFunction(shape.Vertices);
        
        return new Shape<TTargetVertexType>(result);
    }
}

public static class PositionTextureNormalShapes
{
    static PositionTextureNormalShapes()
    {
        ImmutableArray<PositionTextureNormalVertex> cube =
        [
            new(new Vector3(-1, -1, -1), new Vector2(0, 0), new Vector3(0, 0, -1)),
            new(new Vector3(-1, 1, -1), new Vector2(0, 1), new Vector3(0, 0, -1)),
            new(new Vector3(1, 1, -1), new Vector2(1, 1), new Vector3(0, 0, -1)),

            new(new Vector3(1, 1, -1), new Vector2(1, 1), new Vector3(0, 0, -1)),
            new(new Vector3(1, -1, -1), new Vector2(1, 0), new Vector3(0, 0, -1)),
            new(new Vector3(-1, -1, -1), new Vector2(0, 0), new Vector3(0, 0, -1)),

            new(new Vector3(1, -1, -1), new Vector2(0, 0), new Vector3(1, 0, 0)),
            new(new Vector3(1, 1, -1), new Vector2(0, 1), new Vector3(1, 0, 0)),
            new(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(1, 0, 0)),

            new(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(1, 0, 0)),
            new(new Vector3(1, -1, 1), new Vector2(1, 0), new Vector3(1, 0, 0)),
            new(new Vector3(1, -1, -1), new Vector2(0, 0), new Vector3(1, 0, 0)),

            new(new Vector3(1, -1, 1), new Vector2(0, 0), new Vector3(0, 0, 1)),
            new(new Vector3(1, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1)),
            new(new Vector3(-1, 1, 1), new Vector2(1, 1), new Vector3(0, 0, 1)),

            new(new Vector3(-1, 1, 1), new Vector2(1, 1), new Vector3(0, 0, 1)),
            new(new Vector3(-1, -1, 1), new Vector2(1, 0), new Vector3(0, 0, 1)),
            new(new Vector3(1, -1, 1), new Vector2(0, 0), new Vector3(0, 0, 1)),

            new(new Vector3(-1, -1, 1), new Vector2(0, 0), new Vector3(-1, 0, 0)),
            new(new Vector3(-1, 1, 1), new Vector2(0, 1), new Vector3(-1, 0, 0)),
            new(new Vector3(-1, 1, -1), new Vector2(1, 1), new Vector3(-1, 0, 0)),

            new(new Vector3(-1, 1, -1), new Vector2(1, 1), new Vector3(-1, 0, 0)),
            new(new Vector3(-1, -1, -1), new Vector2(1, 0), new Vector3(-1, 0, 0)),
            new(new Vector3(-1, -1, 1), new Vector2(0, 0), new Vector3(-1, 0, 0)),

            new(new Vector3(-1, 1, -1), new Vector2(0, 0), new Vector3(0, 1, 0)),
            new(new Vector3(-1, 1, 1), new Vector2(0, 1), new Vector3(0, 1, 0)),
            new(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 1, 0)),

            new(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 1, 0)),
            new(new Vector3(1, 1, -1), new Vector2(1, 0), new Vector3(0, 1, 0)),
            new(new Vector3(-1, 1, -1), new Vector2(0, 0), new Vector3(0, 1, 0)),

            new(new Vector3(-1, -1, 1), new Vector2(0, 0), new Vector3(0, -1, 0)),
            new(new Vector3(-1, -1, -1), new Vector2(0, 1), new Vector3(0, -1, 0)),
            new(new Vector3(1, -1, -1), new Vector2(1, 1), new Vector3(0, -1, 0)),

            new(new Vector3(1, -1, -1), new Vector2(1, 1), new Vector3(0, -1, 0)),
            new(new Vector3(1, -1, 1), new Vector2(1, 0), new Vector3(0, -1, 0)),
            new(new Vector3(-1, -1, 1), new Vector2(0, 0), new Vector3(0, -1, 0)),
        ];
        Cube = cube;
        
        ImmutableArray<PositionTextureNormalVertex> isometricCube =
        [
            new(new Vector3(1, -1, -1), new Vector2(0, 0), new Vector3(1, 0, 0)),
            new(new Vector3(1, 1, -1), new Vector2(0, 1), new Vector3(1, 0, 0)),
            new(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(1, 0, 0)),

            new(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(1, 0, 0)),
            new(new Vector3(1, -1, 1), new Vector2(1, 0), new Vector3(1, 0, 0)),
            new(new Vector3(1, -1, -1), new Vector2(0, 0), new Vector3(1, 0, 0)),

            new(new Vector3(1, -1, 1), new Vector2(0, 0), new Vector3(0, 0, 1)),
            new(new Vector3(1, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1)),
            new(new Vector3(-1, 1, 1), new Vector2(1, 1), new Vector3(0, 0, 1)),

            new(new Vector3(-1, 1, 1), new Vector2(1, 1), new Vector3(0, 0, 1)),
            new(new Vector3(-1, -1, 1), new Vector2(1, 0), new Vector3(0, 0, 1)),
            new(new Vector3(1, -1, 1), new Vector2(0, 0), new Vector3(0, 0, 1)),

            new(new Vector3(-1, 1, -1), new Vector2(0, 0), new Vector3(0, 1, 0)),
            new(new Vector3(-1, 1, 1), new Vector2(0, 1), new Vector3(0, 1, 0)),
            new(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 1, 0)),

            new(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 1, 0)),
            new(new Vector3(1, 1, -1), new Vector2(1, 0), new Vector3(0, 1, 0)),
            new(new Vector3(-1, 1, -1), new Vector2(0, 0), new Vector3(0, 1, 0)),
        ];
        IsometricCube = isometricCube;
        
        ImmutableArray<PositionTextureNormalVertex> verticalQuad =
        [
            new(new Vector3(-1.0f, -1.0f, 0.0f), new Vector2(0, 1), new Vector3(0, 0, 1)),
            new(new Vector3(-1.0f, 1.0f, 0.0f), new Vector2(0, 0), new Vector3(0, 0, 1)),
            new(new Vector3(1.0f, -1.0f, 0.0f), new Vector2(1, 1), new Vector3(0, 0, 1)),
            new(new Vector3(1.0f, 1.0f, 0.0f), new Vector2(1, 0), new Vector3(0, 0, 1)),
        ];
        VerticalQuad = verticalQuad;

        ImmutableArray<PositionTextureNormalVertex> horizontalQuad =
        [
            new(new Vector3(-1.0f, 0.0f, -1.0f), new Vector2(0, 1), new Vector3(0, 1, 0)),
            new(new Vector3(-1.0f, 0.0f, 1.0f), new Vector2(0, 0), new Vector3(0, 1, 0)),
            new(new Vector3(1.0f, 0.0f, -1.0f), new Vector2(1, 1), new Vector3(0, 1, 0)),
            new(new Vector3(1.0f, 0.0f, -1.0f), new Vector2(1, 1), new Vector3(0, 1, 0)),
            new(new Vector3(-1.0f, 0.0f, 1.0f), new Vector2(0, 0), new Vector3(0, 1, 0)),
            new(new Vector3(1.0f, 0.0f, 1.0f), new Vector2(1, 0), new Vector3(0, 1, 0)),
        ];
        HorizontalQuad = horizontalQuad;
    }

    public static readonly Shape<PositionTextureNormalVertex> Cube;

    public static readonly Shape<PositionTextureNormalVertex> IsometricCube;

    public static readonly Shape<PositionTextureNormalVertex> HorizontalQuad;

    public static readonly Shape<PositionTextureNormalVertex> VerticalQuad;
}

public static class PositionTextureShapes
{
    private static ImmutableArray<PositionTextureVertex> RecreateShape(ImmutableArray<PositionTextureNormalVertex> shape)
    {
        var builder = ImmutableArray.CreateBuilder<PositionTextureVertex>(shape.Length);
        foreach (PositionTextureNormalVertex positionTextureNormalVertex in shape)
        {
            builder.Add(new PositionTextureVertex(positionTextureNormalVertex.Position, positionTextureNormalVertex.TextureCoords));
        }
        return builder.MoveToImmutable();
    }

    static PositionTextureShapes()
    {
        Cube = PositionTextureNormalShapes.Cube.Reshape(RecreateShape);
        HorizontalQuad = PositionTextureNormalShapes.HorizontalQuad.Reshape(RecreateShape);
        VerticalQuad = PositionTextureNormalShapes.VerticalQuad.Reshape(RecreateShape);
    }

    public static readonly Shape<PositionTextureVertex> Cube;

    public static readonly Shape<PositionTextureVertex> HorizontalQuad;

    public static readonly Shape<PositionTextureVertex> VerticalQuad;
}

public static class PositionNormalColorShapes
{
    private static ImmutableArray<PositionNormalColorVertex> RecreateShape(ImmutableArray<PositionTextureNormalVertex> shape, SrgbColor color)
    {
        var builder = ImmutableArray.CreateBuilder<PositionNormalColorVertex>(shape.Length);
        foreach (PositionTextureNormalVertex positionTextureNormalVertex in shape)
        {
            builder.Add(new PositionNormalColorVertex(positionTextureNormalVertex.Position, positionTextureNormalVertex.Normal, color));
        }
        return builder.MoveToImmutable();
    }

    static PositionNormalColorShapes()
    {
        Cube = PositionTextureNormalShapes.Cube.Reshape(vertices => RecreateShape(vertices, new SrgbColor(255, 255, 255, 255)));
        IsometricCube = PositionTextureNormalShapes.IsometricCube.Reshape(vertices => RecreateShape(vertices, new SrgbColor(255, 255, 255, 255)));
        HorizontalQuad = PositionTextureNormalShapes.HorizontalQuad.Reshape(vertices => RecreateShape(vertices, new SrgbColor(255, 255, 255, 255)));
        VerticalQuad = PositionTextureNormalShapes.VerticalQuad.Reshape(vertices => RecreateShape(vertices, new SrgbColor(255, 255, 255, 255)));
    }

    public static readonly Shape<PositionNormalColorVertex> Cube;

    public static readonly Shape<PositionNormalColorVertex> IsometricCube;

    public static readonly Shape<PositionNormalColorVertex> HorizontalQuad;

    public static readonly Shape<PositionNormalColorVertex> VerticalQuad;
}

public static class PositionColorShapes
{
    static PositionColorShapes()
    {
        ImmutableArray<PositionColorVertex> horizontalQuadLines =
        [
            new(new Vector3(-1.0f, 0.0f, -1.0f), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(1.0f, 0.0f, -1.0f), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(1.0f, 0.0f, 1.0f), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1.0f, 0.0f, 1.0f), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1.0f, 0.0f, -1.0f), new SrgbColor(255, 255, 255, 255)),
        ];
        HorizontalQuadLines = horizontalQuadLines;

        ImmutableArray<PositionColorVertex> cubeLines =
        [
            new(new Vector3(-1, -1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1, -1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1, -1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1, -1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1, -1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1, -1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1, -1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1, -1, -1), new SrgbColor(255, 255, 255, 255)),

            new(new Vector3(-1,  1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1,  1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1,  1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1,  1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1,  1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1,  1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1,  1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1,  1, -1), new SrgbColor(255, 255, 255, 255)),

            new(new Vector3(-1, -1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1,  1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1, -1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1,  1, -1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1, -1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3( 1,  1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1, -1,  1), new SrgbColor(255, 255, 255, 255)),
            new(new Vector3(-1,  1,  1), new SrgbColor(255, 255, 255, 255)),
        ];
        CubeLines = cubeLines;
    }

    public static readonly Shape<PositionColorVertex> HorizontalQuadLines;

    public static readonly Shape<PositionColorVertex> CubeLines;
}

public static class PositionShapes
{
    private static ImmutableArray<PositionVertex> RecreateShape(ImmutableArray<PositionTextureNormalVertex> shape)
    {
        var builder = ImmutableArray.CreateBuilder<PositionVertex>(shape.Length);
        foreach (PositionTextureNormalVertex positionTextureNormalVertex in shape)
        {
            builder.Add(new PositionVertex(positionTextureNormalVertex.Position));
        }
        return builder.MoveToImmutable();
    }

    static PositionShapes()
    {
        Cube = PositionTextureNormalShapes.Cube.Reshape(RecreateShape);
        HorizontalQuad = PositionTextureNormalShapes.HorizontalQuad.Reshape(RecreateShape);
        VerticalQuad = PositionTextureNormalShapes.VerticalQuad.Reshape(RecreateShape);

        ImmutableArray<PositionVertex> cubeLines =
        [
            new(new Vector3(-1, -1, -1)),
            new(new Vector3( 1, -1, -1)),
            new(new Vector3( 1, -1, -1)),
            new(new Vector3( 1, -1,  1)),
            new(new Vector3( 1, -1,  1)),
            new(new Vector3(-1, -1,  1)),
            new(new Vector3(-1, -1,  1)),
            new(new Vector3(-1, -1, -1)),

            new(new Vector3(-1,  1, -1)),
            new(new Vector3( 1,  1, -1)),
            new(new Vector3( 1,  1, -1)),
            new(new Vector3( 1,  1,  1)),
            new(new Vector3( 1,  1,  1)),
            new(new Vector3(-1,  1,  1)),
            new(new Vector3(-1,  1,  1)),
            new(new Vector3(-1,  1, -1)),

            new(new Vector3(-1, -1, -1)),
            new(new Vector3(-1,  1, -1)),
            new(new Vector3( 1, -1, -1)),
            new(new Vector3( 1,  1, -1)),
            new(new Vector3( 1, -1,  1)),
            new(new Vector3( 1,  1,  1)),
            new(new Vector3(-1, -1,  1)),
            new(new Vector3(-1,  1,  1)),
        ];
        CubeLines = cubeLines;
    }

    public static readonly Shape<PositionVertex> Cube;

    public static readonly Shape<PositionVertex> HorizontalQuad;

    public static readonly Shape<PositionVertex> VerticalQuad;

    public static readonly Shape<PositionVertex> CubeLines;
}

