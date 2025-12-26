using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

public class HardpointSpec
{
    // public GameObject ignitionEffect;
    // public GameObject shotEffect;
    // public GameObject hitEffect;

    public string name;
    public string Category;

    public string Manufacturer;
    public string Mounting;
    public float Damage;
    public float DamageStability;
    public int Slots;
    public float HeatGenerated;
    public int CriticalModifier;
    public string Type;
    public string Subtype;
    public string BonusA;
    public string BonusB;
    public float MinRange;
    public float MaxRange;
    public string AmmoCategory;
    public float startAmmoCapacity;
    public float EvasiveDamageMultiplier;
    public float EvasivePipsIgnored;
    public float DamageVariance;
    public float HeatDamage;
    public float AccuracyModifier;
    public float CriticalChanceMultiplier;
    public bool IndirectFireCapable;
    public float RefireModifier;
    public float ShotsWhenFired;
    public float ProjectilesPerShot;
    public float AttackRecoil;
    public float Instability;
    public float InventorySize;
    public float Tonnage;
    public float FiringDelay = 0f;
    public float Duration = 0f;
    public bool isTurret = false;

    //    Name	Manufacturer	Mounting	Damage	Combat performance	Value	Rarity
    //    Tonnage	Slots	Direct	Stability	Heat	Heat generated	Accuracy Modifier	Critical Multiplier

    private string[] value;

    public HardpointSpec(string[] spec)
    {
        string[] labels =
        {
            "",
            "Category",
            "Type",
            "WeaponSubType",
            "BonusValueA",
            "BonusValueB",
            "MinRange",
            "MaxRange",
            "AmmoCategory",
            "StartingAmmoCapacity",
            "HeatGenerated",
            "Damage",
            "OverheatedDamageMultiplier",
            "EvasiveDamageMultiplier",
            "EvasivePipsIgnored",
            "DamageVariance",
            "HeatDamage",
            "AccuracyModifier",
            "CriticalChanceMultiplier",
            "IndirectFireCapable",
            "RefireModifier",
            "ShotsWhenFired",
            "ProjectilesPerShot",
            "AttackRecoil",
            "Instability",
            "InventorySize",
            "Tonnage",
            "FiringDelay",
        };

        int i = 0;

        name = spec[0];
        if (name.ToLower().Contains(".turret"))
            isTurret = true;

        i++;
        Category = spec[i++];
        Type = spec[i++].Trim();
        Subtype = spec[i++].Trim();
        BonusA = spec[i++].Trim();
        BonusB = spec[i++].Trim();
        float.TryParse(spec[i++], out MinRange);
        float.TryParse(spec[i++], out MaxRange);
        AmmoCategory = spec[i++].Trim();
        bool success = (float.TryParse(spec[i++], out startAmmoCapacity));
        success = success && (float.TryParse(spec[i++], out HeatGenerated));
        success = success && (float.TryParse(spec[i++], out Damage));
        success = success && (float.TryParse(spec[i++], out OverheatedDamageMultiplier));
        success = success && (float.TryParse(spec[i++], out EvasiveDamageMultiplier));
        success = success && (float.TryParse(spec[i++], out EvasivePipsIgnored));
        success = success && (float.TryParse(spec[i++], out DamageVariance));
        success = success && (float.TryParse(spec[i++], out HeatDamage));
        success = success && (float.TryParse(spec[i++], out AccuracyModifier));
        success = success && (float.TryParse(spec[i++], out CriticalChanceMultiplier));
        success = success && (bool.TryParse(spec[i++], out IndirectFireCapable));
        success = success && (float.TryParse(spec[i++], out RefireModifier));
        success = success && (float.TryParse(spec[i++], out ShotsWhenFired));
        success = success && (float.TryParse(spec[i++], out ProjectilesPerShot));
        success = success && (float.TryParse(spec[i++], out AttackRecoil));
        success = success && (float.TryParse(spec[i++], out Instability));
        success = success && (float.TryParse(spec[i++], out InventorySize));
        success = success && (float.TryParse(spec[i++], out Tonnage));
        success = success && (float.TryParse(spec[i++], out FiringDelay));

        Damage *= 10;
        if (!success)
        {
            // ("Parse failed: " + string.Join(",", spec));
        }
    }

    public float OverheatedDamageMultiplier;

    public static Dictionary<string, HardpointSpec> GetWeapons()
    {
        Dictionary<string, HardpointSpec> weapons = new Dictionary<string, HardpointSpec>();
        string path = "Scripts/Combat/weapons.csv";

        if (!System.IO.File.Exists(path))
        {
            path = System.IO.Path.Combine(
                System.AppDomain.CurrentDomain.BaseDirectory,
                "Scripts/Combat/weapons.csv"
            );
            if (!System.IO.File.Exists(path))
            {
                // Fallback to project root relative
                path = "Scripts/Combat/weapons.csv";
            }
        }

        if (!System.IO.File.Exists(path))
        {
            // Just return empty if not found to avoid crash, or throw?
            // Throwing is better for debugging.
            return weapons;
        }

        string[] lines = System.IO.File.ReadAllLines(path);
        // Skip header (row 0)
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] entry = ParseCsvLine(line);

            // Determine type based on Category (Index 1)
            HardpointSpec hardpointSpec = null;
            if (entry.Length < 2)
                continue;

            string category = entry[1]; // Index 1

            if (category == "Energy" || category == "AntiPersonnel")
                hardpointSpec = new Energy(entry);
            else if (category == "Ballistic")
                hardpointSpec = new Ballistic(entry);
            else if (category == "Missile")
                hardpointSpec = new Missile(entry);
            else if (category == "Defence" || entry[0] == "PointDefence")
                hardpointSpec = new PointDefence(entry);
            else
                hardpointSpec = new HardpointSpec(entry); // Fallback
            // Debug.Print(hardpointSpec.ToString());
            if (hardpointSpec != null)
            {
                if (weapons.ContainsKey(hardpointSpec.name))
                    continue; // Duplicate protection
                weapons.Add(hardpointSpec.name, hardpointSpec);
            }
        }

        return weapons;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var currentToken = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentToken.ToString());
                currentToken.Clear();
            }
            else
            {
                currentToken.Append(c);
            }
        }

        result.Add(currentToken.ToString());
        return result.ToArray();
    }

    public override string ToString()
    {
        return @$"
name :{name}
manufacturer;   {Manufacturer}                           
mounting;   {Mounting}                               
damage;   {Damage}                                  
damageStability;   {DamageStability}                         
heatDamage;   {HeatDamage}                              
slots;   {Slots}                                     
tonnage;   {Tonnage}                                   
heatGenerated;   {HeatGenerated}                           
accuracyModifier;   {AccuracyModifier}                          
criticalModifier;   {CriticalModifier}                          
type;   {Type}                         
subtype;   {Subtype}                      
bonusA;   {BonusA}                       
bonusB;   {BonusB}                       
minRange;   {MinRange}                     
maxRange;   {MaxRange}                     
ammoCategory;   {AmmoCategory}                 
startAmmoCapacity;   {startAmmoCapacity}                      
EvasiveDamageMultiplier;   {EvasiveDamageMultiplier}                
EvasivePipsIgnored;   {EvasivePipsIgnored}                     
DamageVariance;   {DamageVariance}                         
HeatDamage;   {HeatDamage}                             
AccuracyModifier;   {AccuracyModifier}                       
CriticalChanceMultiplier;   {CriticalChanceMultiplier}               
IndirectFireCapable;   {IndirectFireCapable}                    
RefireModifier;   {RefireModifier}                         
ShotsWhenFired;   {ShotsWhenFired}                         
ProjectilesPerShot;   {ProjectilesPerShot}                     
AttackRecoil;   {AttackRecoil}                           
Instability;   {Instability}                            
InventorySize;   {InventorySize}                          
Tonnage;   {Tonnage}                                
FiringDelay;   {FiringDelay}                                
";
    }

    internal class PointDefence : HardpointSpec
    {
        private int pointCount;

        public PointDefence(string[] value)
            : base(value)
        {
            String n_missiles = Regex.Match(value[3], @"\d+").Value;
            Int32.TryParse(n_missiles, out this.pointCount);
            FiringDelay = this.pointCount / 20f;
        }

        public int PointCount
        {
            get => pointCount;
        }
    }

    internal class Energy : HardpointSpec
    {
        // public List<GameObject> laserImpact;

        internal readonly struct EnergyType
        {
            public float FireDelay { get; } //No setter methods!
            public float Duration { get; } //No setter methods!
            public static EnergyType Pulse = new EnergyType(2.0f, 1.0f);
            public static EnergyType Normal = new EnergyType(0f, 0f);

            private EnergyType(float duration, float f)
            {
                this.Duration = duration;
                this.FireDelay = f;
            }
        }

        public EnergyType type;

        public Energy(string[] value)
            : base(value)
        {
            if (this.Subtype.Contains("Pulse"))
                type = EnergyType.Pulse;
            else
                type = EnergyType.Normal;
            this.Duration = type.Duration;
            // Debug.Log(this.MaxRange + " " + this.Subtype + " " + this.type.FireDelay);
        }
    }

    internal class Ballistic : HardpointSpec
    {
        int numberMissiles;

        public Ballistic(string[] value)
            : base(value)
        {
            String n_missiles = Regex.Match(value[3], @"\d+").Value;
            if (Int32.TryParse(n_missiles, out this.numberMissiles) && this.numberMissiles > 0)
            {
                ShotsWhenFired = this.numberMissiles;
            }
        }

        public int NumberMissiles
        {
            get => numberMissiles;
        }
    }

    public class Missile : HardpointSpec
    {
        int numberMissiles;

        public Missile(string[] value)
            : base(value)
        {
            String n_missiles = Regex.Match(value[3], @"\d+").Value;
            Int32.TryParse(n_missiles, out this.numberMissiles);
        }

        public int NumberMissiles
        {
            get => numberMissiles;
            set => numberMissiles = value;
        }
    }

    /*

    -------------------------------------------------------------------------------------------
    Wing Commander Privateer guns - https://apocalyptech.com/games/privgun/

    Name 	Speed (m/s)	Damage (cm)	Energy (GJ)	Refire Delay (sec)	Price (Cr)	Damage/sec	Energy/sec
    Laser	1400	2.0	4	0.30	1,000	6.67	13.33
    Mass Driver	1100	2.6	5	0.35	1,500	7.43	14.29
    Meson Blaster	1300	3.2	8	0.40	2,500	8.00	20.00
    Neutron Gun	960	6.2	16	0.65	5,000	9.54	24.62
    Particle Cannon	1000	4.3	11	0.50	10,000	8.60	22.00
    Tachyon Cannon	1250	5.0	8	0.40	20,000	12.50	20.00
    Ionic Pulse Cannon	1200	5.4	15	0.60	40,000	9.00	25.00
    Plasma Gun	940	7.2	19	0.72	80,000	10.00	26.39
    Steltek Gun	1175	10.0	19	0.45	0	22.22	42.22
    Steltek Gun Mk2	1250	19.0	17	0.37	0	51.35	45.95


    Missiles

    Proton Torpedoes:
    Speed: 1200 kps
    Damage: 20 cm

    Proton Torpedoes inflict more damage than missiles, and are much cheaper, but they are essentially big dumbfires, making them ineffective against more agile targets expect at point-blank range.

    Dumbfires:
    Speed: 1000 kps
    Damage: 13 cm

    Dumbfires do the least damage of any missile type. You are better off buying some other type instead. They also cost more and do less damage than proton torpedoes.

    Heatseekers:
    Speed: 800 kps
    Damage: 15 cm

    Heatseekers are slow enough that faster ships such as the Centurion can evade or outrun them, but they are cheaper than any other guided missile. They also inflict more damage than Dumbfires.

    ImRecs:
    Speed: 850 kps
    Damage: 17 cm

    ImRecs are a bit pricier than Heatseekers, but are a bit faster and more powerful. Additionally, they can acquire and maintain lock from any angle, making it impossible to shake them off through evasive action. Great if you can afford them.

    Friend or Foe:
    Speed: 900 kps
    Damage: 17 cm

    FF missiles are expensive, but need no target lock. Also, if the target blows up before they arrive, FF missiles will automatically head for the next nearest enemy target.

    */
    private static object[][] ed =
    {
        new object[] { "Plasma Acc", "Thermal & Kinetic", 5, 10, 5, 100, 10, 16, 13, 793, 600 },
        new object[] { "Cannon", "Kinetic", 5, 9, 5, 100, 2, 16, 2, 700, 800 },
        new object[] { "Cannon", "Kinetic", 4, 8, 5, 100, 2, 16, 5, 401, 600 },
        new object[] { "Beam Laser", "Thermal", 41.4, 7, 9.9, 16, 2, 396, 160 },
        new object[] { "Beam Laser", "Thermal", 32.7, 7, 10.6, 16, 8, 746, 160 },
        new object[] { "Pulse Laser", "Thermal", 26.9, 5, 1.6, 16, 177, 600 },
        new object[] { "Pulse Laser", "Thermal", 21.7, 5, 1.6, 16, 877, 600 },
        new object[] { "Multi-Cannon", "Kinetic", 23.3, 4, 90, 2100, 0.5, 16, 6, 377, 600 },
        new object[] { "Multi-Cannon", "Kinetic", 24.5, 4, 90, 2100, 0.4, 16, 1, 177, 600 },
        new object[] { "Plasma Acc", "Thermal & Kinetic", 4, 9, 5, 100, 8, 8, 3, 051, 200 },
        new object[] { "Plasma Acc", "Thermal & Kinetic", 4, 8, 20, 300, 4, 8, 4, 119, 120 },
        new object[] { "Cannon", "Kinetic", 4, 7, 5, 100, 2, 8, 675, 200 },
        new object[] { "Cannon", "Kinetic", 4, 7, 5, 100, 1, 8, 1, 350, 400 },
        new object[] { "Beam Laser", "Thermal", 5, 6, 5, 8, 1, 177, 600 },
        new object[] { "Beam Laser", "Thermal", 4, 6, 6, 8, 2, 396, 160 },
        new object[] { "Cannon", "Kinetic", 4, 6, 5, 100, 1, 8, 16, 204, 800 },
        new object[] { "Beam Laser", "Thermal", 4, 5, 4, 8, 19, 399, 600 },
        new object[] { "Burst Laser", "Thermal", 4, 4, 1, 8, 140, 400 },
        new object[] { "Pulse Laser", "Thermal", 4, 4, 1, 8, 70, 400 },
        new object[] { "Pulse Laser", "Thermal", 4, 3, 1, 8, 140, 600 },
        new object[] { "Frag Cannon", "Kinetic", 10, 3, 3, 90, 1, 8, 1, 167, 360 },
        new object[] { "Frag Cannon", "Kinetic", 10, 3, 3, 90, 1, 8, 1, 751, 040 },
        new object[] { "Multi-Cannon", "Kinetic", 18.9, 3, 90, 2100, 0.3, 8, 578, 436 },
        new object[] { "Multi-Cannon", "Kinetic", 20.8, 3, 90, 2100, 0.3, 8, 140, 400 },
        new object[] { "Burst Laser", "Thermal", 4, 3, 1, 8, 281, 600 },
        new object[] { "Frag Cannon", "Kinetic", 9, 3, 3, 90, 1, 8, 1, 400, 830 },
        new object[] { "Pulse Laser", "Thermal", 3, 3, 1, 8, 400, 400 },
        new object[] { "Burst Laser", "Thermal", 4, 3, 1, 8, 800, 400 },
        new object[] { "Frag Cannon", "Kinetic", 9, 2, 3, 90, 1, 8, 5, 836, 800 },
        new object[] { "Plasma Acc", "Thermal", 4, 7, 5, 100, 10, 4, 834, 200 },
        new object[] { "Missile Rack", "Explosive", 8, 7, 12, 24, 3, 4, 240, 400 },
        new object[] { "Rail Gun", "Thermal & Kinetic", 4, 7, 1, 30, 10, 4, 412, 800 },
        new object[] { "Cannon", "Kinetic", 4, 6, 5, 100, 1, 4, 168, 430 },
        new object[] { "Missile Rack", "Explosive", 3, 6, 6, 18, 3, 4, 512, 400 },
        new object[] { "Cannon", "Kinetic", 3, 6, 5, 100, 1, 4, 337, 600 },
        new object[] { "Rail Gun", "Thermal & Kinetic", 5, 5, 3, 90, 3, 4, 619, 200 },
        new object[] { "Beam Laser", "Thermal", 4, 5, 4, 4, 500, 600 },
        new object[] { "Beam Laser", "Thermal", 4, 5, 4, 4, 299, 520 },
        new object[] { "Cannon", "Kinetic", 3, 5, 5, 100, 1, 4, 4, 051, 200 },
        new object[] { "Beam Laser", "Thermal", 3, 4, 3, 4, 2, 099, 900 },
        new object[] { "Burst Laser", "Thermal", 4, 3, 1, 4, 48, 500 },
        new object[] { "Frag Cannon", "Kinetic", 9, 3, 3, 90, 1, 4, 291, 840 },
        new object[] { "Pulse Laser", "Thermal", 3, 3, 1, 4, 35, 400 },
        new object[] { "Pulse Laser", "Thermal", 3, 3, 1, 4, 17, 600 },
        new object[] { "Burst Laser", "Thermal", 4, 3, 1, 4, 23, 000 },
        new object[] { "Missile Rack", "Explosive", 4, 3, 12, 120, 3, 4, 768, 600 },
        new object[] { "Multi-Cannon", "Kinetic", 4, 2, 90, 2100, 1, 4, 38, 000 },
        new object[] { "Multi-Cannon", "Kinetic", 4, 2, 90, 2100, 1, 4, 57, 000 },
        new object[] { "Burst Laser", "Thermal", 3, 2, 1, 4, 162, 800 },
        new object[] { "Multi-Cannon", "Kinetic", 3, 2, 90, 2100, 1, 4, 1, 292, 800 },
        new object[] { "Frag Cannon", "Kinetic", 9, 2, 3, 90, 1, 4, 437, 800 },
        new object[] { "Frag Cannon", "Kinetic", 9, 2, 3, 90, 1, 4, 1, 459, 200 },
        new object[] { "Pulse Laser", "Thermal", 3, 2, 1, 4, 132, 800 },
        new object[] { "Missile Rack", "Explosive", 8, 7, 8, 16, 3, 2, 32, 180 },
        new object[] { "Missile Rack", "Explosive", 3, 6, 6, 6, 3, 2, 72, 600 },
        new object[] { "Rail Gun", "Thermal & Kinetic", 4, 6, 1, 30, 7, 2, 51, 600 },
        new object[] { "Cannon", "Kinetic", 3, 5, 5, 100, 1, 2, 42, 200 },
        new object[] { "Cannon", "Kinetic", 3, 5, 5, 100, 1, 2, 21, 100 },
        new object[] { "Beam Laser", "Thermal", 3, 4, 3, 2, 74, 650 },
        new object[] { "Cannon", "Kinetic", 3, 4, 5, 100, 1, 2, 506, 400 },
        new object[] { "Beam Laser", "Thermal", 3, 4, 3, 2, 37, 430 },
        new object[] { "Beam Laser", "Thermal", 3, 3, 2, 2, 500, 000 },
        new object[] { "Burst Laser", "Thermal", 3, 2, 1, 2, 8, 600 },
        new object[] { "Burst Laser", "Thermal", 3, 2, 1, 2, 4, 400 },
        new object[] { "Pulse Laser", "Thermal", 3, 2, 1, 2, 6, 600 },
        new object[] { "Pulse Laser", "Thermal", 2, 2, 1, 2, 26, 000 },
        new object[] { "Burst Laser", "Thermal", 3, 2, 1, 2, 8, 800 },
        new object[] { "Multi-Cannon", "Kinetic", 3, 2, 90, 2100, 1, 2, 9, 500 },
        new object[] { "Frag Cannon", "Kinetic", 7, 2, 3, 90, 1, 2, 54, 720 },
        new object[] { "Frag Cannon", "Kinetic", 8, 2, 3, 90, 1, 2, 36, 000 },
        new object[] { "Multi-Cannon", "Kinetic", 3, 2, 90, 2100, 1, 2, 14, 250 },
        new object[] { "Pulse Laser", "Thermal", 3, 2, 1, 2, 2, 200 },
        new object[] { "Burst Laser", "Thermal", 2, 1, 1, 2, 52, 800 },
        new object[] { "Frag Cannon", "Kinetic", 6, 1, 3, 90, 1, 2, 182, 400 },
        new object[] { "Beam Laser", "Thermal", 3, 0.00, 1, 2, 56, 150 },
        new object[] { "Multi-Cannon", "Kinetic", 2, 0.00, 90, 2100, 1, 2, 81, 600 },
    };

    /*
    Double shot
Efficient
High capacity
Lightweight
Overcharged
Rapid fire
Sturdy
Experimental Effects

The following Experimental Effects can be applied to this module:

Corrosive Shell
Dazzle Shell
Double Braced
Drag Munition
Flow Control
Incendiary Rounds
Multi-Servos
Oversized
Screening Shell
Stripped Down


---- ENERGY MODS
Efficient
Focused
Lightweight
Long-range
Overcharged
Rapid fire
Short-range
Sturdy
Experimental Effects

The following Experimental Effects can be applied to this module:

Concordant Sequence
Double Braced
Flow Control
Inertial Impact
Multi-Servos
Oversized
Phasing Sequence
Scramble Spectrum
Stripped Down
Thermal Shock
    */
}
