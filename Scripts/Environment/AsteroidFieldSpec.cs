using Godot;

namespace Planetsgodot.Scripts.Environment;

public class AsteroidFieldSpec
{
    public bool Enabled { get; set; } = false;
    public int Count { get; set; } = 1000;
    public float Radius { get; set; } = 1000f;
    public float OrbitSpeed { get; set; } = 0.5f;
    public Vector3 Center { get; set; } = Vector3.Zero;

    // Helper properties for Mission Scripts (2D Plane)
    public float X
    {
        get => Center.X;
        set => Center = new Vector3(value, 0, Center.Z);
    }

    public float Y
    {
        get => Center.Z; // Maps to Z in 3D space
        set => Center = new Vector3(Center.X, 0, value); // Should set Z!
    }

    // Factory method for quick setup
    public static AsteroidFieldSpec Create(int count, float radius)
    {
        return new AsteroidFieldSpec
        {
            Enabled = true,
            Count = count,
            Radius = radius
        };
    }
}
