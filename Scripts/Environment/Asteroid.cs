using System;
using Godot;
using System.Collections.Generic;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Controllers;

namespace Planetsgodot.Scripts.Environment;

public partial class Asteroid : RigidBody3D, IDamageable, ITarget
{
    public CombatDirector.Squadron.Attitude Attitude => CombatDirector.Squadron.Attitude.Neutral;

    bool amIDead = false;
    private float integrity = 10000;
    public float radius = 40.0f; // Base radius for standard asteroid size

    public OptimizedAsteroidField OptimizedField; // Reference to the optimized field manager
    public int OptimizedPoolIndex = -1; // Index in the physics pool

    public override void _Ready()
    {
        _getRigidBody3D = this;
    }

    private RigidBody3D _getRigidBody3D;


    public void Destroy()
    {
        // Safe GameController access
        GameController gc = GetNodeOrNull<GameController>("/root/GameController");
        if (gc == null) gc = GetNodeOrNull<GameController>("/root/Root3D/GameController");

        if (gc != null)
        {
            gc.DeregisterTarget(this);
        }

        if (OptimizedField != null && OptimizedPoolIndex != -1)
        {
            // Report destruction to the optimized field manager
            OptimizedField.ReportAsteroidDestruction(this);
        }
        else
        {
            // Standard behavior for standalone asteroids
            this.Free();
        }
    }

    public void Destroy(List<Node> createdNodes)
    {
        Destroy();
    }


    public void Damage(HardpointSpec currentHardpointSpec, Vector3 hit, double deltaTime)
    {
        if (OptimizedField != null)
        {
            Vector3 dir = (GlobalPosition - hit).Normalized();
            OptimizedField.DamageAsteroid(OptimizedPoolIndex, currentHardpointSpec.Damage * (float)deltaTime,
                dir);
            return;
        }

        integrity -= currentHardpointSpec.Damage * (float)deltaTime;

        if (integrity < 0)
        {
            Destroy();
        }
    }

    public void Damage(float damage, Vector3 transformForward, Vector3 hitPosition = default)
    {
        if (OptimizedField != null)
        {
            OptimizedField.DamageAsteroid(OptimizedPoolIndex, damage, transformForward);
            return;
        }

        integrity -= damage;
        if (integrity < 0)
        {
            Destroy();
        }
    }

    public bool IsDead()
    {
        return amIDead;
    }

    public void _OnBodyEntered(Node body)
    {
        GD.Print("Asteroid collided with: ", body.Name);
    }

    enum AsteroidType
    {
        Iron,
    }

    public RigidBody3D GetRigidBody3D() => _getRigidBody3D;

    public Godot.Collections.Dictionary GetStatus()
    {
        Godot.Collections.Dictionary status = new Godot.Collections.Dictionary();
        status["name"] = "Asteroid";
        status["mass"] = Mass;
        status["type"] = AsteroidType.Iron.ToString();
        status["linear_velocity"] = LinearVelocity.Length();

        // Default values to prevent GUI crashes
        status["shield"] = new float[] { 0, 0, 0, 0 };
        status["armor"] = new float[] { integrity, 0, 0, 0 }; // Show integrity as armor
        status["position"] = $"{Position.X:F0} {Position.Z:F0}";
        status["state"] = "Orbiting";
        status["tactics"] = "None";
        status["debug_threat_name"] = "";
        status["debug_threat_time"] = 0f;
        status["acceleration"] = 0f;
        status["max_speed"] = 0f;

        return status;
    }

    public void SetTargeted(bool b)
    {
    }

    public ShipController MyShip()
    {
        throw new NotImplementedException();
    }
}