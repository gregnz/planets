using System;
using System.Collections.Generic;
using Godot;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Controllers;


namespace Planetsgodot.Scripts.AI;

public enum TacticalPosition
{
    Direct,
    FlankLeft,
    FlankRight,
    Behind,
    Evade
}

/// <summary>
/// AI Pilot that outputs player-style controls (MovementX, MovementY, Boosting).
/// This ensures AI ships fly exactly like the player with no "cheating".
/// Refactored for smooth PID control and physics-based arrival.
/// </summary>
public class AIPilot
{
    // === OUTPUT: Same controls as player ===
    // MovementX: -1 = turn left, +1 = turn right
    // MovementY: -1 = backward thrust, +1 = forward thrust
    public float MovementX { get; private set; }
    public float MovementY { get; private set; }
    public bool Boosting { get; set; }
    public bool ForceBoost { get; set; }


    // === TARGET STATE ===
    public Vector3 TargetPosition { get; set; }
    public float TargetOrientation { get; set; } // Desired Y rotation in radians
    public float DesiredSpeed { get; set; } // Target speed (0 to ship's maxSpeed)
    public bool HasArrived { get; private set; }
    public bool OrientationMatched { get; private set; }

    // === EXCLUSIONS ===
    public List<Node> IgnoredColliders { get; set; } = new List<Node>();

    // === TUNING PARAMETERS ===
    public float ArrivalRadius = 5.0f;
    public float SlowdownRadius = 50.0f;

    // Rotation PID Settings
    // Rotation PID Settings
    // Proportional Gain (kp): How hard to turn based on angle error
    // Tuned for "Snappy" response (Star Wars style)
    private float _rotationKp = 25.0f;

    // Derivative Gain (kd): How much to resist spinning (Damping)
    private float _rotationKd = 5.0f;

    // === STATE ===
    // === STATE ===

    // === DEBUG INFO ===
    public float DebugAngleToTarget { get; private set; }
    public float DebugDistanceToTarget { get; private set; }
    public float DebugStoppingDistance { get; private set; }
    public float DebugCurrentSpeed { get; private set; }
    public float DebugAngularVelocity { get; private set; }

    /// <summary>
    /// Feed-forward velocity target (e.g. Leader velocity)
    /// </summary>
    public Vector3 MatchVelocity { get; set; } = Vector3.Zero;

    /// <summary>
    /// Main update method.
    /// Calculates steering forces and maps them to ship controls.
    /// </summary>
    public void UpdateControls(ShipController ship, RigidBody3D rb, float delta)
    {
        if (ship?.spec == null || rb == null)
        {
            MovementX = 0;
            MovementY = 0;
            Boosting = false;
            return;
        }

        // === 1. SENSING ===
        Vector3 currentPos = rb.GlobalPosition;
        Vector3 currentVel = rb.LinearVelocity;
        Vector3 forward = -rb.GlobalTransform.Basis.Z;
        float currentSpeed = currentVel.Length();
        float maxAccel = ship.spec.acceleration * 10.0f; // Approx force multiplier

        Vector3 toTarget = TargetPosition - currentPos;
        toTarget.Y = 0; // Planar
        float dist = toTarget.Length();

        DebugDistanceToTarget = dist;
        DebugCurrentSpeed = currentSpeed;
        DebugAngularVelocity = rb.AngularVelocity.Y;

        // === 2. TARGET SPEED CALCULATION (ARRIVAL LOGIC) ===
        // We want to arrive at velocity 0 (or MatchVelocity).
        // Physics Formula: v^2 = u^2 + 2as => v_limit = Sqrt(2 * a * dist)
        // This ensures we can stop exactly at the target.

        float brakingDist = dist;
        float speedLimit = Mathf.Sqrt(2.0f * maxAccel * brakingDist);

        // Apply user-defined constraints
        float targetSpeed = ship.spec.maxSpeed;
        if (DesiredSpeed >= 0) targetSpeed = Mathf.Min(targetSpeed, DesiredSpeed);

        // Use the lower of physical limit or desired limit
        // We use 0.9 factor to be safe (under-shoot slightly rather than over-shoot)
        targetSpeed = Mathf.Min(targetSpeed, speedLimit * 0.9f);

        // Hysteresis for "Arrived" state
        if (!HasArrived && dist < ArrivalRadius && currentSpeed < 5.0f)
        {
            HasArrived = true;
        }
        else if (HasArrived && dist > ArrivalRadius * 1.5f)
        {
            HasArrived = false;
        }

        if (HasArrived) targetSpeed = 0f;

        // === 3. DESIRED VELOCITY ===
        Vector3 desiredVel = toTarget.Normalized() * targetSpeed;

        // Add Feed-Forward Velocity (Formation flying)
        if (MatchVelocity.LengthSquared() > 0.1f)
        {
            desiredVel += MatchVelocity;
            // Clamping is complex here: if matching leader, we might need to exceed maxSpeed temporarily?
            // For now, clamp to max + boost
            if (desiredVel.Length() > ship.spec.maxSpeed * 1.5f)
                desiredVel = desiredVel.Normalized() * ship.spec.maxSpeed * 1.5f;
        }

        // === 5. STEERING CONTROL (PD for Rotation) ===
        // Calculate desired heading
        Vector3 desiredHeading = desiredVel.Normalized();

        // If we want specific orientation (e.g. stopped and facing target)
        if (HasArrived && Mathf.Abs(TargetOrientation) > 0.001f && desiredVel.Length() < 1.0f)
        {
            desiredHeading = new Vector3(Mathf.Sin(TargetOrientation), 0, -Mathf.Cos(TargetOrientation));
        }
        else if (desiredVel.LengthSquared() < 0.1f)
        {
            // No movement desired, maintain current? or forward?
            desiredHeading = forward;
        }

        // Calculate Angle Error
        // Angle between Forward and Desired Heading
        // Use Cross product to determine sign (Left/Right)
        float angleError = SignedAngleTo(forward, desiredHeading, Vector3.Up);
        DebugAngleToTarget = Mathf.RadToDeg(angleError);

        // PD Controller
        // Torque = Kp * error - Kd * angular_velocity
        // MovementX is roughly Torque input (-1 to 1)

        float pTerm = angleError * _rotationKp;
        float dTerm = rb.AngularVelocity.Y * _rotationKd;

        // Capital ship agility modifier (sluggish turning)
        float agilityModifier = ship.spec.IsCapitalShip ? 0.2f : 1.0f;

        // TorqueDemand (Positive = Left) = Kp * Error - Kd * Velocity
        float torqueDemand = ((_rotationKp * agilityModifier) * angleError) - (_rotationKd * rb.AngularVelocity.Y);

        // Map TorqueDemand to MoveX which is inverted (Right is Positive)
        // MoveX = -TorqueDemand
        MovementX = Mathf.Clamp(-torqueDemand, -1.0f, 1.0f);

        // Determine precision state
        OrientationMatched = Mathf.Abs(angleError) < Mathf.DegToRad(2.0f) && Mathf.Abs(rb.AngularVelocity.Y) < 0.1f;


        // === 6. THRUST CONTROL (PID for Velocity) with TURN GATING ===
        // We just compare forward speed to desired forward speed component
        // Project desired velocity onto our forward vector
        float desiredForwardSpeed = desiredVel.Dot(forward);
        float currentForwardSpeed = currentVel.Dot(forward);
        float speedError = desiredForwardSpeed - currentForwardSpeed;

        // Proportional throttle control
        float throttle = Mathf.Clamp(speedError / 5.0f, -1.0f, 1.0f);

        // TURN GATING: Don't thrust if we are not facing the target
        // If error > 15 degrees, kill forward thrust (but allow braking/reverse)
        if (Mathf.Abs(angleError) > Mathf.DegToRad(15.0f))
        {
            // Only clamp positive thrust (allow braking)
            if (throttle > 0) throttle = 0;
        }

        MovementY = throttle;

        // Boost Logic: If we are far behind desired speed and strictly moving forward
        Boosting = (speedError > 20.0f && MovementY > 0.9f) || ForceBoost;

        // Revserse Logic Check: Don't reverse if we are moving fast forward (just brake)
        if (MovementY < 0 && currentForwardSpeed > 5.0f)
        {
            // Braking
            MovementY = -1.0f;
        }
    }

    // Helper for signed angle
    private float SignedAngleTo(Vector3 from, Vector3 to, Vector3 axis)
    {
        return Mathf.Atan2(
            from.Cross(to).Dot(axis),
            from.Dot(to)
        );
    }

    // === COLLISION AVOIDANCE ===

    // === UTILS ===

    public string GetDebugString()
    {
        // Capture key physics/logic states
        return
            $"TgtSpd:{DesiredSpeed:F1} CurSpd:{DebugCurrentSpeed:F1} Thr:{MovementY:F2} Boost:{Boosting} Arrived:{HasArrived}";
    }

    public void SetTargetPosition(Vector3 position)
    {
        TargetPosition = position;
        TargetOrientation = 0;
    }

    public void SetTargetPositionAndOrientation(Vector3 position, float orientationRadians)
    {
        TargetPosition = position;
        TargetOrientation = orientationRadians;
    }

    public void SetTargetEntity(ITarget target, float followDistance = 10.0f)
    {
        if (target == null) return;
        Vector3 targetPos = target.Position;
        Vector3 targetVel = target.LinearVelocity;

        if (targetVel.LengthSquared() > 0.1f)
        {
            TargetPosition = targetPos - targetVel.Normalized() * followDistance;
            TargetOrientation = Mathf.Atan2(targetVel.X, -targetVel.Z);
        }
        else
        {
            TargetPosition = targetPos + new Vector3(0, 0, followDistance); // Default behind
        }

        DesiredSpeed = targetVel.Length();
    }

    /// <summary>
    /// Calculate attack position relative to a target
    /// </summary>
    public void SetAttackPosition(ITarget target, TacticalPosition tactic, float distance = 15.0f)
    {
        if (target == null)
            return;

        Vector3 targetPos = target.Position;
        RigidBody3D targetRb = target.GetRigidBody3D();
        float targetYaw = targetRb?.Rotation.Y ?? 0;

        Vector3 offset;
        float orientation;

        switch (tactic)
        {
            case TacticalPosition.FlankLeft:
                // Position to target's left, facing toward target
                offset = new Vector3(-distance, 0, 0);
                orientation = targetYaw + Mathf.Pi / 2; // Face right toward target
                break;
            case TacticalPosition.FlankRight:
                // Position to target's right, facing toward target
                offset = new Vector3(distance, 0, 0);
                orientation = targetYaw - Mathf.Pi / 2; // Face left toward target
                break;
            case TacticalPosition.Behind:
                // Position behind target
                offset = new Vector3(0, 0, distance);
                orientation = targetYaw; // Face same direction
                break;
            case TacticalPosition.Direct:
            default:
                // Head-on approach
                offset = new Vector3(0, 0, -distance);
                orientation = targetYaw + Mathf.Pi; // Face toward target
                break;
        }

        // Rotate offset by target's yaw
        float cos = Mathf.Cos(targetYaw);
        float sin = Mathf.Sin(targetYaw);
        Vector3 rotatedOffset = new Vector3(
            offset.X * cos - offset.Z * sin,
            0,
            offset.X * sin + offset.Z * cos
        );

        TargetPosition = targetPos + rotatedOffset;
        TargetOrientation = NormalizeAngle(orientation);
        DesiredSpeed = 0; // Will stop at attack position
    }

    /// <summary>
    /// Normalize angle to -PI to PI range
    /// </summary>
    private float NormalizeAngle(float angle)
    {
        while (angle > Mathf.Pi)
            angle -= Mathf.Pi * 2;
        while (angle < -Mathf.Pi)
            angle += Mathf.Pi * 2;
        return angle;
    }

    public string GetDebugInfo()
    {
        return $"MX:{MovementX:F2} MY:{MovementY:F2} "
               + $"Dist:{DebugDistanceToTarget:F1} AngErr:{DebugAngleToTarget:F0} "
               + $"Spd:{DebugCurrentSpeed:F1} Arr:{HasArrived}";
    }
}
