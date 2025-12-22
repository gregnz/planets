using Godot;
using System;

public partial class VisualBullet : Node3D
{
    // Tuning
    private Color _coreColor = new Color(1f, 0.8f, 0.2f); // Golden/Yellow default
    private float _coreSize = 0.01f;
    private float _length = 4.0f; // Long tracer look

    public override void _Ready()
    {
        // 1. Glowing Core (The physical bullet look)
        // CreateCore();

        // 2. Trail (Particle ribbon)
        // CreateTrail();

        // 3. Light (Illumination)
        CreateLight();
    }

    private void CreateCore()
    {
        var meshInstance = new MeshInstance3D();
        var mesh = new CapsuleMesh();
        mesh.Radius = 0.5f * _coreSize;
        mesh.Height = _length;
        meshInstance.Mesh = mesh;

        // Rotate to align with Z-forward
        meshInstance.RotationDegrees = new Vector3(90, 0, 0);

        // Glowing Material
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = _coreColor;
        mat.EmissionEnabled = true;
        mat.Emission = _coreColor * 5.0f; // Bright glow
        mat.EmissionEnergyMultiplier = 2.0f;
        meshInstance.MaterialOverride = mat;

        CallDeferred("add_child", meshInstance);
    }

    private void CreateTrail()
    {
        // Switched to CpuParticles3D to avoid physics/collision interference reported by user.
        var particles = new CpuParticles3D();
        particles.Amount = 20;
        particles.Lifetime = 0.5f;
        particles.Explosiveness = 0.0f;
        particles.FixedFps = 60;
        
        particles.Direction = Vector3.Back;
        particles.Spread = 0;
        particles.Gravity = Vector3.Zero;
        particles.InitialVelocityMin = 0;
        particles.InitialVelocityMax = 0;
        particles.ScaleAmountMin = 0.05f;
        particles.ScaleAmountMax = 0.01f;
        particles.Color = new Color(_coreColor, 0.5f);
        
        var drawPass = new QuadMesh();
        drawPass.Size = new Vector2(0.1f, 0.1f);
        var drawMat = new StandardMaterial3D();
        drawMat.VertexColorUseAsAlbedo = true;
        drawMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        drawMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        drawPass.Material = drawMat;

        particles.Mesh = drawPass;
        particles.LocalCoords = false; // Trail should be in world space to leave a ribbon behind

        CallDeferred("add_child", particles);
    }

    private void CreateLight()
    {
        var light = new OmniLight3D();
        light.LightColor = _coreColor;
        light.LightEnergy = 1.0f;
        light.OmniRange = 10.0f;
        CallDeferred("add_child", light);
    }
}
