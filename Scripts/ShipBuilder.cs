using System.Collections;
using System.Collections.Generic;
using Godot;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Core;

public class ShipBuilder
{
    public PackedScene turretPrefab  = GD.Load<PackedScene>("res://turret_2.tscn");
    public PackedScene energyPrefab = GD.Load<PackedScene>("res://Energy.tscn");
    private PackedScene ballisticPrefab = GD.Load<PackedScene>("res://turret_2.tscn");
    public PackedScene missilePrefab =  GD.Load<PackedScene>("res://missile_launcher.tscn");
    public PackedScene pointDefencePrefab;

    public void Build(ShipFactory.ShipSpec shipSpecification, Node3D parent)
    {
        int i = 0;
        foreach (ShipFactory.ShipSpec.Hardpoint h in shipSpecification.hardpoints)
        {
            Node3D hardpointContent = null;

            if (h.HardpointSpec is HardpointSpec.Energy || h.name.Contains("Laser"))
            {
                hardpointContent = (Energy)energyPrefab.Instantiate();
                ((Energy)hardpointContent).spec = h.HardpointSpec;
            }
            else if (h.HardpointSpec is HardpointSpec.Ballistic)
            {
                hardpointContent = (Node3D)ballisticPrefab.Instantiate();
            }
            else if (h.HardpointSpec is HardpointSpec.Missile)
            {
                hardpointContent = (Node3D)missilePrefab.Instantiate();
            }
            else if (h.HardpointSpec is HardpointSpec.PointDefence)
            {
                // hardpointContent = Instantiate(pointDefencePrefab, transform);
            }


            if (hardpointContent == null) continue;
            hardpointContent.Name = $"{h.name}_{i}";
            parent.AddChild(hardpointContent);
            hardpointContent.Position = (new Vector3(h.x, h.y, h.z));
            hardpointContent.Scale = new Vector3(1f, 1f, 1f);
            // hardpointContentTransform.ScaledLocal(new Vector3(0.1f, 0.1f, 0.1f));
            h.hardpointContent = hardpointContent;
            i++;
        }
    }
}