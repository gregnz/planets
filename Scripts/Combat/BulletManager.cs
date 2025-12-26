using Godot;
using System;
using System.Collections.Generic;
using Planetsgodot.Scripts.Core;

namespace Planetsgodot.Scripts.Combat
{
    // A high-performance manager for massive amounts of simple ballistic projectiles.
    // Uses PhysicsServer3D for movement/collision and MultiMeshInstance3D for rendering.
    public partial class BulletManager : Node3D
    {
        private static BulletManager _instance;

        public static BulletManager Instance
        {
            get
            {
                // Note: This relies on EnsureInstance being called or _instance being preserved.
                // If accessed before EnsureInstance, it returns null.
                return _instance;
            }
        }

        public static void EnsureInstance(Node context)
        {
            if (_instance != null && GodotObject.IsInstanceValid(_instance)) return;

            _instance = new BulletManager();
            _instance.Name = "BulletManager";
            // Add to CurrentScene to ensure it is part of the active level world
            if (context != null && context.IsInsideTree())
            {
                context.GetTree().CurrentScene.AddChild(_instance);
            }
            else
            {
                // Fallback if context is weird, though FireSystem should be in tree
                GD.PrintErr("BulletManager: Context not in tree!");
            }

            _instance.GlobalPosition = Vector3.Zero;
            GD.Print("BulletManager Created and attached to CurrentScene.");
        }

        // Limits
        private const int MAX_BULLETS = 10000;

        // Data Arrays
        private struct BulletData
        {
            public bool Active;
            public Vector3 Position;
            public Vector3 Velocity;
            public float TimeToLive;
            public float Damage;
            public Rid OwnerRid; // To ignore self
            public Color Color;
        }

        private BulletData[] _bullets;
        private int _activeCount = 0;

        // Rendering
        private MultiMeshInstance3D _multiMeshInstance;
        private MultiMesh _multiMesh;

        // Physics
        private PhysicsDirectSpaceState3D _spaceState;

        public override void _Ready()
        {
            _bullets = new BulletData[MAX_BULLETS];
            SetupVisuals();

            _bullets = new BulletData[MAX_BULLETS];
            SetupVisuals();
        }

        private void SetupVisuals()
        {
            _multiMeshInstance = new MultiMeshInstance3D();
            _multiMeshInstance.Name = "BulletMultiMesh";
            AddChild(_multiMeshInstance);

            _multiMesh = new MultiMesh();
            _multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            _multiMesh.UseColors = true; // Allow per-bullet color
            _multiMesh.InstanceCount = MAX_BULLETS;
            _multiMesh.VisibleInstanceCount = 0;

            // Simple Box/Capsule mesh for the tracer
            var mesh = new BoxMesh();
            mesh.Size = new Vector3(0.15f, 0.15f, 0.20f); // Reduced scale (was 0.3/6.0)

            var mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = true;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded; // Always visible, glowing
            mat.AlbedoColor = Colors.White;

            mesh.Material = mat;

            _multiMesh.Mesh = mesh;
            _multiMeshInstance.Multimesh = _multiMesh;

            // Critical for visibility: Set bounds large enough so it doesn't get culled
            _multiMeshInstance.CustomAabb =
                new Aabb(new Vector3(-50000, -50000, -50000), new Vector3(100000, 100000, 100000));
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_activeCount == 0) return;

            // Get space state for raycasting
            _spaceState = PhysicsServer3D.SpaceGetDirectState(GetWorld3D().Space);

            float dt = (float)delta;
            int processed = 0;

            // Iterate through buffer
            for (int i = 0; i < MAX_BULLETS; i++)
            {
                if (!_bullets[i].Active) continue;

                UpdateBullet(i, dt);
                processed++;
                if (processed >= _activeCount) break;
            }

            // Update Visuals
            UpdateMultiMesh();
        }

        private void UpdateBullet(int i, float delta)
        {
            ref var b = ref _bullets[i];

            // 1. Move
            b.TimeToLive -= delta;
            if (b.TimeToLive <= 0)
            {
                DeactivateBullet(i);
                return;
            }

            Vector3 start = b.Position;
            Vector3 step = b.Velocity * delta;
            Vector3 end = start + step;

            // 2. Raycast
            var query = PhysicsRayQueryParameters3D.Create(start, end);
            query.CollisionMask = 0xFF; // Layers 1-8 (Broaden to catch everything solid)
            query.CollideWithAreas = true; // ESSENTIAL for hitting Shields (Area3D)
            if (b.OwnerRid.IsValid)
            {
                var input = new Godot.Collections.Array<Rid>();
                input.Add(b.OwnerRid);
                query.Exclude = input;
            }

            var result = _spaceState.IntersectRay(query);

            if (result.Count > 0)
            {
                // Hit!
                HandleHit(i, result);
                DeactivateBullet(i);
            }
            else
            {
                // No Hit, Move forward
                b.Position = end;
            }
        }

        private void HandleHit(int index, Godot.Collections.Dictionary result)
        {
            // Apply Damage
            var collider = result["collider"].As<Node>();
            if (collider is IDamageable dmg)
            {
                var pos = result["position"].AsVector3();
                dmg.Damage(_bullets[index].Damage, -_bullets[index].Velocity.Normalized(), pos);
            }

            // Always Spawn Visual Impact
            // GD.Print($"Impact at {result["position"]}");
            var impactPos = result["position"].AsVector3();
            // TODO: Object Pooling
            var impact = new VisualImpact();
            impact.Configure(_bullets[index].Color, 0.5f);
            if (GetTree().CurrentScene != null)
            {
                GetTree().CurrentScene.AddChild(impact);
                impact.GlobalPosition = impactPos;
                impact.Fire();
            }
        }

        private void DeactivateBullet(int index)
        {
            _bullets[index].Active = false;
            _activeCount--;
        }

        private void UpdateMultiMesh()
        {
            int visibleIndex = 0;
            for (int i = 0; i < MAX_BULLETS; i++)
            {
                if (!_bullets[i].Active) continue;

                ref var b = ref _bullets[i];

                // Set Transform
                Transform3D t = Transform3D.Identity;
                t.Origin = b.Position;

                // Align Z with Velocity
                if (b.Velocity != Vector3.Zero)
                {
                    // LookAt aligns -Z to target.
                    t = t.LookingAt(b.Position + b.Velocity, Vector3.Up);
                }

                _multiMesh.SetInstanceTransform(visibleIndex, t);
                _multiMesh.SetInstanceColor(visibleIndex, b.Color);

                visibleIndex++;
            }

            _multiMesh.VisibleInstanceCount = visibleIndex;
        }

        // Public API
        public static void Spawn(Node context, Vector3 position, Vector3 velocity, float damage, float range,
            Color color, RigidBody3D owner)
        {
            EnsureInstance(context);
            if (_instance != null)
            {
                _instance.SpawnBulletInternal(position, velocity, damage, range, color, owner);
            }
        }

        private void SpawnBulletInternal(Vector3 position, Vector3 velocity, float damage, float range, Color color,
            RigidBody3D owner)
        {
            if (_activeCount >= MAX_BULLETS) return; // Pool full

            // Find first free slot
            int slot = -1;
            for (int i = 0; i < MAX_BULLETS; i++)
            {
                if (!_bullets[i].Active)
                {
                    slot = i;
                    break;
                }
            }

            if (slot == -1) return;

            // Initialize
            _bullets[slot].Active = true;
            _bullets[slot].Position = position;
            _bullets[slot].Velocity = velocity;
            _bullets[slot].Damage = damage;
            _bullets[slot].Color = color;
            _bullets[slot].OwnerRid = owner.GetRid();

            float speed = velocity.Length();
            _bullets[slot].TimeToLive = (speed > 0) ? range / speed : 0;

            _activeCount++;
        }
    }
}
