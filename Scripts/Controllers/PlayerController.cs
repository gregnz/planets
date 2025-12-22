using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using Godot;
using Godot.Collections;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Environment;
using Planetsgodot.Scripts.Missions;
using Planetsgodot.Scripts.AI;

namespace Planetsgodot.Scripts.Controllers;

public partial class PlayerController : RigidBody3D, IDamageable, ITarget
{
    public CombatDirector.Squadron.Attitude Attitude => CombatDirector.Squadron.Attitude.Friend;

    int i = 0;
    internal GameController gameController;
    public ShipController ship = new();
    ShipFactory.ShipSpec shipSpecification;
    FireSystem fireSystem;
    public PackedScene explosion;
    private bool amIDead = false;

    // private ShieldModifier _shieldModifier;
    private PlayerState _state;
    private SignalBus signalBus;
    private float t = 0;
    private Shield shield;

    // Surface Mode
    public bool SurfaceMode = false;
    private bool _wasSurfaceMode = false;

    public PlayerState State
    {
        get => _state;
    }

    public class PlayerState
    {
        internal Vector3 Rotation;
        internal Vector3 Pos;
        internal RigidBody3D CurrentTarget;
        internal ShipFactory.ShipSpec.Hardpoint CurrentHp;
        internal List<ShipFactory.ShipSpec.Hardpoint> Hps;
        internal ShieldSpec Shield;
        internal ArmorSpec Armor;
    }

    public override void _Ready()
    {
        // Add to Player group for NPC collision avoidance detection
        AddToGroup("Player");

        _state = new PlayerState();
        fireSystem = new FireSystem(this);

        string shipId = "Anaconda"; // Fallback

        if (
            CampaignManager.Instance != null
            && CampaignManager.Instance.State != null
            && CampaignManager.Instance.State.UnlockedShips.Count > 0
        )
        {
            // Use the most recently unlocked ship
            shipId = CampaignManager.Instance.State.UnlockedShips[
                CampaignManager.Instance.State.UnlockedShips.Count - 1
            ];
            GD.Print($"[PlayerParams] Loading Ship: {shipId}");
        }

        shipSpecification = ShipFactory.presetFromString(shipId);
        ship.initialiseFromSpec(shipSpecification, this, fireSystem);

        Node3D VisualsNode = this.GetChild(0) as Node3D;
        new ShipBuilder().Build(shipSpecification, VisualsNode);
        
        // Add Shield
        shield = new Shield();
        VisualsNode.AddChild(shield);
        ship.SetShieldVisuals(shield);

        // _shieldModifier = GetComponentInChildren<ShieldModifier>();
        fireSystem.Initialise(shipSpecification);

        SetCollisionLayerValue(1, false);
        CollisionLayer = 2;
        CollisionMask = 60; // Include Layer 32 (Asteroids)

        // Lock physics to XZ plane
        AxisLockLinearY = true;
        AxisLockAngularX = true;
        AxisLockAngularZ = true;

        gameController = GetNode<GameController>("/root/GameController");
        gameController.player = this;

        signalBus = GetNode<SignalBus>("/root/SignalBus");
    }

    public override void _Process(double delta)
    {
        t += (float)delta;
        if (t > 1)
        {
            signalBus.EmitSignal("StatusChanged", GetStatus());

            // Debug Y position
            if (_state.Pos.Y > 0.1f || _state.Pos.Y < -0.1f)
                GD.Print($"Player Y Position Warning: {_state.Pos.Y}");

            t = 0;
        }

        fireSystem.Update(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        ship.HandleMovement(shipSpecification, this, delta, false, false);
        PhysicsDirectSpaceState3D spaceState3D = GetWorld3D().DirectSpaceState;

        if (SurfaceMode)
        {
            if (!_wasSurfaceMode)
            {
                // Enter Surface Mode
                AxisLockLinearY = true; // We control Y directly, no physics
                AxisLockAngularX = false;
                AxisLockAngularZ = false;

                // Disable physics-based gravity and collision
                GravityScale = 0;
                ContinuousCd = false;

                // We don't need collision with terrain - we control height directly
                CollisionLayer = 0; // No collision layer
                CollisionMask = 0; // Collide with nothing

                _wasSurfaceMode = true;
            }

            // Get terrain height at current position
            var surface = GetTree().CurrentScene.GetNodeOrNull<PlanetSurface>("PlanetSurface");
            if (surface != null)
            {
                // Query terrain height at player's XZ position (in terrain's local coords)
                // Note: PlanetSurface uses ScaleFactor, so we need to convert
                float terrainX = GlobalPosition.X / surface.ScaleFactor;
                float terrainZ = GlobalPosition.Z / surface.ScaleFactor;
                float terrainHeight = surface.GetHeight(terrainX, terrainZ);

                // Fixed hover offset above terrain
                float hoverHeight = 1f;
                float targetY = terrainHeight + hoverHeight;

                // Smoothly lerp to target height
                Vector3 pos = GlobalPosition;
                pos.Y = Mathf.Lerp(pos.Y, targetY, (float)delta * 5.0f);
                GlobalPosition = pos;

                // Calculate terrain slope for alignment (using finite differences)
                float sampleDist = 1.0f;
                float hLeft = surface.GetHeight(terrainX - sampleDist, terrainZ);
                float hRight = surface.GetHeight(terrainX + sampleDist, terrainZ);
                float hBack = surface.GetHeight(terrainX, terrainZ - sampleDist);
                float hForward = surface.GetHeight(terrainX, terrainZ + sampleDist);

                // Calculate normal from height differences
                Vector3 normal = new Vector3(
                    (hLeft - hRight) / (2 * sampleDist * surface.ScaleFactor),
                    1.0f,
                    (hBack - hForward) / (2 * sampleDist * surface.ScaleFactor)
                ).Normalized();

                AlignWithNormal(normal, (float)delta);
            }
        }
        else
        {
            if (_wasSurfaceMode)
            {
                // Exit Surface Mode
                AxisLockLinearY = true;
                AxisLockAngularX = true;
                AxisLockAngularZ = true;

                GravityScale = 0;

                // Restore space collision (Layer 2, Mask for NPCs/Asteroids)
                CollisionLayer = 2;
                CollisionMask = 60;

                // Reset rotation to flat
                Rotation = new Vector3(0, Rotation.Y, 0);
                _wasSurfaceMode = false;
            }
        }

        // Handle Firing Input (Continuous polling)
        if (Input.IsActionPressed("Fire"))
        {
            fireSystem.Fire();
            signalBus.EmitSignal("WeaponStatusChanged", fireSystem.activeHardpoint.GetStatus());
        }
        else if (Input.IsActionJustReleased("Fire"))
        {
            fireSystem.firing = false;
            fireSystem.StopFiring();
        }

        if (fireSystem.firing) { }
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        base._IntegrateForces(state);
        if (!SurfaceMode)
        {
            Vector3 velocity = state.LinearVelocity;
            velocity.Y = 0;
            state.LinearVelocity = velocity;
        }
    }

    public override void _Input(InputEvent inputEvent)
    {
        Key[] weaponKeys =
        {
            Key.Key1,
            Key.Key2,
            Key.Key3,
            Key.Key4,
            Key.Key5,
            Key.Key6,
            Key.Key7,
            Key.Key8,
            Key.Key9,
        };

        for (int i = 0; i < shipSpecification.shipsWeapons.Count; i++)
        {
            if (Input.IsKeyPressed(weaponKeys[i]))
            {
                fireSystem.StopFiring();
                fireSystem.activeHardpoint = shipSpecification.hardpoints[i];
                signalBus.EmitSignal("WeaponStatusChanged", fireSystem.activeHardpoint.GetStatus());
            }
        }

        if (Input.IsActionJustPressed("SelectNextTarget"))
        {
            GD.Print("Player: SelectNextTarget pressed");
            ChangeTarget(gameController.RequestNextTarget());
        }

        if (Input.IsActionJustPressed("SelectPreviousTarget"))
        {
            ChangeTarget(gameController.RequestPrevTarget());
        }

        // Squad Commands
        if (inputEvent is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            switch (keyEvent.Keycode)
            {
                case Key.F1:
                    SendSquadOrder(CombatDirector.OrderType.FormUp);
                    break;
                case Key.F2:
                    SendSquadOrder(CombatDirector.OrderType.AttackTarget, fireSystem.currentTarget);
                    break;
                case Key.F3:
                    SendSquadOrder(CombatDirector.OrderType.FreeFire);
                    break;
                case Key.F4:
                    SendSquadOrder(CombatDirector.OrderType.Evasion);
                    break;
            }
        }

        // Firing logic moved to _PhysicsProcess

        ship.MovementX = 0;
        ship.MovementY = 0;
        if (Input.IsActionPressed("turn_left"))
            ship.MovementX = -1;
        if (Input.IsActionPressed("turn_right"))
            ship.MovementX = 1;
        if (Input.IsActionPressed("forward_thrust"))
            ship.MovementY = 1;
        if (Input.IsActionPressed("backward_thrust"))
            ship.MovementY = -1;

        if (Input.IsActionPressed("boost"))
            ship.boosting = true;
        else
            ship.boosting = false;

        _state.Rotation = Rotation;
        _state.Pos = Position;
        _state.CurrentTarget = null;
        _state.Shield = ship.Shield;
        _state.Armor = ship.Armor;
        if (Input.IsActionPressed("boost"))
            ship.boosting = true;
        else
            ship.boosting = false;

        _state.Rotation = Rotation;
        _state.Pos = Position;
        _state.CurrentTarget = null;
        _state.Shield = ship.Shield;
        _state.Armor = ship.Armor;
    }

    private void ChangeTarget(ITarget newTarget)
    {
        if (
            fireSystem.currentTarget != null
            && GodotObject.IsInstanceValid(fireSystem.currentTarget as Node)
        )
        {
            fireSystem.currentTarget.SetTargeted(false);
        }

        fireSystem.currentTarget = newTarget;

        if (
            fireSystem.currentTarget != null
            && GodotObject.IsInstanceValid(fireSystem.currentTarget as Node)
        )
        {
            fireSystem.currentTarget.SetTargeted(true);
        }
    }

    public void Destroy()
    {
        amIDead = true;
        // GameObject explosion_ = Instantiate(explosion, transform.position, Quaternion.identity, transform.parent);
        // foreach (ParticleSystem p in explosion_.GetComponents<ParticleSystem>())
        // {
        // 	p.Play();
        // }

        // Destroy(explosion_, 5);
        // this.gameController.DeregisterTarget(gameObject);
        // this.gameObject.SetActive(false);
        // Destroy(gameObject.GetComponent<Ship>().meshObj);
        // Destroy(gameObject.GetComponent<UserInterface>().uiComponent);
        // Destroy(this.gameObject, 2);
    }

    public void Destroy(List<Node> createdNodes)
    {
        Destroy();
    }

    public void Damage(HardpointSpec currentHardpointSpec, Vector3 hit, double deltaTime)
    {
        // shield?.OnHit(hit); // Handled by ShipController
        ship.Damage(currentHardpointSpec, hit, deltaTime);
        UpdateShieldState();
    }

    public void Damage(float damage, Vector3 transformForward, Vector3 hitPosition = default)
    {
        // Shield hit visual handled by ShipController if quadrant active
        ship.Damage(damage, transformForward, hitPosition);
        UpdateShieldState();
    }
    
    private void UpdateShieldState()
    {
        if (shield == null || ship.Shield == null) return;
        
        bool shieldUp = false;
        foreach(float s in ship.Shield.strength) 
        {
            if (s > 0) 
            { 
                shieldUp = true; 
                break; 
            }
        }
        shield.SetActive(shieldUp);
    }

    public bool IsDead()
    {
        return amIDead;
    }

    public void _OnBodyEntered(Node body)
    {
        GD.Print("Player collided with: ", body.Name);
    }

    public RigidBody3D GetRigidBody3D()
    {
        return this;
    }

    public Dictionary GetStatus()
    {
        Dictionary status = ship.ToUIDict();
        status["linear_velocity"] = LinearVelocity.Length();
        status["heat"] = fireSystem.heatTotal;
        status["position"] = Position.X + " " + Position.Z;
        return status;
    }

    public void SetTargeted(bool b) { }

    public ShipController MyShip()
    {
        return ship;
    }

    public Node3D GetTargetNode()
    {
        return fireSystem?.currentTarget as Node3D;
    }

    private void SendSquadOrder(CombatDirector.OrderType type, ITarget target = null)
    {
        // Default formation position?
        // For now just pass type and target
        var order = new CombatDirector.SquadOrder(type, target);
        
        if (gameController != null && gameController.combatDirector != null)
        {
             gameController.combatDirector.IssuePlayerSquadronOrder(order);
             GD.Print($"Player Issued Order: {type}");
        }
    }

    private void AlignWithNormal(Vector3 normal, float delta)
    {
        // Align Local Up with Normal
        Transform3D xform = GlobalTransform;
        Vector3 currentUp = xform.Basis.Y;

        Vector3 axis = currentUp.Cross(normal).Normalized();
        float angle = Mathf.Acos(Mathf.Clamp(currentUp.Dot(normal), -1f, 1f));

        if (angle > 0.01f)
        {
            // Apply Torque to rotate
            // Gentle P-controller alignment
            Vector3 torque = axis * angle * 20.0f * Mass * delta; // Reduced from 200

            // Strong damping for smooth motion
            torque -= AngularVelocity * 50.0f * Mass * delta; // Increased from 10

            ApplyTorque(torque);
        }
    }
}

public interface IShipController
{
    public ShipController MyShip();
}
