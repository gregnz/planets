using Godot;
using System;

public partial class PlanetSurface : Node3D
{
    [Export] public int Size = 100;
    [Export] public float ScaleFactor = 2.0f;
    [Export] public float HeightScale = 20.0f;
    [Export] public FastNoiseLite Noise;
    [Export] public Material TerrainMaterial;

    private MeshInstance3D _meshInstance;
    private StaticBody3D _staticBody;
    private CollisionShape3D _collisionShape;

    public override void _Ready()
    {
        if (Noise == null)
        {
            Noise = new FastNoiseLite();
            Noise.Seed = (int)Time.GetTicksMsec();
            Noise.Frequency = 0.02f;
            Noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        }

        GenerateTerrain();
    }

    public void GenerateTerrain()
    {
        // Cleanup existing
        if (_meshInstance != null) _meshInstance.QueueFree();
        if (_staticBody != null) _staticBody.QueueFree();

        _meshInstance = new MeshInstance3D();
        
        AddChild(_meshInstance);

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        // Simple grid generation
        // Center at 0,0
        int halfSize = Size / 2;

        for (int z = -halfSize; z < halfSize; z++)
        {
            for (int x = -halfSize; x < halfSize; x++)
            {
                // Triangle 1 (Reversed winding for Up Normal)
                AddVertex(surfaceTool, x, z);
                AddVertex(surfaceTool, x, z + 1);
                AddVertex(surfaceTool, x + 1, z);

                // Triangle 2 (Reversed winding for Up Normal)
                AddVertex(surfaceTool, x + 1, z);
                AddVertex(surfaceTool, x, z + 1);
                AddVertex(surfaceTool, x + 1, z + 1);
            }
        }

        surfaceTool.GenerateNormals();
        
        // Coloring based on height
        // Note: Godot 4 SurfaceTool doesn't support vertex colors easily without specific shader
        // But we can try setting UVs for a gradient texture or just vertex colors if material supports VERTEX_COLOR
        
        ArrayMesh mesh = surfaceTool.Commit();
        _meshInstance.Mesh = mesh;
        _meshInstance.MaterialOverride = TerrainMaterial ?? GetDefaultMaterial();
        
        // Physics - Use helper
        // This creates a StaticBody3D and CollisionShape3D as children of meshInstance
        _meshInstance.CreateTrimeshCollision();
        
        // Find the generated body to set layers
        StaticBody3D body = null;
        foreach(var child in _meshInstance.GetChildren())
        {
            if (child is StaticBody3D sb)
            {
                body = sb;
                break;
            }
        }
        
        if (body != null)
        {
            body.CollisionLayer = 3; 
            body.CollisionMask = 3;
            GD.Print("Terrain Collision Generated Successfully on Layers 1,2");
        }
        else
        {
             GD.PrintErr("Failed to generate Terrain Collision Body!");
        }
        
        // Scale everything
        // Note: We handled Vertex position scaling, but if we wanted to scale the node we could.
        // Instead we applied ScaleFactor during vertex creation.
    }

    private void AddVertex(SurfaceTool st, int x, int z)
    {
        float h = GetHeight(x, z);
        
        // UV mapping
        st.SetUV(new Vector2(x, z) / Size);
        
        // Color based on height (Low=Blue/Sand, Mid=Green, High=White)
        Color c = Colors.Green;
        if (h < HeightScale * 0.1f) c = Colors.SandyBrown;
        else if (h > HeightScale * 0.7f) c = Colors.Snow;
        st.SetColor(c);

        st.AddVertex(new Vector3(x * ScaleFactor, h, z * ScaleFactor));
    }

    public float GetHeight(float x, float z)
    {
        return Noise.GetNoise2D(x, z) * HeightScale;
    }
    
    private Material GetDefaultMaterial()
    {
        var mat = new StandardMaterial3D();
        mat.VertexColorUseAsAlbedo = true; // Use the colors we set
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled; // Double sided
        mat.AlbedoColor = Colors.White; // Base color
        return mat;
    }
}
