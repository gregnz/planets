using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Godot.Collections;
using Newtonsoft.Json;
using utils;

public class ShipFactory
{
    public string name;

    static int secondsForSpeed = 30; // eg: Adder@244 = 8m/s. GD ✅ Confirmed this works godot. Will travel 8 units / second.

    /* Acceleration: How much force to add per second. massMetres/second/second.
     *
     * Eg: Adder is 39.41. Max speed is 8m/s.
     * What does '39.41' mean? 39.41 * acceleration coefficient = add "Mass" force per second. So a 1/39 coefficient will  add 1 unit/s2
     * to the velocity of the ship, based on a 100kg weight. So every second, the ship will be 1 unit/second faster.
     *
     * Note, this doesn't include drag.
     */
    static float accelerationCoefficient = 1 / 39.41f;
    private static float radPerSecondCoeff = Mathf.Pi / 8; // Adder@4 = Mathf.PI/2 (ie, 90 degrees per second). 2PI Radians = 360.

    // Type-7 Transporter = 1* Math.PI/8 = 45 degrees per second.

    public static System.Collections.Generic.Dictionary<string, HardpointSpec> weapons =
        HardpointSpec.GetWeapons();

    public enum ShipType
    {
        SidewinderMkI,
        EagleMkII,
        Hauler,
        Adder,
        ImperialEagle,
        ViperMkIII,
        CobraMkIII,
        ViperMkIV,
        DiamondbackScout,
        CobraMkIV,
        Type6Transporter,
        Dolphin,
        DiamondbackExplorer,
        ImperialCourier,
        Keelback,
        AspScout,
        Vulture,
        AspExplorer,
        FederalDropship,
        Type7Transporter,
        AllianceChieftain,
        FederalAssaultShip,
        ImperialClipper,
        AllianceChallenger,
        AllianceCrusader,
        FederalGunship,
        KraitMkII,
        Orca,
        FerDeLance,
        Python,
        Type9Heavy,
        BelugaLiner,
        Type10Defender,
        Anaconda,
        FederalCorvette,
        ImperialCutter,
        Missile,
        TigersClaw, // Capital Ship
    }

    public enum Location
    {
        One,
        Two,
        Three,
        Four,
        Five,
        Six,
        Seven,
        Eight,
        Nine,
    }

    public class ShipSpec
    {
        public string name { get; set; }
        public int size { get; set; }
        public float maxSpeed { get; set; }

        public float acceleration { get; set; }

        public int manoeuvrability { get; set; }

        public float rotateSpeed
        {
            get => (manoeuvrability + 1) * radPerSecondCoeff;
            set => throw new NotImplementedException();
        }

        public float boostCoeff { get; set; }
        public float armor { get; set; } = 100;
        public float shield { get; set; } = 100;
        public double CrossSectionalArea = 1;

        public Vector3 SizeModifier = Vector3.One;

        public List<Hardpoint> hardpoints = new();

        public List<HardpointSpec> shipsWeapons = new();
        public System.Collections.Generic.Dictionary<HardpointSpec, int> weaponsCounts = new();
        public List<string> weaponsNames;
        public bool adjustableThrust = true;
        public Variant weaponsAsStringArray;

        public class Hardpoint
        {
            public HardpointSpec HardpointSpec;
            public Node3D hardpointContent;
            public Location Location { get; set; }
            public string name { get; set; }
            public int ammo { get; set; }
            public float x { get; set; }
            public float y { get; set; }
            public float z { get; set; }

            public ulong lastFireTime;
            public bool isTurret;

            public bool HasAmmunition()
            {
                return ammo > 0;
            }

            public void UpdateAmmunition(int ammunitionImpact)
            {
                if (ammo == int.MaxValue)
                    return;
                ammo += ammunitionImpact;
            }

            public LineRenderer GetLineRenderer<T>()
            {
                return new LineRenderer();
            }

            public Godot.Collections.Dictionary GetStatus()
            {
                return new Godot.Collections.Dictionary() { { "name", name }, { "ammo", ammo } };
            }
        }

        public void ResolveWeapons()
        {
            shipsWeapons = new List<HardpointSpec>();
            weaponsCounts = new System.Collections.Generic.Dictionary<HardpointSpec, int>();

            foreach (Hardpoint h in hardpoints)
            {
                string lookupName = h.name;
                if (lookupName.EndsWith(".turret", StringComparison.OrdinalIgnoreCase))
                {
                    h.isTurret = true;
                    // Replace .turret with .json for lookup
                    lookupName = lookupName.Substring(0, lookupName.Length - 7) + ".json";
                }

                // Fallback: if user put .json but it's not in dict, or just name without extension
                if (!weapons.ContainsKey(lookupName) && !lookupName.EndsWith(".json"))
                {
                    lookupName += ".json";
                }

                if (!weapons.ContainsKey(lookupName))
                {
                    // Try to find it without exact match? Or just throw clearer error
                    // For now, let's assume the swap worked.
                    // If still fail, maybe it didn't have .turret but some other issue?
                    // But error says: 'Weapon_..._Optics.turret' was not present.
                    // So just swapping should fix it.
                }

                HardpointSpec hardpointSpec = weapons[lookupName];
                h.HardpointSpec = hardpointSpec;
                h.ammo = h.ammo == -1 ? int.MaxValue : h.ammo;
                shipsWeapons.Add(hardpointSpec);

                // Don't modify the shared spec!
                // if (h.name.EndsWith(".turret")) hardpointSpec.isTurret = true;

                int currentCount;
                weaponsCounts.TryGetValue(hardpointSpec, out currentCount);
                weaponsCounts[hardpointSpec] = currentCount + 1;
            }

            List<string> weaponNames = new List<string>();
            foreach (HardpointSpec h in weaponsCounts.Keys)
            {
                weaponNames.Add(h.name);
            }

            weaponsAsStringArray = weaponNames.ToArray();
        }
    }

    private static System.Collections.Generic.Dictionary<ShipType, String> presets = new()
    {
        {
            ShipType.Missile,
            @$"{{
				'name': 'Missile', 
				'size': 1, 
				'maxSpeed': 8, 
                'acceleration': 2,
                'acceleration': 2,
                'manoeuvrability': 10,
                'armor': 0.1,
                'shield': 0, 
				'boostCoeff': 1,
				'adjustableThrust': false,
				'hardpoints' : [
				]
				}}"
        },
        {
            ShipType.TigersClaw,
            @$"{{
                'name': 'Tigers Claw',
                'size': 500,
                'maxSpeed': 2,
                'acceleration': 0.3,
                'manoeuvrability': 1,
                'armor': 5000,
                'shield': 3000,
                'boostCoeff': 1.2,
                'adjustableThrust': true,
                'hardpoints' : [
                    {{'location': 'One', 'name': 'Weapon_Laser_MediumLaserER_1-MagnaVI.turret', 'ammo': -1, 'x': 2.0, 'y': 0.5, 'z': 3.0}},
                    {{'Location': 'Two', 'name': 'Weapon_Laser_MediumLaserER_1-MagnaVI.turret', 'ammo': -1, 'x': -2.0, 'y': 0.5, 'z': 3.0}},
                    {{'Location': 'Three', 'name': 'Weapon_Laser_MediumLaserER_1-MagnaVI.turret', 'ammo': -1, 'x': 2.5, 'y': 0.5, 'z': -2.0}},
                    {{'Location': 'Four', 'name': 'Weapon_Laser_MediumLaserER_1-MagnaVI.turret', 'ammo': -1, 'x': -2.5, 'y': 0.5, 'z': -2.0}},
                    {{'Location': 'Five', 'name': 'Weapon_LRM_LRM20_3-Zeus.json', 'ammo': 200, 'x': 1.5, 'y': 1, 'z': 0.0}},
                    {{'Location': 'Six', 'name': 'Weapon_LRM_LRM20_3-Zeus.json', 'ammo': 200, 'x': -1.5, 'y': 1, 'z': 0.0}},
                ]
            }}"
        },
        /*
        {            ShipType.LightFighter,
                    @"{
                        'name': 'Hornet',
                        'size': 1,
                        'maxSpeed': 8,
                        'acceleration': 500,
                        'rotateSpeed': 180,
                        'afterburner': true,
                        'hardpoints' : [
                            {'location': 'One', 'name': 'Weapon_Laser_MediumLaserER_1-MagnaVI.turret', 'ammo': -1, 'x': 0, 'y': 0.06, 'z': 0.6},
                            {'Location': 'Two', 'name': 'Weapon_Laser_MediumLaserER_1-MagnaVI.turret', 'ammo': -1, 'x': 0.4, 'y': 0.25, 'z': -0.9},
                            {'Location': 'Three', 'name': 'Weapon_Laser_MediumLaserER_1-MagnaVI.turret', 'ammo': -1, 'x': 0.0, 'y': 0.25, 'z': -0.9},
                            {'Location': 'Four', 'name': 'Weapon_LRM_LRM20_3-Zeus.json', 'ammo': -1, 'x': 0.0, 'y': 0.25, 'z': -0.2},
                            {'Location': 'Five', 'name': 'Weapon_Autocannon_AC2_2-Mydron.json', 'ammo': -1, 'x':-0.4, 'y': 0.25, 'z': -0.9},
                        ]
                        }"
        
                },
                */
    };

    private static ShipSpec Create(string s)
    {
        ShipSpec spec = JsonConvert.DeserializeObject<ShipSpec>(s);
        spec.ResolveWeapons();
        return spec;
    }

    public static ShipSpec presetFromString(string type)
    {
        var t = ShipType.Adder;
        ShipType.TryParse(type, true, out t);
        return Create(presets[t]);
    }

    public static ShipSpec GetPreset(ShipType shipType)
    {
        ShipSpec shipSpec = Create(presets[shipType]);
        // Debug.Log($"{shipSpec} {shipSpec.maxSpeed}");
        return shipSpec;
    }

    static ShipFactory()
    {
        FileAccess file = FileAccess.Open("res://ed_ships.csv", FileAccess.ModeFlags.Read);
        var text = file.GetAsText();
        int i = 0;
        foreach (string line in text.Split('\n'))
        {
            if (i++ == 0)
                continue;
            string[] def = line.Split(',');

            string shipId = def[0].Replace(" ", "").Replace("-", "");
            ShipType t;
            ShipType.TryParse(shipId, true, out t);

            // Name, Acc, Mass, Jump, Manuver, Speed, Boost, Armour, Shield, X, Cost
            // Adder, 39.41, 35, 20.76, 4, 244, 355, 315, 102,  3A, 2520740

            presets[t] =
                $@"{{
					'name': '{def[0]}', 
					'acceleration': {float.Parse(def[1]) * accelerationCoefficient}, 
					'size': '{float.Parse(def[2])}', 
					'manoeuvrability': {int.Parse(def[4])}, 
					'maxSpeed': {int.Parse(def[5]) / secondsForSpeed}, // Speed in units/minute? 
					'maxSpeed': {int.Parse(def[5]) / secondsForSpeed}, // Speed in units/minute? 
					'boostCoeff': {float.Parse(def[6]) / float.Parse(def[5])},
                    'armor': {float.Parse(def[7])},
                    'shield': {float.Parse(def[8])},
					'hardpoints' : [ 
						{{
							'location': 'One', 
							'name': 'Weapon_Laser_SmallLaserER_1-Diverse_Optics.turret', 
							'ammo': -1, 
							'x': 0.55, 'y': 0.05, 'z': 0.25
						}},
						{{'Location': 'Two', 
							'name': 'Weapon_Laser_MediumLaserER_1-MagnaVI.json', 
							'ammo': -1, 
							'x': -0.55, 'y': 0.05, 'z': 0.25
						}},
						{{'Location': 'Three', 
							'name': 'Weapon_Laser_MediumLaserER_1-MagnaVI.turret', 
							'ammo': -1, 
							'x': 0.35, 'y': 0.0, 'z': -0.1
}},
						{{'Location': 'Four',
							'name': 'Weapon_LRM_LRM5_0-STOCK.json', 
							'ammo': 100, 
							'x': -0.35, 'y': 0.0, 'z': -0.1
}},
						{{'Location': 'Five', 
							'name': 'Weapon_Autocannon_AC2_2-Mydron.json', 
							'ammo': 100, 
							'x':0, 'y': 0.25, 'z': 0.25
}},
						{{'Location': 'Six', 
							'name': 'PointDefence', 
							'ammo': 100, 
							'x':-0.4, 'y': 0.25, 'z': -0.9}},
					]
				}}";
        }
    }
}

public enum DamageDirection
{
    FRONT,
    BACK,
    LEFT,
    RIGHT,
}

/*
 * TODO: Need to add "types" modifiers, that work better against some weapons than others.
 */
public class ArmorSpec
{
    public float[] maxStrength;
    public float[] strength;

    public ArmorSpec(float baseStrength = 100)
    {
        maxStrength = new float[] { baseStrength, baseStrength, baseStrength, baseStrength };
        strength = new float[] { baseStrength, baseStrength, baseStrength, baseStrength };
    }

    public float Deplete(int damageSide, float damage)
    {
        float residualDamage = Mathf.Min(0, strength[damageSide] - damage);

        strength[damageSide] -= damage;
        if (residualDamage < 0)
            strength[damageSide] = 0;
        return residualDamage;
    }

    public float[] GetStrengthPercents()
    {
        return maxStrength.Zip(strength, (a, b) => b * 100 / a).ToArray();
    }

    public override string ToString()
    {
        return String.Join(",", strength);
    }
}

public class ShieldSpec
{
    private float regenerationRate = 1.0f;
    public float[] maxStrength;
    public float[] strength;

    public ShieldSpec(float baseStrength = 100)
    {
        maxStrength = new float[] { baseStrength, baseStrength, baseStrength, baseStrength };
        strength = new float[] { baseStrength, baseStrength, baseStrength, baseStrength };
    }

    public float Deplete(int damageSide, float damage)
    {
        float residualDamage = Mathf.Min(0, strength[damageSide] - damage);

        strength[damageSide] -= damage;
        if (residualDamage < 0)
            strength[damageSide] = 0;
        return residualDamage;
    }

    public float[] GetStrengthPercents()
    {
        var array = maxStrength.Zip(strength, (a, b) => b * 100 / a).ToArray();
        return array;
    }

    public override string ToString()
    {
        return String.Join(",", strength) + $" {regenerationRate}/sec";
    }
}
