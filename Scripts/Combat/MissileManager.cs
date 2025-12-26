using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Planetsgodot.Scripts.Core;

namespace Planetsgodot.Scripts.Combat
{
    public partial class MissileManager : Node3D
    {
        public static MissileManager Instance { get; private set; }

        private struct MissileData
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 Velocity;
            public Quaternion Rotation;
            public Node3D Target;

            // Flight Parameters
            public float MaxSpeed;
            public float Acceleration;
            public float TurnRateRadians; // Rad/s
            public float LifeTime;

            // Combat
            public float Damage;
            public Node Launcher; // Friendly fire check
            public float Scale;
            public bool EngineIgnited;
            public float TimeSinceLaunch;
        }

        private const int MAX_MISSILES = 4000;
        private MissileData[] _missiles;
        private MultiMeshInstance3D _multiMeshInstance;
        private MultiMesh _multiMesh;
        private int _activeCount = 0;

        // Visuals
        private PackedScene _explosionPrefab;
        private Mesh _missileMesh;

        public override void _Ready()
        {
            Instance = this;
            _missiles = new MissileData[MAX_MISSILES];

            _explosionPrefab = GD.Load<PackedScene>("res://explosion.tscn");

            SetupMultiMesh();
        }

        private void SetupMultiMesh()
        {
            _multiMeshInstance = new MultiMeshInstance3D();
            _multiMesh = new MultiMesh();

            // Try to grab the mesh from the new missile.glb
            try
            {
                var spec = ShipFactory.GetPreset(ShipFactory.ShipType.Missile);
                string path = spec.PrefabPath ?? "res://missile.glb";

                // GLB files are loaded as PackedScenes in Godot
                var scene = GD.Load<PackedScene>(path);
                if (scene != null)
                {
                    var instance = scene.Instantiate();

                    // Recursive find for MeshInstance3D
                    _missileMesh = FindMeshInNode(instance);

                    if (_missileMesh == null)
                    {
                        GD.PrintErr($"MissileManager: Could not find MeshInstance3D in {path}. Using Cylinder.");
                        _missileMesh = new CylinderMesh() { TopRadius = 0.1f, BottomRadius = 0.2f, Height = 1.0f };
                    }
                    else
                    {
                        // Optional: Apply a default material if the GLB didn't bring one compatible with MultiMesh, 
                        // but usually it's fine.
                        GD.Print($"MissileManager: Successfully loaded mesh from {path}");
                    }

                    instance.QueueFree();
                }
                else
                {
                    throw new Exception($"Failed to load {path}");
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"MissileManager: Error loading missile mesh: {e.Message}. Fallback to Cylinder.");
                _missileMesh = new CylinderMesh();
            }

            _multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            _multiMesh.InstanceCount = MAX_MISSILES;
            _multiMesh.VisibleInstanceCount = 0;
            _multiMesh.Mesh = _missileMesh;

            _multiMeshInstance.Multimesh = _multiMesh;
            _multiMeshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; // Optimization
            AddChild(_multiMeshInstance);
        }

        private Mesh FindMeshInNode(Node node)
        {
            if (node is MeshInstance3D mi && mi.Mesh != null)
            {
                return mi.Mesh;
            }

            foreach (Node child in node.GetChildren())
            {
                var mesh = FindMeshInNode(child);
                if (mesh != null) return mesh;
            }

            return null;
        }

        // Launch angle spread pattern
        private readonly float[] _spreadAngles = { 0, -10, 10, -20, 20, -30, 30, -40, 40, -50, 50 };

        public void SpawnMissile(Node launcher, Node3D target, Vector3 startPos, Quaternion startRot,
            Vector3 initialVelocity, int volleyIndex)
        {
            // Find free slot
            int index = -1;
            for (int i = 0; i < MAX_MISSILES; i++)
            {
                if (!_missiles[i].Active)
                {
                    index = i;
                    break;
                }
            }

            if (index == -1)
            {
                GD.PrintErr("MissileManager: Max missiles reached!");
                return;
            }

            // Load Spec
            var spec = ShipFactory.GetPreset(ShipFactory.ShipType.Missile);

            // Calculate Spread
            float spreadAngleDeg = _spreadAngles[volleyIndex % _spreadAngles.Length];
            Quaternion spreadRot = Quaternion.FromEuler(new Vector3(0, Mathf.DegToRad(spreadAngleDeg), 0));
            Quaternion finalRot = startRot * spreadRot;

            // Calculate Initial Velocity (Impulse outwards, not forward)
            // Original code: ignitionAngle * 2f + Position ... thrust * Mass ...
            // We'll emulate the "Kick" side/outwards
            Vector3 forwardDir = -new Basis(finalRot).Z;

            // Initial velocity: Inherit ship velocity + small kick in spread direction
            Vector3 kickVelocity = forwardDir * (spec.acceleration * spec.boostCoeff * 0.2f);

            // Defaults (could be passed in via spec)
            _missiles[index] = new MissileData
            {
                Active = true,
                Position = startPos + (forwardDir * 2.0f), // Offset spawn slightly
                Rotation = finalRot,
                Velocity = initialVelocity + kickVelocity,
                Target = target,
                Launcher = launcher,

                MaxSpeed = spec.maxSpeed,
                Acceleration = spec.acceleration,
                TurnRateRadians = spec.rotateSpeed,
                LifeTime = 10.0f,
                Damage = 50.0f,
                Scale = 0.25f,
                EngineIgnited = false,
                TimeSinceLaunch = 0f
            };

            _activeCount++;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_activeCount == 0)
            {
                _multiMesh.VisibleInstanceCount = 0;
                return;
            }

            float dt = (float)delta;
            int visibleCount = 0;

            // Prepare Raycast
            var spaceState = PhysicsServer3D.SpaceGetDirectState(GetWorld3D().Space);

            for (int i = 0; i < MAX_MISSILES; i++)
            {
                if (!_missiles[i].Active) continue;

                // Update Logic
                UpdateMissile(i, dt, spaceState);

                // Update Visuals if still active
                if (_missiles[i].Active)
                {
                    // Visual correction: Rotate 90 degrees clockwise (Y-axis -90)
                    Quaternion visualRot =
                        _missiles[i].Rotation * Quaternion.FromEuler(new Vector3(0, -Mathf.Pi / 2, 0));

                    Transform3D t = new Transform3D(new Basis(visualRot), _missiles[i].Position);
                    // Apply Scale
                    t = t.ScaledLocal(Vector3.One * _missiles[i].Scale);
                    _multiMesh.SetInstanceTransform(visibleCount, t);
                    visibleCount++;
                }
            }

            _multiMesh.VisibleInstanceCount = visibleCount;
        }

        private void UpdateMissile(int i, float dt, PhysicsDirectSpaceState3D spaceState)
        {
            // 1. Check Lifetime
            _missiles[i].LifeTime -= dt;
            _missiles[i].TimeSinceLaunch += dt;

            if (_missiles[i].LifeTime <= 0)
            {
                DeactivateMissile(i);
                return;
            }

            // Ignition Logic (e.g. 0.5s delay)
            if (!_missiles[i].EngineIgnited && _missiles[i].TimeSinceLaunch > 0.4f)
            {
                _missiles[i].EngineIgnited = true;
                // TODO: Spawn Trail Particle here? Or just enable it in the MultiMesh shader?
                // For now, simple logic change
            }

            Vector3 currentPos = _missiles[i].Position;
            Vector3 forward = -new Basis(_missiles[i].Rotation).Z; // Godot Forward is -Z

            // 2. Guidance (Homing) - ONLY if Engine Ignited
            if (_missiles[i].EngineIgnited && _missiles[i].Target != null &&
                GodotObject.IsInstanceValid(_missiles[i].Target))
            {
                // Predict intercept roughly? Or just pure pursuit
                Vector3 targetPos = _missiles[i].Target.GlobalPosition;
                Vector3 toTarget = (targetPos - currentPos).Normalized();

                // Rotate forward towards toTarget limited by TurnRate
                // Simple Slerp-like approach for vectors
                // Or better: Rotate quaternion

                // Calculate desired rotation
                // Handle case where we are already there or vectors are problematic
                if ((targetPos - currentPos).LengthSquared() > 1.0f)
                {
                    // Current forward is 'forward'
                    // We want to rotate 'forward' to 'toTarget'
                    // Axis of rotation is Cross product
                    Vector3 axis = forward.Cross(toTarget);
                    if (axis.LengthSquared() > 0.0001f)
                    {
                        axis = axis.Normalized();
                        float angle = Mathf.Acos(Mathf.Clamp(forward.Dot(toTarget), -1f, 1f));
                        float maxStep = _missiles[i].TurnRateRadians * dt;

                        if (angle > maxStep)
                        {
                            // Rotate by maxStep around axis
                            Quaternion rot = Quaternion.FromEuler(axis * maxStep);
                            _missiles[i].Rotation = rot * _missiles[i].Rotation;
                        }
                        else
                        {
                            // Just snap to look at? No, careful of Up vector.
                            // Properly:
                            // Basis lookAt = Basis.LookingAt(toTarget, Vector3.Up);
                            // _missiles[i].Rotation = lookAt.GetRotationQuaternion();

                            // Incremental Rotation is smoother/safer
                            Quaternion rot = Quaternion.FromEuler(axis * angle);
                            _missiles[i].Rotation = rot * _missiles[i].Rotation;
                        }
                    }
                }
            }

            // Re-calc forward after rotation
            forward = -new Basis(_missiles[i].Rotation).Z;

            // 3. Acceleration & Velocity
            // Only accelerate if engine is ignited
            if (_missiles[i].EngineIgnited)
            {
                float speed = _missiles[i].Velocity.Length();
                if (speed < _missiles[i].MaxSpeed)
                {
                    speed += _missiles[i].Acceleration * dt;
                }

                _missiles[i].Velocity = forward * speed;
            }
            // else: Coast with initial kick velocity (drag could apply here if wanted)

            // 4. Move
            Vector3 nextPos = currentPos + _missiles[i].Velocity * dt;

            // 5. Collision (Raycast from current to next)
            var query = PhysicsRayQueryParameters3D.Create(currentPos, nextPos);
            query.CollideWithAreas = true;
            query.CollideWithBodies = true;
            query.CollisionMask = 63; // Layers 1-6 (Includes Asteroids=32)

            var result = spaceState.IntersectRay(query);

            if (result.Count > 0)
            {
                Node collider = result["collider"].As<Node>();

                // Friendly Fire Check (simple)
                // If launcher is the collider, ignore inside initial launch window?
                if (collider == _missiles[i].Launcher && _missiles[i].LifeTime > 9.0f)
                {
                    // Ignore self-hit on launch
                }
                else
                {
                    HandleImpact(collider, result["position"].AsVector3(), forward, i);
                    return; // Stop update
                }
            }

            _missiles[i].Position = nextPos;

            // 6. Constrain to 2D Plane (optional, based on game rules)
            if (Mathf.Abs(_missiles[i].Position.Y) > 0.1f)
            {
                _missiles[i].Position.Y = 0;
            }
        }

        private void HandleImpact(Node hitObj, Vector3 hitPos, Vector3 direction, int index)
        {
            // 1. Visuals
            SpawnExplosion(hitPos);

            // 2. Damage
            // Look for IDamageable
            Node n = hitObj;
            IDamageable damageable = n as IDamageable;

            // Traverse up if not found (hit collider child)
            if (damageable == null)
            {
                n = hitObj.GetParent();
                damageable = n as IDamageable;
            }

            if (damageable != null)
            {
                damageable.Damage(_missiles[index].Damage, direction, hitPos);
            }
            else if (hitObj.Name == "ShieldArea") // Specific logic from Missile.cs
            {
                // Typically ShieldArea is child of Body.
                // Need to find parent IDamageable
                Node parent = hitObj.GetParent();
                if (parent is IDamageable d) d.Damage(_missiles[index].Damage, direction, hitPos);
            }

            DeactivateMissile(index);
        }

        public void SpawnExplosion(Vector3 pos)
        {
            if (_explosionPrefab != null)
            {
                var exp = _explosionPrefab.Instantiate() as GpuParticles3D;
                if (exp != null)
                {
                    GetTree().CurrentScene.AddChild(exp);
                    exp.GlobalPosition = pos;
                    exp.Emitting = true;
                    // Auto cleanup script on explosion usually handles queue free
                    // If not, add a timer
                    exp.CreateTween().TweenCallback(Callable.From(() => exp.QueueFree())).SetDelay(2.0f);
                }
            }
        }

        private void DeactivateMissile(int index)
        {
            _missiles[index].Active = false;
            _activeCount--;
        }
    }
}
