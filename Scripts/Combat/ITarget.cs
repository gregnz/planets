using Godot;
using Godot.Collections;
using Planetsgodot.Scripts.Controllers;

namespace Planetsgodot.Scripts.Combat;

public interface ITarget
{
    Vector3 Position { get; set; }
    Vector3 GlobalPosition { get; }
    Vector3 LinearVelocity { get; set; }
    RigidBody3D GetRigidBody3D();
    public Dictionary GetStatus();
    void SetTargeted(bool b);
    ShipController MyShip();
    CombatDirector.Squadron.Attitude Attitude { get; }
}
