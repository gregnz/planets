using Godot;
using System;

namespace Planetsgodot.Scripts.Combat
{
    public partial class VisualImpact : Node3D
    {
        private Color _color = new Color(1, 0.5f, 0); // Orange default
        private float _scale = 1.0f;

        public void Configure(Color color, float scale)
        {
            _color = color;
            _scale = scale;
        }

        public override void _Ready()
        {
            // 1. Flash Light
            var light = new OmniLight3D();
            light.LightColor = _color;
            light.LightEnergy = 5.0f * _scale;
            light.OmniRange = 10.0f * _scale;
            AddChild(light);

            // Fade out light tween
            var tween = CreateTween();
            tween.TweenProperty(light, "light_energy", 0.0f, 0.2f);
            tween.TweenCallback(Callable.From(light.QueueFree));

            // 2. Sparks / Debris
            var particles = new CpuParticles3D();
            particles.Emitting = false;
            particles.OneShot = true;
            particles.Amount = 20;
            particles.Lifetime = 0.5f;
            particles.Explosiveness = 1.0f;
            particles.Direction = Vector3.Back; // Relative to impact normal if rotated
            particles.Spread = 180.0f;
            particles.InitialVelocityMin = 5.0f * _scale;
            particles.InitialVelocityMax = 10.0f * _scale;
            particles.ScaleAmountMin = 0.1f * _scale;
            particles.ScaleAmountMax = 0.3f * _scale;

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = _color;
            mat.EmissionEnabled = true;
            mat.Emission = _color * 4.0f;
            mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;

            var quad = new QuadMesh();
            quad.Size = new Vector2(0.2f, 0.2f);
            quad.Material = mat;
            particles.Mesh = quad;

            AddChild(particles);
            // Don't emit immediately. Wait for Fire().
        }

        public void Fire()
        {
            foreach (var child in GetChildren())
            {
                if (child is CpuParticles3D p) p.Emitting = true;
                // Light fade is handled by tween in _Ready, which is fine as it moves with parent
            }

            // Auto cleanup
            GetTree().CreateTimer(1.0f).Connect("timeout", new Callable(this, "QueueFree"));
        }
    }
}
