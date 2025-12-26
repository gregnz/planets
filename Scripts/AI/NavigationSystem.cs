using System;
using System.Collections.Generic;
using Godot;

namespace Planetsgodot.Scripts.AI;

public class NavigationSystem
{
    private const int NumRays = 64;
    private const float LookAheadDistance = 50.0f; // Max distance to check for obstacles
    private const float AgentRadius = 5.0f; // Size of the ship for avoidance

    // Arrays for Context Maps
    private Vector3[] _rayDirections;
    private float[] _interestMap;
    private float[] _dangerMap;

    // Public access for Debugging
    public Vector3[] RayDirections => _rayDirections;
    public float[] InterestMap => _interestMap;
    public float[] DangerMap => _dangerMap;
    public Vector3 BestDirection { get; private set; }

    public NavigationSystem()
    {
        InitializeRays();
    }

    private void InitializeRays()
    {
        _rayDirections = new Vector3[NumRays];
        _interestMap = new float[NumRays];
        _dangerMap = new float[NumRays];

        // Generate rays in a circle on the XZ plane (Top-Down 2D)
        float angleStep = Mathf.Pi * 2 / NumRays;
        for (int i = 0; i < NumRays; i++)
        {
            float angle = i * angleStep;
            // Z is Forward in Godot? -Z is forward.
            // Let's use standard trig: X = sin, Z = cos. 
            // 0 angle = Forward (-Z)
            _rayDirections[i] = new Vector3(Mathf.Sin(angle), 0, -Mathf.Cos(angle));
        }
    }

    public Vector3 GetBestDirection(RigidBody3D agent, Vector3 targetPos, List<Node> ignored, float currentSpeed = 0f)
    {
        // 1. Clear Maps
        Array.Clear(_interestMap, 0, NumRays);
        Array.Clear(_dangerMap, 0, NumRays);

        Vector3 agentPos = agent.GlobalPosition;

        // 2. Build Interest Map (Where do we WANT to go?)
        Vector3 directionToTarget = (targetPos - agentPos).Normalized();
        for (int i = 0; i < NumRays; i++)
        {
            float dot = _rayDirections[i].Dot(directionToTarget);
            _interestMap[i] = Mathf.Max(0, dot);
        }


        // 3. Build Danger Map (Where CAN'T we go?)
        PhysicsDirectSpaceState3D spaceState = agent.GetWorld3D().DirectSpaceState;

        // Dynamic LookAhead
        float dynamicLookAhead = Math.Max(LookAheadDistance, currentSpeed * 2.0f);
        dynamicLookAhead = Math.Min(dynamicLookAhead, 300.0f);

        // Prepare query common params
        Godot.Collections.Array<Godot.Rid> excludeArray = new Godot.Collections.Array<Godot.Rid>();
        excludeArray.Add(agent.GetRid());
        if (ignored != null)
        {
            foreach (var node in ignored)
            {
                if (node is CollisionObject3D co) excludeArray.Add(co.GetRid());
            }
        }

        var query = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 0xFFFFFFFF, excludeArray);

        for (int i = 0; i < NumRays; i++)
        {
            Vector3 rayDir = _rayDirections[i];
            Vector3 to = agentPos + rayDir * dynamicLookAhead;

            query.From = agentPos;
            query.To = to;

            var result = spaceState.IntersectRay(query);
            if (result.Count > 0)
            {
                // Hit something!
                if (result["position"].Obj is Vector3 hitPos)
                {
                    float dist = agentPos.DistanceTo(hitPos);
                    float strength = 1.0f - (dist / dynamicLookAhead);
                    strength = strength * strength;

                    _dangerMap[i] = strength;
                    SmearDanger(i, strength, 2);
                }
            }
        }

        // 4. Choose Best Direction
        BestDirection = Vector3.Zero;
        float bestScore = -999f;
        int bestIndex = -1;

        for (int i = 0; i < NumRays; i++)
        {
            // Simple: Score = Interest - Danger
            // Multiplier on Danger to make it strict
            float score = _interestMap[i] - (_dangerMap[i] * 20.0f);

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (bestIndex != -1)
        {
            BestDirection = _rayDirections[bestIndex];
        }
        else
        {
            // Should be impossible but fallback to target
            BestDirection = directionToTarget;
        }

        return BestDirection;
    }

    private void SmearDanger(int centerIndex, float strength, int radius)
    {
        // Simple linear falloff for neighbors
        for (int offset = 1; offset <= radius; offset++)
        {
            float falloff = 1.0f - ((float)offset / (radius + 1));
            float neighborStrength = strength * falloff;

            int left = (centerIndex - offset + NumRays) % NumRays;
            int right = (centerIndex + offset) % NumRays;

            _dangerMap[left] = Mathf.Max(_dangerMap[left], neighborStrength);
            _dangerMap[right] = Mathf.Max(_dangerMap[right], neighborStrength);
        }
    }
}
