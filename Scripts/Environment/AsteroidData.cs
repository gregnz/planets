using Godot;

namespace Planetsgodot.Scripts;

/// <summary>
/// Lightweight data structure for asteroid state.
/// Used for data-oriented processing without individual Godot nodes.
/// </summary>
public struct AsteroidData
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float OrbitAngle;      // Current angle in orbit (radians)
    public float OrbitRadius;     // Distance from field center
    public float Scale;           // Size multiplier
    public float Integrity;       // Health (0-100)
    public bool IsDestroyed;      // Marked for removal from rendering
    public int ActiveBodyIndex;   // Index in physics pool (-1 if not active)
    
    public static AsteroidData Create(Vector3 position, float orbitRadius, float orbitAngle, float scale)
    {
        return new AsteroidData
        {
            Position = position,
            Velocity = Vector3.Zero,
            OrbitAngle = orbitAngle,
            OrbitRadius = orbitRadius,
            Scale = scale,
            Integrity = 100f,
            IsDestroyed = false,
            ActiveBodyIndex = -1
        };
    }
}
