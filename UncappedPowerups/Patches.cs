using HarmonyLib;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PowerupConfig
{

    [HarmonyPatch(typeof(GenerateAllResources))]
    class GeneratePatch
    {
        [HarmonyPatch("Awake"), HarmonyPrefix]
        static bool Awake(GenerateAllResources __instance)
        {
            ResourceGenerator chestGenerator = null;
            for (int i = 0; i < __instance.spawners.Length; i++)
            {
                if (__instance.spawners[i] && __instance.spawners[i].name == "GenerateChests")
                {
                    chestGenerator = __instance.spawners[i].GetComponent<ResourceGenerator>();
                    break;
                }
            }
            if (chestGenerator)
            {
                chestGenerator.minSpawnAmount = Mod.instance.ChestSpawnAmount;
                chestGenerator.resourcePrefabs[0].weight = Mod.instance.WhiteChestWeight;
                chestGenerator.resourcePrefabs[1].weight = Mod.instance.BlackChestWeight;
                chestGenerator.resourcePrefabs[2].weight = Mod.instance.OrangeChestWeight;
                chestGenerator.resourcePrefabs[3].weight = Mod.instance.BlackChestWeight;
            }

            return true;
        }
    }
    
    
    [HarmonyPatch(typeof(PowerupUI))]
    class PowerupUIPatch
    {
        [HarmonyPatch(nameof(PowerupUI.AddPowerup)), HarmonyPrefix]
        static bool AddPowerup(PowerupUI __instance, int powerupId, ref Dictionary<int, GameObject> ___powerups)
        {
            if (PowerupInventoryPatch.gain == 1) return true;

            if (!___powerups.ContainsKey(powerupId))
            {
                GameObject gameObject = Object.Instantiate(__instance.uiPrefab, __instance.transform);
                Powerup powerup = ItemManager.Instance.allPowerups[powerupId];
                gameObject.GetComponent<Image>().sprite = powerup.sprite;
                gameObject.GetComponent<PowerupInfo>().powerup = powerup;
                ___powerups.Add(powerupId, gameObject);
            }

            TextMeshProUGUI componentInChildren = ___powerups[powerupId].GetComponentInChildren<TextMeshProUGUI>();
            componentInChildren.text = Traverse.Create(PowerupInventory.Instance).Field<int[]>("powerups").Value[powerupId].ToString();
            return false;
        }
    }


    [HarmonyPatch(typeof(PowerupInventory))]
    class PowerupInventoryPatch
    {
        static int Count(string name, int[] powerups)
        {
            int c = powerups[Mod.instance.GetScalingID(name)];
#if DEBUG
            if (name != "spoooBean" && name != "sneaker" && name != "broccoli" && name != "peanutButter")
            {
                Mod.instance.log.LogMessage(name + " count: " + c);
            }
#endif
            return c;
        }



        static float Calculate(string name, int[] powerups)
        {
            return Mod.instance.Calculate(name, Count(name, powerups));
        }


        public static float AdrenalineDuration()
        {
            return Calculate("adrenalineDuration", Traverse.Create(PowerupInventory.Instance).Field<int[]>("powerups").Value);
        }
        public static float AdrenalineCooldown()
        {
            return Calculate("adrenalineCooldown", Traverse.Create(PowerupInventory.Instance).Field<int[]>("powerups").Value);
        }

        public static float ShieldRegenDelay()
        {
            return Calculate("bluePillDelay", Traverse.Create(PowerupInventory.Instance).Field<int[]>("powerups").Value);
        }

        public static int gain = 1;

        [HarmonyPatch(nameof(PowerupInventory.AddPowerup)), HarmonyPrefix]
        static bool AddPowerup(PowerupInventory __instance, string name, int powerupId, int objectId, ref int[] ___powerups)
        {
            if (LocalClient.serverOwner)
            {
                gain = Mod.instance.PowerUpsPerPickup();
            }

            ___powerups[powerupId] += gain;

            UiEvents.Instance.AddPowerup(ItemManager.Instance.allPowerups[powerupId]);

            PlayerStatus.Instance.UpdateStats();
            PowerupUI.Instance.AddPowerup(powerupId);
            string colorName = ItemManager.Instance.allPowerups[powerupId].GetColorName();
            string message = string.Concat(new string[]
            {
            "Picked up <color=",
            colorName,
            ">(",
            name,
            ")<color=white>"
            });
            ChatBox.Instance.SendMessage(message);
            Vector3 position = ItemManager.Instance.list[objectId].transform.position;

            ParticleSystem particleSystem = Object.Instantiate(__instance.powerupFx, position, Quaternion.identity).GetComponent<ParticleSystem>();
            var main = particleSystem.main;
            main.startColor = ItemManager.Instance.allPowerups[powerupId].GetOutlineColor();

            if (ItemManager.Instance.allPowerups[powerupId].tier == Powerup.PowerTier.Orange)
            {
                particleSystem.gameObject.GetComponent<RandomSfx>().sounds = new AudioClip[]
                {
                    __instance.goodPowerupSfx
                };
                particleSystem.GetComponent<RandomSfx>().Randomize(0f);
            }
            AchievementManager.Instance.PickupPowerup(name);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.StartJuice)), HarmonyPrefix]
        static bool StartJuice(PowerupInventory __instance, ref int[] ___powerups, ref float ___juiceSpeed)
        {
            int count = Count("juice", ___powerups);
            if (count == 0)
            {
                return false;
            }
            ___juiceSpeed = __instance.GetJuiceMultiplier(null);
            __instance.CancelInvoke("StopJuice");
            __instance.Invoke("StopJuice", Mod.instance.Calculate("juiceDuration", count));
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetAdrenalineBoost)), HarmonyPrefix]
        static bool GetAdrenalineBoost(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("adrenalineBoost", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetStrengthMultiplier)), HarmonyPrefix]
        static bool GetStrengthMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = 1f + Calculate("dumbbell", playerPowerups) +
                            Calculate("berserk", playerPowerups)  * 
                       (PlayerStatus.Instance.maxHp - PlayerStatus.Instance.hp) / (float)PlayerStatus.Instance.maxHp;
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetStaminaMultiplier)), HarmonyPrefix]
        static bool GetStaminaMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("peanutButter", playerPowerups) *
                        (PlayerStatus.Instance.adrenalineBoost ? __instance.GetAdrenalineBoost(playerPowerups) : 1f);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetSpeedMultiplier)), HarmonyPrefix]
        static bool GetSpeedMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("sneaker", playerPowerups) *
                        (PlayerStatus.Instance.adrenalineBoost ? __instance.GetAdrenalineBoost(playerPowerups) : 1f) *
                        PlayerStatus.Instance.currentSpeedArmorMultiplier;
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetSniperScopeMultiplier)), HarmonyPrefix]
        static bool GetSniperScopeMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            int count = Count("sniperScope", playerPowerups);
            __result = Mod.instance.ChanceMultiplier("sniperScopeChance", "sniperScopeDamage", count);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetSniperScopeDamageMultiplier)), HarmonyPrefix]
        static bool GetSniperScopeDamageMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("sniperScopeDamage", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetShield)), HarmonyPrefix]
        static bool GetShield(PowerupInventory __instance, ref int __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = (int)Calculate("bluePillShield", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetRobinMultiplier)), HarmonyPrefix]
        static bool GetRobinMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("robinHoodHat", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetResourceMultiplier)), HarmonyPrefix]
        static bool GetResourceMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }

            float b = GameManager.gameSettings.gameMode == GameSettings.GameMode.Versus ? 1.75f : 0.0f;

            __result = b + Calculate("checkeredShirt", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetMaxDraculaStacks)), HarmonyPrefix]
        static bool GetMaxDraculaStacks(PowerupInventory __instance, ref int __result, ref int[] ___powerups)
        {
            __result = (int)Calculate("draculaCap", ___powerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetLootMultiplier)), HarmonyPrefix]
        static bool GetLootMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            
            __result = Calculate("piggybank", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetLightningMultiplier)), HarmonyPrefix]
        static bool GetLightningMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            int count = Count("knutsHammer", playerPowerups);
            __result = Mod.instance.ChanceMultiplier("knutsHammerChance", "knutsHammerDamage", count, -1f);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetLifestealMultiplier)), HarmonyPrefix]
        static bool GetLifestealMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("crimsonDagger", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetKnockbackMultiplier)), HarmonyPrefix]
        static bool GetKnockbackMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            int count = Count("bulldozer", playerPowerups);

            __result = Mod.instance.ChanceMultiplier("bulldozerChance", "bulldozerPower", count, 0f);

            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetJumpMultiplier)), HarmonyPrefix]
        static bool GetJumpMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("jetpack", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetJuiceMultiplier)), HarmonyPrefix]
        static bool GetJuiceMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("juiceAttackSpeed", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetHungerMultiplier)), HarmonyPrefix]
        static bool GetHungerMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Mathf.Max(0f, Calculate("spoooBean", playerPowerups));
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetHpMultiplier)), HarmonyPrefix]
        static bool GetHpMultiplier(PowerupInventory __instance, ref int __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = (int)Calculate("redPill", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetHpIncreasePerKill)), HarmonyPrefix]
        static bool GetHpIncreasePerKill(PowerupInventory __instance, ref int __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = (int)Calculate("draculaGain", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetHealingMultiplier)), HarmonyPrefix]
        static bool GetHealingMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("broccoli", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetFallWingsMultiplier)), HarmonyPrefix]
        static bool GetFallWingsMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            int count = Count("wingsOfGlory", playerPowerups);
            if (count == 0)
            {
                __result = 1f;
                return false;
            }
            __result = Mod.instance.Calculate("wingsOfGlory", count);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetExtraJumps)), HarmonyPrefix]
        static bool GetExtraJumps(PowerupInventory __instance, ref int __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = (int)Calculate("janniksFrog", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetAttackSpeedMultiplier)), HarmonyPrefix]
        static bool GetAttackSpeedMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, ref float ___juiceSpeed, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = ___juiceSpeed * (Calculate("orangeJuice", playerPowerups));

            if (PlayerStatus.Instance.adrenalineBoost)
            {
                __result *= __instance.GetAdrenalineBoost(playerPowerups);
            }
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetCritChance)), HarmonyPrefix]
        static bool GetCritChance(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = 0.1f + Calculate("horseshoe", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetDefenseMultiplier)), HarmonyPrefix]
        static bool GetDefenseMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            __result = Calculate("danisMilk", playerPowerups);
            return false;
        }

        [HarmonyPatch(nameof(PowerupInventory.GetEnforcerMultiplier)), HarmonyPrefix]
        static bool GetEnforcerMultiplier(PowerupInventory __instance, ref float __result, ref int[] ___powerups, int[] playerPowerups, float speed)
        {
            if (playerPowerups == null)
            {
                playerPowerups = ___powerups;
            }
            int count = Count("enforcer", playerPowerups);
            if (count == 0)
            {
                __result = 1f;
                return false;
            }
            
            if (speed < 0)
            {
                speed = PlayerMovement.Instance.GetVelocity().magnitude;
            }

            __result = 1f + Mod.instance.Calculate("enforcer", count) * speed;
            return false;
        }

    }

    [HarmonyPatch(typeof(PowerupCalculations))]
    class PowerupCalculationsPatch
    {
        private static Vector2 randomDamageRange = new Vector2(0.4f, 1.2f);

        static float GetMultiplier(float chance)
        {
            if (Mod.instance.BetterChances())
            {
                int b = Mathf.FloorToInt(chance);
                if (Random.Range(0f, 1f) < chance - b)
                {
                    b++;
                }
                return 1f + b;
            }
            else
            {
                if (Random.Range(0f, 1f) < chance)
                {
                    return 2f;
                }
                return 1f;
            }
        }

        [HarmonyPatch(nameof(PowerupCalculations.GetDamageMultiplier)), HarmonyPrefix]
        static bool GetDamageMultiplier(PowerupCalculations __instance, ref PowerupCalculations.DamageResult __result, bool falling, float speedWhileShooting)
        {
            float baseDamage = Random.Range(randomDamageRange.x, randomDamageRange.y) * PowerupInventory.Instance.GetStrengthMultiplier(null);

            float crit = GetMultiplier(PowerupInventory.Instance.GetCritChance(null));

            baseDamage *= crit;

            float lifestealMultiplier = PowerupInventory.Instance.GetLifestealMultiplier(null);
            float sniperScopeMultiplier = PowerupInventory.Instance.GetSniperScopeMultiplier(null);

            bool sniped = false;
            baseDamage *= sniperScopeMultiplier;

            if (sniperScopeMultiplier > 1f)
            {
                sniped = true;
            }
            float lightningMultiplier = PowerupInventory.Instance.GetLightningMultiplier(null);

            float num2 = 1f;
            if (falling)
            {
                num2 = PowerupInventory.Instance.GetFallWingsMultiplier(null);
            }
            float enforcerMultiplier = PowerupInventory.Instance.GetEnforcerMultiplier(null, speedWhileShooting);
            baseDamage *= num2 * enforcerMultiplier;
            __result = new PowerupCalculations.DamageResult(baseDamage, crit > 1f, lifestealMultiplier, sniped, lightningMultiplier, num2 > 1f);
            return false;
        }
    }

    [HarmonyPatch(typeof(ServerHandle))]
    class ServerHandlePatch
    {
        [HarmonyPatch(nameof(ServerHandle.ItemPickedUp)), HarmonyPrefix]
        static bool ItemPickedUp(int fromClient, Packet packet)
        {
            if (Server.clients[fromClient].player == null)
            {
                return false;
            }
            int pickupID = packet.ReadInt(true);
            Debug.Log(string.Concat(new object[]
            {
            "object: ",
            pickupID,
            " picked up by player: ",
            fromClient
            }));
            Item component = ItemManager.Instance.list[pickupID].GetComponent<Item>();

            if (!ItemManager.Instance.list.ContainsKey(pickupID))
            {
                return false;
            }

            if (component.powerup)
            {
                if (Mod.instance.IsEnabled(Mod.instance.FromDisplayName(component.powerup.name)))
                {
                    Server.clients[fromClient].player.powerups[component.powerup.id] += Mod.instance.PowerUpsPerPickup();
                    GameManager.instance.powerupsPickedup = true;
                    Dictionary<string, int> stats = Server.clients[fromClient].player.stats;
                    int num2 = stats["Powerups"];
                    stats["Powerups"] = num2 + 1;
                    if (fromClient == LocalClient.instance.myId)
                    {
                        PowerupInventory.Instance.AddPowerup(component.powerup.name, component.powerup.id, pickupID);
                    }
                }
            }
            else if (component.item)
            {
                if (fromClient == LocalClient.instance.myId)
                {
                    InventoryUI.Instance.AddItemToInventory(component.item);
                }
            }
            ItemManager.Instance.PickupItem(pickupID);
            ServerSend.PickupItem(fromClient, pickupID);
            return false;
        }

        [HarmonyPatch(nameof(ServerHandle.PlayerDamageMob)), HarmonyPrefix]
        static bool PlayerDamageMob(int fromClient, Packet packet)
        {
            if (Server.clients[fromClient].player == null)
            {
                return false;
            }
            int mobID = packet.ReadInt(true);
            int damage = packet.ReadInt(true);
            float sharpness = packet.ReadFloat(true);
            int hitEffect = packet.ReadInt(true);
            Vector3 pos = packet.ReadVector3(true);
            int weaponType = packet.ReadInt(true);
            if (!MobManager.Instance.mobs.ContainsKey(mobID))
            {
                return false;
            }
            Mob mob = MobManager.Instance.mobs[mobID];
            if (!mob)
            {
                return false;
            }
            HitableMob component = mob.GetComponent<HitableMob>();
            if (component.hp <= 0)
            {
                return false;
            }
            float sharpDefense = component.mob.mobType.sharpDefense;
            float defense = component.mob.mobType.defense;
            int dmg = GameManager.instance.CalculateDamage((float)damage, defense, sharpness, sharpDefense);
            Debug.Log(string.Format("Mob took {0} damage from {1}.", dmg, fromClient));
            int newHP = component.hp - dmg;
            if (newHP > component.maxHp)
            {
                newHP = component.maxHp;
            }
            if (newHP <= 0)
            {
                newHP = 0;
                LootDrop dropTable = component.dropTable;
                float buffMultiplier = 1f;
                Mob mobComponent = component.GetComponent<Mob>();
                if (mobComponent && mobComponent.IsBuff())
                {
                    buffMultiplier = 1.25f;
                }
                LootExtra.DropMobLoot(component.transform, dropTable, fromClient, buffMultiplier);
                if (mobComponent.bossType != Mob.BossType.None)
                {
                    LootExtra.BossLoot(component.transform, mob.bossType);
                }
                Dictionary<string, int> stats = Server.clients[fromClient].player.stats;
                int killCount = stats["Kills"];
                stats["Kills"] = killCount + 1;
            }
            component.hp = component.Damage(newHP, fromClient, hitEffect, pos);
            float knockbackMultiplier = PowerupInventory.Instance.GetKnockbackMultiplier(Server.clients[fromClient].player.powerups);
            if (((float)dmg / (float)mob.hitable.maxHp > mob.mobType.knockbackThreshold || knockbackMultiplier > 0f) && newHP > 0)
            {
                Vector3 vector = component.transform.position - GameManager.players[fromClient].transform.position;
                vector = VectorExtensions.XZVector(vector).normalized * Mathf.Max(1f, knockbackMultiplier);
                ServerSend.KnockbackMob(mobID, vector);
                if (hitEffect == 0)
                {
                    hitEffect = 4;
                }
            }
            if (newHP <= 0 && LocalClient.instance.myId == fromClient)
            {
                PlayerStatus.Instance.AddKill(weaponType, mob);
            }
            ServerSend.PlayerHitMob(fromClient, mobID, newHP, hitEffect, pos, weaponType);
            PlayerStatus.WeaponHitType weaponHitType = (PlayerStatus.WeaponHitType)weaponType;
            if (weaponHitType != PlayerStatus.WeaponHitType.Rock && weaponHitType != PlayerStatus.WeaponHitType.Undefined && damage > 0)
            {
                GameManager.instance.onlyRock = false;
            }
            Dictionary<string, int> stats2 = Server.clients[fromClient].player.stats;
            stats2["DamageDone"] = stats2["DamageDone"] + dmg;
            return false;
        }
    }

    [HarmonyPatch(typeof(ServerSend))]
    class ServerSendPatch
    {
        [HarmonyPatch(nameof(ServerSend.PickupItem)), HarmonyPrefix]
        static bool PickupItem(int fromClient, int objectID)
        {
            using (Packet packet = new Packet(19))
            {
                packet.Write(fromClient);
                packet.Write(objectID);
                packet.Write(Mod.instance.PowerUpsPerPickup());
                typeof(ServerSend).GetMethod("SendTCPDataToAll", BindingFlags.NonPublic | BindingFlags.Static, null, new System.Type[] { typeof(int), typeof(Packet) }, null).Invoke(null, new object[] { LocalClient.instance.myId, packet });
            }
            return false;
        }
    }
    
    [HarmonyPatch(typeof(ClientHandle))]
    class ClientHandlePatch
    {
        [HarmonyPatch(nameof(ClientHandle.PickupItem)), HarmonyPrefix]
        static bool PickupItem(Packet packet)
        {
            int fromClient = packet.ReadInt(true);
            int objectID = packet.ReadInt(true);
            int amount = packet.ReadInt(true);
            PowerupInventoryPatch.gain = amount;
            Item component = ItemManager.Instance.list[objectID].GetComponent<Item>();
            if (!component)
            {
                return false;
            }
            if (component.powerup)
            {
                GameManager.instance.powerupsPickedup = true;
            }
            if (LocalClient.instance.myId == fromClient && !LocalClient.serverOwner)
            {
                if (component.item)
                {
                    InventoryUI.Instance.AddItemToInventory(component.item);
                }
                else if (component.powerup)
                {
                    PowerupInventory.Instance.AddPowerup(component.powerup.name, component.powerup.id, objectID);
                }
            }
            if (!LocalClient.serverOwner)
            {
                ItemManager.Instance.PickupItem(objectID);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemManager))]
    class ItemManagerPatch
    {

        [HarmonyPatch("Awake"), HarmonyPostfix]
        static void Awake(ItemManager __instance)
        {
            Mod.instance.LoadIDs();
            PowerupInventoryPatch.gain = 1;
        }

        [HarmonyPatch(nameof(ItemManager.GetRandomPowerup)), HarmonyPrefix]

        // Token: 0x0600043F RID: 1087 RVA: 0x0001684C File Offset: 0x00014A4C
        static bool GetRandomPowerup(ItemManager __instance, ref Powerup __result, float whiteWeight, float blueWeight, float orangeWeight, ref ConsistentRandom ___random)
        {
            float num = whiteWeight + blueWeight + orangeWeight;
            float num2 = (float)___random.NextDouble();

            List<Powerup[]> powerups = new List<Powerup[]>();

            if (num2 < whiteWeight / num)
            {
                powerups.Add(__instance.powerupsWhite);
                powerups.Add(__instance.powerupsBlue);
                powerups.Add(__instance.powerupsOrange);
            }
            else if (num2 < (whiteWeight + blueWeight) / num)
            {
                powerups.Add(__instance.powerupsBlue);
                powerups.Add(__instance.powerupsOrange);
                powerups.Add(__instance.powerupsWhite);
            }
            else
            {
                powerups.Add(__instance.powerupsOrange);
                powerups.Add(__instance.powerupsBlue);
                powerups.Add(__instance.powerupsWhite);
            }
            List<Powerup> enabled = new List<Powerup>();
            foreach (var arr in powerups)
            {
                foreach (Powerup p in arr)
                {
                    if (Mod.instance.IsEnabled(Mod.instance.FromDisplayName(p.name)))
                    {
                        enabled.Add(p);
                    }
                }
                if (enabled.Count > 0) break;
            }
            if (enabled.Count > 0)
                __result = enabled[Random.Range(0, enabled.Count)];
            else
                __result = powerups[0][0];
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerStatus))]
    class PlayerStatusPatch
    {

        [HarmonyPatch("HandleDamage"), HarmonyPrefix]
        static bool HandleDamage(PlayerStatus __instance, int damageTaken, int damageType, bool ignoreProtection, int damageFromPlayer, ref bool ___readyToAdrenalineBoost, ref bool ___readyToRegenShield, ref bool ___dead)
        {
            if (!ignoreProtection)
            {
                damageTaken = (int)typeof(PlayerStatus).GetMethod("OneShotProection", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(__instance, new object[] { damageTaken });
            }
            if (__instance.shield >= damageTaken)
            {
                __instance.shield -= damageTaken;
            }
            else
            {
                damageTaken -= (int)__instance.shield;
                __instance.shield = 0f;
                __instance.hp -= (float)damageTaken;
            }
            if (__instance.hp <= 0f)
            {
                __instance.hp = 0f;
                typeof(PlayerStatus).GetMethod("PlayerDied", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(__instance, new object[] { damageType, damageFromPlayer });
            }
            if (__instance.hp / __instance.maxHp < 0.3f && !__instance.adrenalineBoost && ___readyToAdrenalineBoost)
            {
                Traverse.Create(__instance).Field<bool>("adrenalineBoost").Value = true;
                ___readyToAdrenalineBoost = false;
                __instance.Invoke("StopAdrenaline", PowerupInventoryPatch.AdrenalineDuration());
            }
            ___readyToRegenShield = false;
            __instance.CancelInvoke("RegenShield");
            if (!___dead)
            {
                __instance.Invoke("RegenShield", PowerupInventoryPatch.ShieldRegenDelay());
            }
            float shakeRatio = (float)damageTaken / (float)__instance.MaxHpAndShield();
            CameraShaker.Instance.DamageShake(shakeRatio);
            DamageVignette.Instance.VignetteHit();
            return false;
        }

        [HarmonyPatch("StopAdrenaline"), HarmonyPrefix]
        static bool StopAdrenaline(PlayerStatus __instance)
        {
            Traverse.Create(__instance).Field<bool>("adrenalineBoost").Value = false;
            __instance.Invoke("ReadyAdrenaline", PowerupInventoryPatch.AdrenalineCooldown());
            return false;
        }
    }
}
