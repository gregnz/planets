using Godot;
using System;

public partial class MuzzleFlash : Node3D
{
    public override void _Ready()
    {
        // Fix Orientation (User reported 180 deg wrong after my last 90 deg fix)
        // Previous: 90. Result: Backwards.
        // Therefore: -90 should be Forwards.
        RotationDegrees = new Vector3(0, -90, 0);

        // 1. Flash Light
        var light = new OmniLight3D();
        light.LightColor = new Color(1f, 0.9f, 0.5f);
        light.LightEnergy = 0.8f; // Reduced from 1.5
        light.OmniRange = 2.0f; // Reduced from 5.0 (was 15.0)
        AddChild(light);
        
        // 2. Particles
        var particles = new GpuParticles3D();
        particles.OneShot = true;
        particles.Explosiveness = 1.0f;
        particles.Amount = 8;
        particles.Lifetime = 0.1f; // Faster flash
        particles.Emitting = true;
        
        var mat = new ParticleProcessMaterial();
        mat.Direction = Vector3.Forward;
        mat.Spread = 25.0f;
        mat.InitialVelocityMin = 1.0f;
        mat.InitialVelocityMax = 5.0f;
        mat.Gravity = Vector3.Zero;
        mat.ScaleMin = 0.01f; // Tiny
        mat.ScaleMax = 0.1f;
        mat.Color = new Color(1f, 0.8f, 0.2f);
        
        var drawPass = new QuadMesh();
        drawPass.Size = new Vector2(0.05f, 0.05f); // Tiny quads (5cm)
        var drawMat = new StandardMaterial3D();
        drawMat.VertexColorUseAsAlbedo = true;
        drawMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        drawMat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        drawMat.AlbedoColor = new Color(1, 1, 1, 1);
        drawMat.EmissionEnabled = true;
        drawMat.Emission = new Color(1f, 0.6f, 0.1f) * 2f;
        drawPass.Material = drawMat;

        particles.ProcessMaterial = mat;
        particles.DrawPass1 = drawPass;
        
        AddChild(particles);

        // Auto-destroy after flash
        var timer = GetTree().CreateTimer(0.2f);
        timer.Connect("timeout", Callable.From(QueueFree));
    }
}
