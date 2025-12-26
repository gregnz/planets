using System.Diagnostics;
using Godot;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Controllers;
using Planetsgodot.Scripts.Environment;

namespace Planetsgodot.Scripts.Combat;

public partial class Bullet : RigidBody3D
{
    // Start is called before the first frame update

    private float speed = 1000f;
    public float damage = 20f;
    public float range = 2500f;
    public Vector3 startPos;
    public bool enabled;
    public string ProjectileType = "Standard"; // Standard, Gauss

    public Color ImpactColor = new Color(1, 0.6f, 0); // Orange/Gold default
    public float ImpactSize = 1.0f;

    public override void _Ready()
    {
        startPos = GlobalPosition;

        // RigidBody3D cannot detect Areas directly via signals.
        // We add a child Area3D specifically to detect Shield Areas.
        var detector = new Area3D();
        detector.Name = "ShieldDetector";
        detector.CollisionLayer = 0; // Don't get hit by things
        detector.CollisionMask = 2; // Detect Shields (Layer 2)

        // Add shape to detector (small sphere)
        var shape = new CollisionShape3D();
        shape.Shape = new SphereShape3D() { Radius = 0.5f };
        detector.AddChild(shape);
        AddChild(detector);

        detector.Connect("area_entered", new Callable(this, "_OnAreaEntered"));

        // Ensure we detect Bodies (Hull) via standard RigidBody contact monitoring
        ContactMonitor = true;
        MaxContactsReported = 1;

        if (!IsConnected("body_entered", new Callable(this, "_OnBodyEntered")))
            Connect("body_entered", new Callable(this, "_OnBodyEntered"));

        // Add Visual Upgrade & Configure Impact
        Node3D viz;
        if (ProjectileType.Contains("Gauss") || ProjectileType.Contains("GAUSS"))
        {
            viz = new GaussVisualBullet();
            ImpactColor = new Color(0.2f, 0.8f, 1.0f); // Cyan
            ImpactSize = 2.5f; // Big impact
        }
        else
        {
            viz = new VisualBullet();
            ImpactColor = new Color(1.0f, 0.7f, 0.2f); // Standard Orange
            ImpactSize = 1.0f;
        }

        AddChild(viz);

        // Apply Muzzle Velocity (Instant speed, not acceleration)
        // ...
    }

    // Update is called once per frame
    public override void _PhysicsProcess(double delta)
    {
        // Standard ballistic movement is handled by physics engine (LinearVelocity).
        ApplyCentralForce(-Transform.Basis.Z * speed * (float)delta);
        if (GlobalPosition.DistanceSquaredTo(startPos) >= range * range)
            QueueFree();
    }

    private void SpawnImpact(Vector3 position, Vector3 normal)
    {
        var impact = new VisualImpact();
        impact.Configure(ImpactColor, ImpactSize);
        // Add to scene root so it doesn't get deleted with the bullet
        GetTree().CurrentScene.AddChild(impact);
        impact.GlobalPosition = position;

        // Align impact with normal (optional, simplistic look-at)
        if (normal != Vector3.Zero && normal != Vector3.Up)
            impact.LookAt(position + normal, Vector3.Up);
    }

    private void SpawnImpactWithRaycast(Node target, bool hitShield)
    {
        // Default values if raycast fails
        Vector3 finalPos = GlobalPosition;
        Vector3 finalNormal = Transform.Basis.Z; // Backwards (towards shooter)

        // Raycast from behind the bullet to ahead of it to find the surface
        var spaceState = GetWorld3D().DirectSpaceState;

        // Start 3m behind, end 3m ahead
        Vector3 origin = GlobalPosition + Transform.Basis.Z * 3.0f;
        Vector3 dest = GlobalPosition - Transform.Basis.Z * 3.0f;

        var query = PhysicsRayQueryParameters3D.Create(origin, dest);
        query.CollideWithBodies = !hitShield;
        query.CollideWithAreas = hitShield;

        if (hitShield)
            query.CollisionMask = 2; // Shield Layer
        else
            query.CollisionMask = 1; // Default/Hull

        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            finalPos = (Vector3)result["position"];
            finalNormal = (Vector3)result["normal"];
        }

        SpawnImpact(finalPos, finalNormal);
    }

    public void _OnBodyEntered(Node body)
    {
        // Handle RigidBody (Hull) collisions
        if (body is IDamageable dmg)
        {
            dmg.Damage(damage, -Transform.Basis.Z, GlobalPosition);
        }

        // Use Raycast to find hull surface
        SpawnImpactWithRaycast(body, false);

        QueueFree();
    }

    public new RigidBody3D Owner { get; set; }

    public void _OnAreaEntered(Area3D area)
    {
    
        // Handle Area3D (Shield) collisions
        if (area.Name == "ShieldArea")
        {
            // Find the Shield node to check quadrant
            var parentRB = area.GetParent() as RigidBody3D;

            // IGNORE FRIENDLY FIRE (Own Shield)
            if (parentRB == Owner)
            {
                return;
            }

            Shield shield = parentRB?.FindChild("Shield", true, false) as Shield;

            if (shield == null && parentRB != null)
            {
                // Fallback: Check for any child of type Shield manually
                foreach (Node child in parentRB.GetChildren())
                {
                    if (child is Shield s)
                    {
                        shield = s;
                        break;
                    }

                    // Check one level deep (e.g. Visual)
                    foreach (Node grandchild in child.GetChildren())
                    {
                        if (grandchild is Shield gs)
                        {
                            shield = gs;
                            break;
                        }
                    }

                    if (shield != null) break;
                }

                if (shield == null) GD.Print($"Bullet: Could not find Shield on {parentRB.Name}");
            }

            if (shield != null && !shield.IsQuadrantActive(GlobalPosition))
            {
                // Quadrant depleted - bullet passes through, don't destroy yet
                GD.Print("Bullet: Shield quadrant depleted, passing through");
                return;
            }

            // Active shield - apply damage via the parent ship
            if (parentRB is IDamageable damageable)
            {
                damageable.Damage(damage, -Transform.Basis.Z, GlobalPosition);
            }

            SpawnImpactWithRaycast(area, true); // Impact on shield surface
            QueueFree();
            return;
        }
    }
}
