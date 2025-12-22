using Godot;
using System;
using System.Collections.Generic;
using Planetsgodot.Scripts.Controllers;

namespace Planetsgodot.Scripts.AI
{
    public class NavigationSystem
    {
        public float SafetyRadius { get; set; } = 15.0f; // Radius to keep clear of other ships
        public float LookaheadTime { get; set; } = 3.0f; // Seconds to predict ahead

        /// <summary>
        /// Calculates a path from current position to target, avoiding static and dynamic obstacles.
        /// </summary>
        public Queue<Vector3> GetPath(RigidBody3D me, Vector3 targetPos, List<Node> ignoredBodies = null)
        {
            var path = new Queue<Vector3>();
            
            // 1. Static Obstacle Check (SphereCast)
            // TODO: Implement SphereCast for asteroids/stations if needed. 
            // For now, space is mostly empty, so we prioritize Dynamic Avoidance.

            // 2. Dynamic Intersection Check (Future Collision Prediction)
            
            Vector3 myPos = me.GlobalPosition;
            Vector3 myVel = me.LinearVelocity;
            
            // Find neighbors
            // Ideally passed in or cached, but getting group is okay for now
            var neighbors = me.GetTree().GetNodesInGroup("NPC"); 
            var players = me.GetTree().GetNodesInGroup("Player");
            
            float closestTime = float.MaxValue;
            RigidBody3D primaryThreat = null;
            Vector3 futureCollisionPoint = Vector3.Zero;
            Vector3 threatFuturePos = Vector3.Zero;

            // Helper for checking threat
            void CheckThreat(Node node)
            {
                if (node == me || node == me.GetParent()) return;
                if (ignoredBodies != null && ignoredBodies.Contains(node)) return;

                
                RigidBody3D otherRb = node as RigidBody3D;
                // If node is not RigidBody directly, maybe it's the controller/parent?
                // Npc script is on the RigidBody usually.
                if (otherRb == null && node is Node3D n3d)
                {
                    // Try to find RB child or see if it IS the RB
                    // In this project Npc.cs extends RigidBody3D usually? 
                    // Let's check Npc.cs: "public partial class Npc : RigidBody3D" -> Yes.
                }

                if (otherRb == null) return;

                Vector3 otherPos = otherRb.GlobalPosition;
                Vector3 otherVel = otherRb.LinearVelocity;

                // Relative physics
                Vector3 relPos = myPos - otherPos; // P
                Vector3 relVel = myVel - otherVel; // V
                
                float relVelSq = relVel.LengthSquared();
                if (relVelSq < 0.1f) return; // Moving together, no collision

                // CPA Time: t = -Dot(P, V) / Dot(V, V)
                // Note: P is (Me - Them). 
                // We want time when Dist is min.
                float t = -relPos.Dot(relVel) / relVelSq;

                if (t > 0 && t < LookaheadTime)
                {
                    // Check distance at time t
                    Vector3 myFuture = myPos + myVel * t;
                    Vector3 otherFuture = otherPos + otherVel * t;
                    float distAtCpa = myFuture.DistanceTo(otherFuture);

                     // Check current distance too (in case we are already too close)
                    float currentDist = myPos.DistanceTo(otherPos);

                    if (distAtCpa < SafetyRadius || (currentDist < SafetyRadius && t < 1.0f))
                    {
                        // Collision Predicted
                        if (t < closestTime)
                        {
                            closestTime = t;
                            primaryThreat = otherRb;
                            futureCollisionPoint = myFuture;
                            threatFuturePos = otherFuture;
                        }
                    }
                }
            }

            foreach (var n in neighbors) CheckThreat(n);
            foreach (var p in players) CheckThreat(p);

            if (primaryThreat != null)
            {
                // 3. Generate Detour
                // We want to pass "Around" the threat.
                // Vector from ThreatFuture to UsFuture
                Vector3 rejectionDir = (futureCollisionPoint - threatFuturePos).Normalized();
                
                if (rejectionDir.LengthSquared() < 0.01f)
                {
                    // Head on collision perfectly? Pick Up or Right.
                    rejectionDir = me.GlobalTransform.Basis.X;
                }

                // Detour point: ThreatPos + (Direction * (Radius + Buffer))
                // We add buffer to SafetyRadius
                Vector3 detourPoint = threatFuturePos + rejectionDir * (SafetyRadius * 1.5f);
                
                // Add Detour then Target
                path.Enqueue(detourPoint);
                path.Enqueue(targetPos);
            }
            else
            {
                // Clear path
                path.Enqueue(targetPos);
            }

            return path;
        }
    }
}
