using Godot;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Controllers;
using Planetsgodot.Scripts.Environment;

namespace Planetsgodot.Scripts.Combat;

public partial class Energy : RayCast3D
{
    private MeshInstance3D beamMesh;
    public HardpointSpec spec;
    private ShaderMaterial beamMaterial;
    public bool Firing;
    private float impulseModifier = 1f;

    public void Enable(bool firing)
    {
        Firing = firing;
        if (beamMesh != null)
            beamMesh.Visible = Firing;

        var exp = GetNodeOrNull<GpuParticles3D>("Explosion");
        if (exp != null)
            exp.Visible = Firing;
    }

    public override void _Ready()
    {
        GD.Print($"Energy _Ready: {Name} {GetPath()}");

        // Enable colliding with Areas (Shields)
        CollideWithAreas = true;
        CollideWithBodies = true;

        // Robustly get or create the beam mesh
        beamMesh = GetNodeOrNull<MeshInstance3D>("LaserLine2");
        if (beamMesh == null)
        {
            beamMesh = new MeshInstance3D();
            beamMesh.Name = "LaserLine2";
            AddChild(beamMesh);
        }

        // Create a simple cylinder mesh for the beam
        var cylinder = new CylinderMesh();
        cylinder.TopRadius = 0.05f;
        cylinder.BottomRadius = 0.05f;
        cylinder.Height = 1.0f;
        cylinder.RadialSegments = 16;
        beamMesh.Mesh = cylinder;

        // Load the simplified electric shader
        var shader = GD.Load<Shader>("res://simple_beam.gdshader");
        beamMaterial = new ShaderMaterial();
        beamMaterial.Shader = shader;

        // Configure shader parameters
        beamMaterial.SetShaderParameter("albedo", new Color(0.2f, 0.8f, 1.0f));
        beamMaterial.SetShaderParameter("emission", new Color(0.5f, 0.9f, 1.0f));
        beamMaterial.SetShaderParameter("speed", 5.0f);
        beamMaterial.SetShaderParameter("width", 2.3f);
        beamMaterial.SetShaderParameter("tiling", 1.0f);

        beamMesh.MaterialOverride = beamMaterial;

        beamMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; // Optimization
        beamMesh.Position = Vector3.Zero;
        beamMesh.RotationDegrees = new Vector3(-90, 0, 0);
        beamMesh.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Firing || beamMesh == null)
            return;

        // Raycast along +Z (local forward for this turret setup based on original code)
        float maxRange = spec.MaxRange / 25.0f; // Match FireSystem logic
        if (maxRange <= 0.1f)
            maxRange = 500.0f; // Fallback for invalid specs

        TargetPosition = new Vector3(0, 0, maxRange);
        ForceRaycastUpdate();

        float length;
        bool hit = IsColliding();

        if (hit)
        {
            // Use distance to hit point, not local Z (which fails for rotated turrets)
            Vector3 hitPointGlobal = GetCollisionPoint();
            length = GlobalPosition.DistanceTo(hitPointGlobal);
            if (length < 0.1f)
                length = 0.1f;

            // Explosion effect moved to after shield logic to ensure correct position

            // Apply damage
            var collider = GetCollider();
            RigidBody3D hitTarget = collider as RigidBody3D;

            bool showExplosion = true;

            // If we hit an Area (like a Shield), try to find the parent RigidBody
            if (hitTarget == null && collider is Area3D area)
            {
                hitTarget = area.GetParent() as RigidBody3D;

                // Calculate precise intersection with Shield Sphere
                var shapeNode = area.GetChild(0) as CollisionShape3D;
                Vector3 rayDir = (ToGlobal(TargetPosition) - GlobalPosition).Normalized();

                if (shapeNode != null && shapeNode.Shape is SphereShape3D sphere)
                {
                    float r = sphere.Radius;
                    Vector3 sphereCenter = area.GlobalPosition;
                    Vector3 rayOrigin = GlobalPosition;

                    Vector3 l = rayOrigin - sphereCenter;
                    float a = 1.0f;
                    float b = 2.0f * l.Dot(rayDir);
                    float c = l.Dot(l) - (r * r);

                    float discriminant = b * b - 4 * a * c;
                    if (discriminant >= 0)
                    {
                        float t = (-b - Mathf.Sqrt(discriminant)) / (2.0f * a);
                        if (t > 0)
                        {
                            hitPointGlobal = rayOrigin + rayDir * t;
                            length = t;
                        }
                    }
                }

                // Check if shield quadrant is active using unified validation
                Shield shield = hitTarget?.FindChild("Shield", true, false) as Shield;
                if (shield == null && hitTarget != null)
                {
                    foreach (Node child in hitTarget.GetChildren())
                    {
                        if (child is Shield s)
                        {
                            shield = s;
                            break;
                        }
                        foreach (Node grandchild in child.GetChildren())
                        {
                            if (grandchild is Shield gs)
                            {
                                shield = gs;
                                break;
                            }
                        }
                        if (shield != null)
                            break;
                    }
                }
                // GD.Print($"Energy Raycast Hit: Target={hitTarget?.Name} ShieldNode={shield?.Name} Path=Visual/Shield");

                if (shield != null && !shield.IsQuadrantActive(hitPointGlobal))
                {
                    // GD.Print("Energy: Shield quadrant depleted, passing through");
                    // Quadrant depleted - do secondary raycast to find hull
                    bool wasCollidingWithAreas = CollideWithAreas;
                    CollideWithAreas = false;
                    ForceRaycastUpdate();

                    if (IsColliding())
                    {
                        hitTarget = GetCollider() as RigidBody3D;
                        hitPointGlobal = GetCollisionPoint();
                        length = GlobalPosition.DistanceTo(hitPointGlobal);
                    }
                    else
                    {
                        hitTarget = null;
                        length = maxRange;
                        showExplosion = false; // Missed everything
                    }

                    CollideWithAreas = wasCollidingWithAreas;
                }
            }

            // Move explosion effect here, AFTER hitPointGlobal potentially updates
            var explosionParticles = GetNodeOrNull<GpuParticles3D>("Explosion");
            if (explosionParticles != null)
            {
                if (showExplosion)
                {
                    explosionParticles.Emitting = true;
                    explosionParticles.Visible = true;
                    // Use global position for explosion to work with rotated turrets
                    explosionParticles.GlobalPosition = hitPointGlobal;
                    // Reset rotation so particles emit consistently
                    explosionParticles.GlobalRotation = Vector3.Zero;
                }
                else
                {
                     explosionParticles.Emitting = false;
                     explosionParticles.Visible = false;
                }
            }

            if (hitTarget != null)
            {
                hitTarget.ApplyForce(
                    -(GlobalPosition - GetCollisionPoint()).Normalized()
                        * (float)delta
                        / hitTarget.Mass
                        * spec.Damage
                        * impulseModifier
                );

                // Check for IDamageable on the RB or the Collider
                if (hitTarget is IDamageable damageable)
                {
                    damageable.Damage(spec, hitPointGlobal, delta);
                }
            }
        }
        else
        {
            length = maxRange;

            var exp = GetNodeOrNull<GpuParticles3D>("Explosion");
            if (exp != null)
            {
                exp.Emitting = false;
                exp.Visible = false;
            }
        }

        // Position beam at midpoint, scale to length
        // Reverting rotation to X -90 (Y maps to Z)
        beamMesh.RotationDegrees = new Vector3(-90, 0, 0);
        beamMesh.Position = new Vector3(0, 0, length / 2.0f);
        beamMesh.Scale = new Vector3(1, length, 1);

        // Debug prints
        // GD.Print(
        //    $"Beam: {Name} Hit:{hit} Length:{length} Pos:{beamMesh.Position} Scale:{beamMesh.Scale}"
        // );

        // Update shader tiling
        beamMaterial.SetShaderParameter("tiling", length * 2.0f);

        beamMesh.Visible = true;
    }
}
