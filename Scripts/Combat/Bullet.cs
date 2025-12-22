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

    public override void _Ready()
    {
        startPos = GlobalPosition;
        
        // RigidBody3D cannot detect Areas directly via signals.
        // We add a child Area3D specifically to detect Shield Areas.
        var detector = new Area3D();
        detector.Name = "ShieldDetector";
        detector.CollisionLayer = 0; // Don't get hit by things
        detector.CollisionMask = 2;  // Detect Shields (Layer 2)
        
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

        // Add Visual Upgrade
        var viz = new VisualBullet();
        AddChild(viz);

        // Apply Muzzle Velocity (Instant speed, not acceleration)
        // Note: FireSystem sets the base LinearVelocity (Ship Velocity) before Adding Child.
        // We add our muzzle velocity to that.
        // LinearVelocity += -GlobalTransform.Basis.Z * speed;
    }

    // Update is called once per frame
    public override void _PhysicsProcess(double delta)
    {
        // Standard ballistic movement is handled by physics engine (LinearVelocity).
        ApplyCentralForce(-Transform.Basis.Z * speed * (float)delta);
        if (GlobalPosition.DistanceSquaredTo(startPos) >= range)
            QueueFree();
    }

    public void _OnBodyEntered(Node body)
    {
        GD.Print("Bullet collided with: ", body.Name);

        // Handle RigidBody (Hull) collisions
        if (body is IDamageable dmg)
        {
            GD.Print("Body is Damageable");
            dmg.Damage(damage, -Transform.Basis.Z, GlobalPosition);
        }

        QueueFree();
    }

    public new RigidBody3D Owner { get; set; }

    public void _OnAreaEntered(Area3D area)
    {
        GD.Print("Bullet area collision: ", area.Name);
        
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
                 foreach(Node child in parentRB.GetChildren())
                 {
                     if (child is Shield s) { shield = s; break; }
                     // Check one level deep (e.g. Visual)
                     foreach(Node grandchild in child.GetChildren())
                     {
                         if (grandchild is Shield gs) { shield = gs; break; }
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
            
            QueueFree();
            return;
        }
    }
}
