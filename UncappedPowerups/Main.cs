using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PowerupConfig
{
    [BepInPlugin(Guid, Name, Version)]
    public class Mod : BaseUnityPlugin
    {
        public const string
            Name = "PowerupConfig",
            Author = "Isse",
            Guid = Author + "." + Name,
            Version = "1.0.0.1";

        public static Mod instance;
        public Harmony harmony;
        public ManualLogSource log;

        class ScalingConfig
        {
            public ConfigEntry<bool> doCD;
            public ConfigEntry<float> linearScaling;

            public ConfigEntry<float> cdScaling;
            public ConfigEntry<float> cdMax;

            public ConfigEntry<float> outsideScaling;
            public ConfigEntry<float> baseValue;

            public DisplayType displayType;
            public bool isInt;

            public string item;
        }

        class ItemConfig
        {
            public List<string> scalings = new List<string>();
            public ConfigEntry<bool> enabled;
            public string format;
            public string name;
            public int id;
        }

        Dictionary<string, ScalingConfig> scalings = new Dictionary<string, ScalingConfig>();



        Dictionary<string, ItemConfig> items = new Dictionary<string, ItemConfig>();
        Dictionary<string, string> fromItemDisplayName = new Dictionary<string, string>();

        ConfigEntry<bool> betterChances;
        ConfigEntry<int> powerupsPerPickup;

        ConfigEntry<bool> guaranteeItem;
        ConfigEntry<int> chestSpawnAmount;
        ConfigEntry<float> whiteChestWeight;
        ConfigEntry<float> blueChestWeight;
        ConfigEntry<float> orangeChestWeight;
        ConfigEntry<float> blackChestWeight;

        public int ChestSpawnAmount => chestSpawnAmount.Value;
        public float WhiteChestWeight => whiteChestWeight.Value;
        public float BlueChestWeight => blueChestWeight.Value;
        public float OrangeChestWeight => orangeChestWeight.Value;
        public float BlackChestWeight => blackChestWeight.Value;

        public bool BetterChances()
        {
            return betterChances.Value;
        }

        public int PowerUpsPerPickup()
        {
            return powerupsPerPickup.Value;
        }

        public bool IsEnabled(string name)
        {
            return items[name].enabled.Value;
        }

        void AddScaling([NotNull]string name, float linearScaling, float cdScaling, float cdMax, bool doCDScaling, DisplayType type = DisplayType.None, float baseValue = 0f, float scaling = 1f, bool isInt = false, string item = null)
        {
            if (item == null) item = name;
            string rHeader = "powerups/" + item;

            if (!items.ContainsKey(item))
            {
                items.Add(item, new ItemConfig() { enabled = Config.Bind(rHeader, item + "Enabled", true, "Should this item be enabled?") });
            }
            items[item].scalings.Add(name);

            var cfg = new ScalingConfig();
            cfg.displayType = type;
            cfg.isInt = isInt;
            cfg.item = item;

            cfg.linearScaling = Config.Bind(rHeader, name + "LinearScaling", linearScaling, "Linear scaling for item.");
            cfg.baseValue = Config.Bind(rHeader, name + "BaseValue", baseValue, "Base value for item");
            cfg.outsideScaling = Config.Bind(rHeader, name + "ExtraLinearScaling", scaling, "Extra linear scaling for item");


            cfg.doCD = Config.Bind(rHeader, name + "DoCap", doCDScaling, "Should item be capped");
            cfg.cdScaling = Config.Bind(rHeader, name + "CDScaling", cdScaling, "Cumulative distribution scaling for item.");
            cfg.cdMax = Config.Bind(rHeader, name + "CDMax", cdMax, "Cumulative distribution max for item.");

            scalings.Add(name, cfg);
            // log.LogMessage("Added Scaling: " + name);
        }

        static string Capitalize(string str)
        {
            return str.Substring(0, 1).ToUpper() + str.Substring(1);
        }

        static string Decapitalize(string str)
        {
            return str.Substring(0, 1).ToLower() + str.Substring(1);
        }

        static string Format(string name)
        {
            var words = name.Split();
            string result = Decapitalize(words[0]);
            for (int i = 1; i < words.Length; i++)
            {
                result += Capitalize(words[i]);
            }
            return result;
        }

        public string FromDisplayName(string name)
        {
            return fromItemDisplayName[name];
        }

        void SetFormat(string itemName, string format)
        {
            string item = Format(itemName);
            if (!items.ContainsKey(item))
            {
                fromItemDisplayName.Add(itemName, item);
                items.Add(item, new ItemConfig() { enabled = Config.Bind("powerups/" + item, item + "Enabled", true, "Should this item be enabled?") });
            }
            items[item].format = format;
            items[item].name = itemName;
        }

        void AddDescription(string name, Func<int, bool, string> print)
        {
            if (name == null)
            {
                log.LogError("Name is null");
                if (print == null)
                {
                    log.LogError("Function is null");
                }
                else
                {
                    log.LogMessage(print(1, false));
                }
            }
            else
            {
                // log.LogMessage("Sending function: " + name);
                SendMessage("RegisterPowerUp", new Tuple<string, Func<int, bool, string>>(name, print));
            }
        }

        public static float CD(int count, float scaleSpeed, float maxValue)
        {
            float f = 2.71828f;
            return (1f - Mathf.Pow(f, (float)(-(float)count) * scaleSpeed)) * maxValue;
        }

        public bool DoCD(string name)
        {
            return scalings[name].doCD.Value;
        }

        public float GetScaling(string name)
        {
            return scalings[name].linearScaling.Value;
        }

        public float GetValue(string name, int count)
        {
            return count * GetScaling(name);
        }

        public string GetScalingDisplayName(string name)
        {
            return items[scalings[name].item].name;
        }

        public int GetItemID(string item) 
        {
            return items[item].id;
        }

        public int GetScalingID(string scaling)
        {
            ItemConfig cfg;
            if (scalings.TryGetValue(scaling, out var val))
            {
                cfg = items[val.item];
            }
            else
            {
                cfg = items[scaling];
            }

            return cfg.id;
        }

        public void LoadIDs()
        {
            foreach (var p in items)
            {
                var item = p.Value;
                item.id = ItemManager.Instance.stringToPowerupId[item.name];
            }
        }

        public float ChanceMultiplier(string chance, string power, int count, float orElse = 1f)
        {
            float c = Calculate(chance, count);
            float bas = scalings[power].baseValue.Value;
            if (BetterChances())
            {
                int b = Mathf.FloorToInt(c);

                if (Random.Range(0f, 1f) < c - b)
                {
                    b++;
                }
                return b > 0 ? (bas + b * CalculateNoBase(power, count)) : orElse;
            }
            else
            {
                if (Random.Range(0f, 1f) < c)
                {
                    return Calculate(power, count);
                }
                return orElse;
            }
        }

        void Awake()
        {
            if (!instance)
                instance = this;
            else
                Destroy(this);

            log = Logger;
            harmony = new Harmony(Guid);

            betterChances = Config.Bind("general", "betterChances", false, 
                "Should values scale when chances go over 100? for example if you have 140% crit chance you have 100% chance to deal 2x damage and 40% chance to deal 3x damage.");
            powerupsPerPickup = Config.Bind("general", "powerupsPerPickup", 1, "How many powerups you get each time you pick one up.");
            
            guaranteeItem = Config.Bind("chests", "guaranteeItem", true, "Guarantees that an item is given from a chest even if all items in that tier is disabled. Does not work if all items are disabled.");
            chestSpawnAmount = Config.Bind("chests", "chestSpawnAmount", 10, "How many times chests try to spawn in each chunk.");
            whiteChestWeight = Config.Bind("chests", "chestWeightWhite", 0.65f, "Spawnrate of white chests");
            blueChestWeight = Config.Bind("chests", "chestWeightBlue", 0.2f, "Spawnrate of blue chests");
            orangeChestWeight = Config.Bind("chests", "chestWeightOrange", 0.1f, "Spawnrate of orange chests");
            blackChestWeight = Config.Bind("chests", "chestWeightBlack", 0.07f, "Spawnrate of black chests");

            SetFormats();
            AddScalings();

            

            harmony.PatchAll();
        }

        void SetFormats()
        {
            SetFormat("Dumbbell", "{0} strength");
            SetFormat("Berserk", "+{0}% strength per % of missing hp");
            SetFormat("Peanut Butter", "{0} stamina");
            SetFormat("Blue Pill", "+{0} shield that regens after {1}");
            SetFormat("Dracula", "Gain {0} max hp on kill, stacks up to {1} max hp");
            SetFormat("Red Pill", "+{0} max hp");
            SetFormat("Broccoli", "+{0} healing");
            SetFormat("Janniks Frog", "+{0} extra jumps");
            SetFormat("Adrenaline", "{0} stamina, movement speed and attack speed for {1} with a cooldown of {2}");
            SetFormat("Sneaker", "+{0} movement speed");
            SetFormat("Sniper Scope", "{0} chance to deal {1} damage");
            SetFormat("Robin Hood Hat", "{0} faster draw speed, arrow speed and arrow damage");
            SetFormat("Checkered Shirt", "{0} resource damage");
            SetFormat("Piggybank", "{0} loot gained");
            SetFormat("Knuts Hammer", "{0} chance to summon lightning that deals {1} of the damage dealt");
            SetFormat("Crimson Dagger", "Heal for {0} of damage dealt");
            SetFormat("Bulldozer", "{0} chance to knockback target with {1} newton");
            SetFormat("Jetpack", "{0} jump height");
            SetFormat("Juice", "{0} attack speed for {1}");
            SetFormat("Spooo Bean", "{0} hunger drain");
            SetFormat("Wings of Glory", "{0} damage while falling");
            SetFormat("Enforcer", "+{0} damage for each meter moved per second");
            SetFormat("Danis Milk", "+{0} armor");
            SetFormat("Horseshoe", "+{0} crit chance");
            SetFormat("Orange Juice", "{0} attack speed");
        }

        void AddScalings()
        {
            AddScaling("dumbbell", 0.1f, 0.01f, 100f, false, DisplayType.Percent, 1f);
            AddScaling("berserk", 1f, 0.01f, 100f, false);
            AddScaling("peanutButter", 0.15f, 0.1f, 2f, false, DisplayType.Percent, 1f);
            AddScaling("bluePillShield", 10f, 0.2f, 100f, false, isInt: true, item: "bluePill"); 
            AddScaling("bluePillDelay", 0f, 0f, 0f, true, DisplayType.Second, 5f, -1f, item: "bluePill");
            AddScaling("draculaGain", 1f, 0.1f, 20f, false, isInt: true, item: "dracula");
            AddScaling("draculaCap", 40f, 0.1f, 500f, false, isInt: true, item: "dracula");
            AddScaling("redPill", 10f, 0.2f, 100f, false, isInt: true);
            AddScaling("broccoli", 0.05f, 0.04f, 2f, false, DisplayType.Percent);
            AddScaling("janniksFrog", 1f, 0.1f, 10f, false, isInt: true);
            AddScaling("adrenalineBoost", 0.5f, 1f, 2f, true, DisplayType.Percent, 1f, item: "adrenaline");
            AddScaling("adrenalineDuration", 0f, 0f, 0f, true, DisplayType.Second, 5f, item: "adrenaline");
            AddScaling("adrenalineCooldown", 0f, 0f, 0f, true, DisplayType.Second, 10f, -1f, item: "adrenaline");
            AddScaling("sneaker", 0.05f, 0.08f, 1.75f, true, DisplayType.Percent, 1f);
            AddScaling("sniperScopeChance", 0.01f, 0.14f, 0.15f, true, DisplayType.Percent, item: "sniperScope");
            AddScaling("sniperScopeDamage", 10f, 0.25f, 50f, true, DisplayType.Percent, item: "sniperScope");
            AddScaling("robinHoodHat", 0.05f, 0.06f, 2f, true, DisplayType.Percent, 1f);
            AddScaling("checkeredShirt", 0.25f, 0.3f, 4f, true, DisplayType.Percent, 1f);
            AddScaling("piggybank", 0.14f, 0.15f, 1.25f, true, DisplayType.Percent, 1f);
            AddScaling("knutsHammerChance", 0.08f, 0.12f, 0.4f, true, DisplayType.Percent, item: "knutsHammer");
            AddScaling("knutsHammerDamage", 0.12f, 0.12f, 1f, true, DisplayType.Percent, 2f, item: "knutsHammer");
            AddScaling("crimsonDagger", 0.05f, 0.1f, 0.5f, true, DisplayType.Percent);
            AddScaling("bulldozerChance", 0.1f, 0.15f, 1f, true, DisplayType.Percent, item: "bulldozer");
            AddScaling("bulldozerPower", 0.0f, 0.0f, 0.0f, false, baseValue: 1f, item: "bulldozer");
            AddScaling("jetpack", 0.15f, 0.075f, 2.5f, true, DisplayType.Percent, 1f);
            AddScaling("juiceAttackSpeed", 0.2f, 0.3f, 1f, true, DisplayType.Percent, 1f, item: "juice");
            AddScaling("juiceDuration", 0.0f, 0.0f, 0.0f, false, DisplayType.Second, 2f, item: "juice");
            AddScaling("spoooBean", 0.05f, 0.2f, 0.5f, true, DisplayType.Percent, 1f, -1f);
            AddScaling("wingsOfGlory", 0.3f, 0.45f, 2.5f, true, DisplayType.Percent, 1f);
            AddScaling("enforcer", 0.5f, 0.4f, 2f, true, DisplayType.Percent, scaling: 1f / 20f);
            AddScaling("danisMilk", 3f, 0.1f, 40f, true);
            AddScaling("horseshoe", 0.05f, 0.08f, 0.9f, true, DisplayType.Percent);
            AddScaling("orangeJuice", 0.1f, 0.12f, 1f, true, DisplayType.Percent, 1f);
        }

        string Percent(float f, float cap = float.PositiveInfinity)
        {
            float val = f * 100f;
            if (val > cap)
            {
                val = cap;
            }
            if (val < 0f)
            {
                val = 0f;
            }
            return val.ToString("N0") + "%";
        }

        string Seconds(float f)
        {
            return f.ToString("N1") + " seconds";
        }

        enum DisplayType
        {
            None,
            Percent,
            Second,
        }

        float Transform(float f, DisplayType type)
        {
            if (type == DisplayType.Percent)
            {
                return f * 100;
            }
            return f;
        }

        string Display(string s, DisplayType type)
        {
            switch (type)
            {
                case DisplayType.Percent:
                    return s + "%";
                case DisplayType.Second:
                    return s + " seconds";
            }
            return s;
        }

        string Display(float f, DisplayType type, bool floor)
        {
            if (floor)
            {
                switch (type)
                {
                    case DisplayType.Percent:
                        return (int)Math.Max(0, f) + "%";
                    case DisplayType.Second:
                        return (int)Math.Max(0, f) + " seconds";
                }
                return ((int)f).ToString();
            }
            switch (type)
            {
                case DisplayType.Percent:
                    return Math.Max(0, f).ToString("N0") + "%";
                case DisplayType.Second:
                    return Math.Max(0, f).ToString("N1") + " seconds";
            }
            return f.ToString("N2");
        }
        public float CalculateNoBase(string name, int count)
        {
            float val;
            if (DoCD(name))
            {
                val = CD(count, scalings[name].cdScaling.Value, scalings[name].cdMax.Value);
            }
            else
            {
                val = count * GetScaling(name);
            }
            return val * scalings[name].outsideScaling.Value;
        }

        public float Calculate(string name, int count)
        {
            return scalings[name].baseValue.Value + CalculateNoBase(name, count);
        }

        string Display(string name, int count, bool extraInfo)
        {
            DisplayType type = scalings[name].displayType;
            bool floor = scalings[name].isInt;
            if (extraInfo)
            {
                float scaling = scalings[name].outsideScaling.Value;
                float baseValue = scalings[name].baseValue.Value;
                baseValue = Transform(baseValue, type);
                scaling = Transform(scaling, type);
                string res;
                if (DoCD(name))
                {
                    float scale = scalings[name].cdMax.Value * scaling;
                    res = string.Format("({0}(1 - e ^ (<color=#8B0000>{1}</color> * {2})) * {3})", baseValue == 0f ? (scale < 0 ? "-" : "") : (baseValue + (scale < 0 ? " - " : " + ")), 
                        count, -scalings[name].cdScaling.Value, Math.Abs(scale));
                }
                else
                {
                    float scale = scaling * GetScaling(name);
                    res = string.Format("({0}{1}<color=#8B0000>{2}</color>)", baseValue == 0f ? "" : (baseValue + " "),
                        scale == 1f ? (baseValue == 0f ? "" : "+ ") : ((scale >= 0 ? (baseValue == 0f ? "" : "+ ") : ("-" + (baseValue == 0f ? "" : " "))) + scale + " * "), count);
                }
                if (floor)
                {
                    return string.Format("Floor{0}", res);
                }
                return "<i>" + Display(res, type) + "</i>";
            }
            else
            {
                return "<color=#8B0000>" + Display(Transform(Calculate(name, count), type), type, floor) + "</color>";
            }
        }

        void Start()
        {
            foreach (var p in items)
            {
                if (p.Value.name.IsNullOrWhiteSpace())
                {
                    continue;
                }
                AddDescription(p.Value.name, (count, extraInfo) =>
                {
                    object[] displays = new object[p.Value.scalings.Count];
                    for (int i = 0; i < displays.Length; i++)
                    {
                        displays[i] = Display(p.Value.scalings[i], count, extraInfo);
                    }
                    return string.Format(p.Value.format, displays);
                });
            }
        }
    }
}
