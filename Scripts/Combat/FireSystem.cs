using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Godot.Collections;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Controllers;
using Planetsgodot.Scripts.Environment;
using utils;
using System.Linq;

namespace Planetsgodot.Scripts.Combat;

public partial class FireSystem : Node
{
    private bool isCooldown;

    int barrelRotation = -1;
    Vector3 barrelOffset = new Vector3(0.1f, 0.0f, 0.3f);
    public bool firing = false;

    private PackedScene ballisticShot = GD.Load<PackedScene>("res://bullet.tscn");


    public float heatTotal = 0;
    public float[] heatSinks = new float[] { 0.2f };

    public ITarget currentTarget { get; set; }

    Gradient origRendererGradient;

    private float _fireTime;
    public List<ShipFactory.ShipSpec.Hardpoint> hardpoints { get; set; }
    List<ShipFactory.ShipSpec.Hardpoint> inRange = new();
    public ShipFactory.ShipSpec.Hardpoint activeHardpoint;
    private readonly RigidBody3D owner;

    public FireSystem(RigidBody3D owner)
    {
        this.owner = owner;
    }

    public void Update(double delta)
    {
        if (currentTarget == null || !GodotObject.IsInstanceValid(currentTarget as Node))
            return;

        foreach (var hp in hardpoints)
        {
            if (hp.isTurret)
            {
                Node3D content = hp.hardpointContent;
                if (content == null)
                    continue;

                Vector3 targetPos = currentTarget.Position;
                Vector3 currentPos = content.GlobalPosition;

                // Calculate direction to target in global space
                // FORCE vector to be on XZ plane to prevent pitching up/down
                Vector3 diff = targetPos - currentPos;
                diff.Y = 0;
                Vector3 directionToTarget = diff.Normalized();

                // We want to rotate the turret to face this direction
                // Assuming turret default facing is Forward (-Z)
                // Use LookAt, but we might need to preserve "Up" vector relative to the ship (owner)

                // transform.LookAt(target, up)
                // But we want smooth rotation.

                try
                {
                    // Get current global rotation
                    Quaternion currentRot = content.GlobalTransform.Basis.GetRotationQuaternion();

                    // target look at
                    // Avoid looking straight up/down relative to owner which might cause gimbal lock issues if we aren't careful,
                    // but standard LookAt handles most cases.
                    // We want the Turret 'Forward' (-Z) to point at target.
                    // And we want the Turret 'Up' to align somewhat with the Ship 'Up' so it doesn't roll weirdly?
                    // Actually usually turrets have a fixed yaw axis and a pitch axis.
                    // For now, let's just do a free ball-turret rotation using LookAt/Slerp.

                    Vector3 up = Vector3.Up; // Use GLOBAL UP to compensate for ship roll/pitch

                    if (directionToTarget.LengthSquared() > 0.001f)
                    {
                        // Calculate desired basis
                        // Godot's LookingAt makes -Z point at target
                        // Negate direction so turret's visual forward (+Z or model-specific) faces target
                        Basis targetBasis = Basis.LookingAt(-directionToTarget, up);

                        // Slerp
                        Quaternion targetRot = targetBasis.GetRotationQuaternion();
                        Quaternion newRot = currentRot.Slerp(targetRot, (float)delta * 5.0f); // Speed 5.0

                        content.GlobalBasis = new Basis(newRot);
                    }
                }
                catch (Exception e)
                {
                    // LookAt failed (view vector parallel to up vector etc)
                }
            }
        }
    }

    public void Fire()
    {
        HardpointSpec currentHardpointSpec = activeHardpoint.HardpointSpec;

        float maxDuration = currentHardpointSpec.Duration;

        if (isCooldown || (maxDuration > 0 && _fireTime > maxDuration) || heatTotal > 1000000)
        {
            StopFiring();
            return;
        }

        heatTotal += currentHardpointSpec.HeatGenerated;
        if (
            currentHardpointSpec is HardpointSpec.Energy
            || currentHardpointSpec.name.Contains("Laser")
            || currentHardpointSpec.Type == "Laser"
        )
        {
            if (FireEnergy(activeHardpoint))
                return;
        }

        if (currentHardpointSpec is HardpointSpec.Ballistic)
        {
            if (!firing)
            {
                FireBallistic(activeHardpoint);
            }
        }

        if (currentHardpointSpec is HardpointSpec.Missile)
        {
            if (!firing)
            {
                FireMissile(activeHardpoint);
            }
        }

        /*
        if (currentHardpointSpec is HardpointSpec.PointDefence)
        {
            if (!firing)
            {
                StartCoroutine(FirePointDefence(activeHardpoint));
            }
        }
        */
    }

    private bool FireEnergy(ShipFactory.ShipSpec.Hardpoint hp)
    {
        firing = true;

        // Transform ignition = activeHardpoint.hardpointContent.transform.Find("Ignition");
        // ParticleSystem ignitionPS = ignition.GetComponent<ParticleSystem>();
        // ignitionPS.Play();

        // Transform impact = activeHardpoint.hardpointContent.transform.Find("Impact");
        // ParticleSystem impactPS = impact.GetComponent<ParticleSystem>();

        var position = activeHardpoint.hardpointContent.Position;
        float screenLength = RangeToScreenLength(hp.HardpointSpec);
        ((Energy)hp.hardpointContent).Enable(true);

        // MeshInstance3D node = (MeshInstance3D)hp.hardpointContent.GetNode("./LaserLine2");
        // node.Visible = true;
        // Transform3D transform = node.Transform;
        // transform.Basis = Basis.Identity;
        // node.Transform = transform;
        // node.Position = new Vector3(screenLength, 0, 0);
        // node.Scale = new Vector3(screenLength, 1, .25f);

        return false;
    }

    public Dictionary _RaycastHitPhysicsProcess(PhysicsDirectSpaceState3D spaceState)
    {
        // https://docs.godotengine.org/en/stable/tutorials/physics/ray-casting.html
        LineRenderer lr = new LineRenderer();

        Vector3 startPos = activeHardpoint.hardpointContent.GlobalPosition;
        lr.SetPosition(0, startPos);

        float screenLength = RangeToScreenLength(activeHardpoint.HardpointSpec);
        Vector3 endPos =
            startPos
            - activeHardpoint.hardpointContent.GlobalTransform.Basis.Z.Normalized()
                * screenLength
                * 2;
        Debug.Print(
            $"{startPos} {endPos} {activeHardpoint.hardpointContent.GlobalTransform.Basis.Z.Normalized()} {screenLength}"
        );

        lr.SetPosition(1, endPos);
        var query = PhysicsRayQueryParameters3D.Create(startPos, endPos, 0b111111101);
        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            GD.Print(
                "Hit at point: ",
                result["position"],
                result[$"collider"],
                endPos,
                screenLength
            );
        }

        // var box_pos = new Vector3(0, sin(_time * 4), 0)

        DebugDraw3D.DrawLine(startPos, endPos, Colors.Aqua);
        return result;
    }

    private void FireBallistic(ShipFactory.ShipSpec.Hardpoint hp)
    {
        // 1. Cooldown Check
        ulong now = Time.GetTicksMsec();
        // Assume RefireModifier is in Seconds (e.g. 1.0 = 1 sec delay, 0.1 = 100ms delay)
        // HardpointSpec values are like "1.00", "2.00".
        float delaySeconds = hp.HardpointSpec.RefireModifier;
        if (delaySeconds <= 0)
            delaySeconds = 0.1f; // Safety minimum
        ulong delayMs = (ulong)(delaySeconds * 1000f);

        if (now - hp.lastFireTime < delayMs)
        {
            return; // Still cooling down
        }

        hp.lastFireTime = now;

        firing = true;
        HardpointSpec currentHardpointSpec = hp.HardpointSpec;
        HardpointSpec.Ballistic currentWeaponSpec = (HardpointSpec.Ballistic)currentHardpointSpec;
        int missileCount = currentWeaponSpec.NumberMissiles;
        if (missileCount == 0)
            missileCount = 1;

        // Transform ignition = hp.hardpointContent.transform.Find("Ignition");
        // ParticleSystem ignitionPS = ignition.GetComponent<ParticleSystem>();
        // GameObject projectile = hp.hardpointContent.transform.Find("Projectile").gameObject;

        Vector3 position = hp.hardpointContent.Position;

        for (int i = 0; i < missileCount; i++)
        {
            if (!hp.HasAmmunition())
                break;

            // ignition.transform.localPosition = barrelOffset * barrelRotation;
            // ignitionPS.Play();

            if (currentWeaponSpec.isTurret && currentTarget != null)
            {
                Vector3 relPos = currentTarget.Position - owner.Position;
                // TODO: rotation = Quaternion.LookRotation(relPos, Vector3.Up);
                // GD.Print("Turret :" + rotation);
            }

            // Calculate offset for current barrel (left/right)
            // Only flip X, keep Z (forward) and Y constant.
            Vector3 currentBarrelOffset = new Vector3(
                barrelOffset.X * barrelRotation,
                barrelOffset.Y,
                barrelOffset.Z
            );

            // MUZZLE FLASH
            var flash = new MuzzleFlash();
            hp.hardpointContent.AddChild(flash);
            flash.Position = currentBarrelOffset;

            Bullet b = (Bullet)ballisticShot.Instantiate();

            // Fix: Transform barrel offset to global space
            // Use Hardpoint basis for Turrets (so offset rotates with turret)
            // Use Owner basis for Fixed weapons (so offset rotates with ship, ignoring weird hardpoint node rotations)
            Basis offsetBasis;
            if (currentWeaponSpec.isTurret)
                offsetBasis = hp.hardpointContent.GlobalBasis;
            else
                offsetBasis = owner.GlobalBasis;

            Vector3 globalBarrelOffset = offsetBasis * currentBarrelOffset;
            Vector3 spawnPosition = hp.hardpointContent.GlobalPosition + globalBarrelOffset;
            spawnPosition.Y = 0; // Roll compensation: keep bullets on y=0 plane
            b.Position = spawnPosition;

            b.enabled = true;
            b.damage = currentHardpointSpec.Damage;
            b.Owner = owner; // Assign Owner so bullet can ignore own shield
            b.range = RangeToScreenLength(currentWeaponSpec);
            b.AddCollisionExceptionWith(owner);
            // Add to CurrentScene instead of Root so they get destroyed on scene change
            owner.GetTree().CurrentScene.AddChild(b);
            b.LinearVelocity = owner.LinearVelocity;

            // Roll compensation: Only use Y rotation (Yaw)
            // Use Hardpoint rotation for Turrets, Owner rotation for Fixed
            Vector3 fireRotation;
            if (currentWeaponSpec.isTurret)
                fireRotation = hp.hardpointContent.GlobalRotation;
            else
                fireRotation = owner.GlobalRotation;

            b.GlobalRotation = new Vector3(0, fireRotation.Y, 0);

            /*
            if (missileCount == 1) bullet_.GetComponent<Renderer>().material.color = Color.blue;
            var cb = bullet_.GetComponent<Collider>();
            var c1 = GetComponentsInChildren<Collider>();
            foreach (Collider c in c1)
                Physics.IgnoreCollision(cb, c);

*/
            // Rigidbody Temporary_RigidBody = Temporary_Bullet_Handler.GetComponent<Rigidbody>();
            // //Tell the bullet to be "pushed" forward by an amount set by Bullet_Forward_Force.
            // Temporary_RigidBody.AddForce(transform.forward * 100f, ForceMode.Force);
            // Destroy(bullet, 2.0f);
            if (barrelRotation == -1)
                barrelRotation = 1;
            else
                barrelRotation = -1;

            // activeHardpoint.UpdateAmmunition(-1);
        }

        firing = false;
    }

    private async void FireMissile(ShipFactory.ShipSpec.Hardpoint hp)
    {
        firing = true;
        HardpointSpec currentHardpointSpec = hp.HardpointSpec;
        HardpointSpec.Missile currentWeaponSpec = (HardpointSpec.Missile)currentHardpointSpec;
        int missileCount = currentWeaponSpec.NumberMissiles;

        // Debug: Log missile count
        GD.Print(
            $"Firing {missileCount} missiles from {currentWeaponSpec.name}. CurrentTarget is {(currentTarget != null ? ((Node)currentTarget).Name : "NULL")}"
        );

        for (int i = 0; i < missileCount; i++)
        {
            var position = hp.hardpointContent.Position;
            if (!activeHardpoint.HasAmmunition())
                break;

            if (MissileManager.Instance != null)
            {
                 MissileManager.Instance.SpawnMissile(
                    owner, 
                    currentTarget as Node3D, 
                    hp.hardpointContent.GlobalPosition, 
                    hp.hardpointContent.GlobalBasis.GetRotationQuaternion(), 
                    owner.LinearVelocity,
                    i // Pass index for spread calculation
                );
            }
            else
            {
                GD.PrintErr("MissileManager instance is null!");
            }

            activeHardpoint.UpdateAmmunition(-1);

            // Stagger missile launches for visual effect (50ms between each)
            if (i < missileCount - 1)
            {
                await owner.ToSignal(
                    owner.GetTree().CreateTimer(0.05f),
                    SceneTreeTimer.SignalName.Timeout
                );
            }
        }

        firing = false;
    }

    public List<ShipFactory.ShipSpec.Hardpoint> GetHardpointsInRange(Vector3 tp, bool checkAmmo)
    {
        inRange.Clear();
        foreach (ShipFactory.ShipSpec.Hardpoint h in hardpoints)
        {
            if (checkAmmo && !h.HasAmmunition())
            {
                StopFiring(h);
                continue;
            }

            float range = h.HardpointSpec.MaxRange / 25;
            if (owner.Position.DistanceTo(tp) < range)
            {
                inRange.Add(h);
            }
            else
            {
                StopFiring(h);
            }
        }

        return inRange;
    }

    private static float RangeToScreenLength(HardpointSpec currentHardpointSpec)
    {
        return currentHardpointSpec.MaxRange / 20f * 2.5f;
    }

    private void FixedUpdate()
    {
        heatTotal -= CalculateHeatSinkageRate();
        if (heatTotal < 0)
            heatTotal = 0;
    }

    private float CalculateHeatSinkageRate()
    {
        return heatSinks.Sum();
    }

    // private System.Collections.IEnumerator FirePointDefence(ShipFactory.ShipSpec.Hardpoint hp)
    // {
    //     HardpointSpec.PointDefence spec = (HardpointSpec.PointDefence)hp.HardpointSpec;
    //
    //     for (int i = 0; i < spec.PointCount; i++)
    //     {
    //         var rDir = Random.insideUnitCircle;
    //         FireEnergy(spec, new Vector3(rDir.x, 0, rDir.y));
    //         yield return new WaitForSeconds(seconds: 0.05f);
    //         StopFiring();
    //     }
    //
    //     yield return null;
    // }

    /*

    private System.Collections.IEnumerator Cooldown()
    {
        HardpointSpec currentHardpointSpec = activeHardpoint.HardpointSpec;
        float firingDelay = currentHardpointSpec.FiringDelay;
        Debug.Log("Firing delay: " + firingDelay);
        if (firingDelay > 0)
        {
            // StopFiring();
            firing = false;
            isCooldown = true;
            yield return new WaitForSeconds(seconds: firingDelay);
        }

        isCooldown = false;
        _fireTime = 0f;
        yield return null;
    }
*/

    System.Collections.IEnumerator FadeLineRenderer()
    {
        // Gradient lineRendererGradient = new Gradient();
        //
        // float fadeSpeed = 0.2f;
        // float timeElapsed = 0f;
        // float alpha = 1f;
        //
        // while (timeElapsed < fadeSpeed)
        // {
        //     foreach (LineRenderer lr in energyLineRenderers)
        //     {
        //         alpha = Mathf.Lerp(1f, 0f, timeElapsed / fadeSpeed);
        //
        //         lineRendererGradient.SetKeys
        //         (
        //             lr.colorGradient.colorKeys,
        //             new[] {new GradientAlphaKey(alpha, timeElapsed / fadeSpeed)}
        //         );
        //         lr.colorGradient = lineRendererGradient;
        //
        //         // lr.SetPosition(0, transform.position);
        //         lr.SetPosition(1, transform.position + transform.forward * (5 * (1 - timeElapsed / fadeSpeed)));
        //     }
        //
        //     timeElapsed += Time.deltaTime;
        //     yield return null;
        // }
        //
        // foreach (LineRenderer lr in energyLineRenderers)
        // {
        //     lr.enabled = false;
        //     lr.colorGradient = origRendererGradient;
        // }
        yield return null;
    }

    internal void StopAllFiring()
    {
        foreach (ShipFactory.ShipSpec.Hardpoint hp in hardpoints)
        {
            StopFiring(hp);
        }
    }

    internal void StopFiring()
    {
        StopFiring(activeHardpoint);
    }

    internal void StopFiring(ShipFactory.ShipSpec.Hardpoint hp)
    {
        Energy energy = hp.hardpointContent as Energy;
        if (energy != null)
            energy.Enable(false);
        firing = false;

        /*
                Transform ignition = hp.hardpointContent.transform.Find("Ignition");
                if (ignition != null)
                {
                    ParticleSystem ignitionPS = ignition.GetComponent<ParticleSystem>();
                    if (ignitionPS != null)
                        ignitionPS.Stop();
                }
        
                Transform impact = hp.hardpointContent.transform.Find("Impact");
                if (impact != null)
                {
                    ParticleSystem impactPS = impact.GetComponent<ParticleSystem>();
                    if (impactPS != null) impactPS.Stop();
                }
        
                StartCoroutine(nameof(Cooldown));
                */
    }

    public void Initialise(ShipFactory.ShipSpec shipSpecification)
    {
        hardpoints = shipSpecification.hardpoints;
        activeHardpoint = hardpoints[0];

        StopAllFiring();
    }
}

public interface IDamageable
{
    void Destroy();
    void Damage(float damage, Vector3 transformForward, Vector3 hitPosition = default);

    public void Destroy(List<Node> createdNodes);
    void Damage(HardpointSpec currentHardpointSpec, Vector3 hit, double deltaTime);

    bool IsDead();

    public void _OnBodyEntered(Node body);
    Vector3 Position { get; set; }
    
    ShipController MyShip();
}

public class RaycastHit { }
