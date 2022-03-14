﻿
using Oxide.Core;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using System;
using System.Linq;
using Oxide.Core.Plugins;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using Oxide.Core.Libraries.Covalence;
using Rust;
using Rust.Ai;
using Network;

namespace Oxide.Plugins
{
    [Info("Payback2", "1928Tommygun", "1.0")]
    [Description("Special Admin Commands To Mess With Cheaters")]
    class Payback2 : RustPlugin
    {
        [PluginReference] Plugin Payback;//| reference to the original payback plugin

        //| Hello!

        //| This is Payback 2!  To make sure you can use this plugin on its own, it comes with some essential features from Payback 1!
        //| If you want to have classic admin commands like ROCKETMAN, JAWS, and BSOD, please purchase Payback 1 at https://payback.fragmod.com


        //| TUTORIAL:
        //| admins require the permission payback.admin to use this plugin
        //| this oxide command works for that: oxide.grant group admin payback.admin
        //| put 'payback' in the F1 or server console to see the tutorial!


        //| =========

        //| ===================

        //| ==============================================================================
        //| TOMMYGUN'S EULA - BY USING THIS PLUGIN YOU AGREE TO THE FOLLOWING!
        //| ==============================================================================
        //| 
        //| Code contained in this file is not licensed to be copied, resold, or modified in any way.
        //| You must purchase another copy of the plugin for each server instance you would like to deploy on.  Migration is allowed.
        //| Do not copy any portion of this document.
        //| Do not share this plugin with other server organizations, they much purchase their own licenses.
        //|
        //| =======================================

        //| ===================

        //| =========

        //| 1928TOMMYGUN
        //MMMMMMMMMMMMMMMMMMMMMMMMWNO::loll::kKKXXXXXXXXXXKXXKx;o000000KKKKNWWWWWWWWWWWWWWWWWWMMMMMMWWXxkNNNNN
        //MMMMMMMMMMMMMMMMMMMMMMMMKc,.......................... ...........'',,,,,,,,,,,,,,,,,;;;;;;;;:..,'''.
        //MMMMMMMMMMMMMMMMMMMMMMMMXc',,;;;;;;;;;;;;;;;;;;;,.         ',''',;:::::::::::::clldxxxxxxxxxxooocccc
        //MMMMMMMMMMMMMMNXK00KXKOdc;'',;:;,,;lxddl::,;::;'.',..''   .kWNNNK:...',,........'oXMMMMMMMMMMMMMMMMM
        //MMMMWNX0Oxol:;'.......       :Oc  .xMMMKc....','.;ool,..  .OMMMMNxcdO0KKo.      lNMMMMMMMMMMMMMMMMMM
        //Odlc;'..                     .:,';xNMMWk.     ;xoxXXx'..   cXMMMMMMMMMWO:     .;OMMMMMMMMMMMMMMMMMMM
        //.                     ';:clodxO0XWMMMXo.    .:x000KK0Ol.   .xMMMMMMMW0:.    .:0WWMMMMMMMMMMMMMMMMMMM
        //.                 .,lONMMMMMMMMMMMMMX:     ,0WMMMMMMMMx.cXMMMMMMMK,     'kNWMMMMMMMMMMMMMMMMMMMMMMMM
        //'              .;o0NMMMMMMMMMMMMMMMMk.    .oWMMMMMMMMMx.  .OMMMMMMMMNl    ,OWMMMMMMMMMMMMMMMMMMMMMMM
        //'           .:xKWMMMMMMMMMMMMMMMMMMMNd.  .oNMMMMMMMMMMx.  .OMMMMMMMMMNkl::kWMMMMMMMMMMMMMMMMMMMMMMMM
        //.       .,lkXMMMMMMMMMMMMMMMMMMMMMMMMMXOdxXMMMMMMMMMMMx.  .OMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM
        //.   .,lkKWMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMXOkkONMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM
        //: .l0NMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM



        //| ==============================================================
        //| Definitions
        //| ==============================================================

        public enum Card
        {
            Pacifism = 0,//zeros all outgoing damage from target player
            InstantKarma = 4,//reflects damage back to the player
            Dud = 5,//prevents damage to non-player entities
            Sit = 8,//force player to sit
            HigherGround = 11,//target player is teleported 100m into the air
            NoRest, // No Rest For The Wicked! Force the player to respawn!
            ViewLoot, // View target entities loot
            Hammer, // hammer - gives target player a hammer that will destroy all the entities owned by the hammer's target
            Bag, // Bag - Print all players that have bagged target player in, and print all players that have been bagged.  Include "discord" after the command to log the results to discord
            Woods, // Woods - If you go down to the woods today, be ready for big surprise!
            PotatoMode, // PotatoMode - Target's frame rate will decrease dramatically over time - Warning, this is designed to eventually crash Rust
        }

        Dictionary<Card, string> descriptions = new Dictionary<Card, string>() {
            {Card.Dud, "target player deals no damage to NON-PLAYER entities.  Also prevents farming / tool use" },
            {Card.InstantKarma, "target player deals no damage to enemies and 35% of the damage is reflected back to them" },
            {Card.Pacifism, "target player deals no player damage to non-teammates; add 'silent' to not send a message about it to other players." },
            {Card.Sit, "spawns a chair in front of you and forces the cheater to sit.  Doesn't let them get up and will place them back in if they die." },
            {Card.HigherGround, "target player is teleported 100m into the air" },
            {Card.NoRest, "No Rest For The Wicked! Force the player to respawn when they die!" },
            {Card.ViewLoot, "View target player's loot" },
            {Card.Hammer, "Gives admin a hammer that will destroy all the entities owned by the hammer's target.  Add -noloot to also delete the loot" },
            {Card.Woods, "Target will get attacked by a bear if he's farming wood or stone.  Add -landmine to strap a bunch of landmines to the bear's face" },
            {Card.PotatoMode, "Target's frame rate will decrease dramatically over time. -- *Warning, this is designed to crash Rust*" },
            {Card.Bag, "Print all players that have bagged target player in, and print all players that have been bagged.  Include \"discord\" after the command to log the results to discord" },
        };

        Dictionary<string, Card> cardAliases = new Dictionary<string, Card>() {
            { "dud", Card.Dud },
            { "in", Card.InstantKarma},
            { "pf", Card.Pacifism},
            { "hg", Card.HigherGround},
            { "nr", Card.NoRest},
            { "res", Card.NoRest},
            { "loot", Card.ViewLoot},
            { "ham", Card.Hammer},
            { "bg", Card.Bag},
            { "bear", Card.Woods},
            { "potato", Card.PotatoMode},
        };



        //| ==============================================================
        //| Giving
        //| ==============================================================
        public void GiveCard(ulong userID, Card card, string[] args = null, BasePlayer admin = null)
        {
            HashSet<Card> cards;
            if (!cardMap.TryGetValue(userID, out cards))
            {
                cards = new HashSet<Card>();
                cardMap[userID] = cards;
            }
            cards.Add(card);
            //Puts($"Payback card {card} given to {userID}");

            BasePlayer player = BasePlayer.FindByID(userID);
            if (player != null)
            {

                if (card == Card.Sit)
                {
                    DoSitCommand(player, admin);
                }
                else if (card == Card.HigherGround)
                {
                    DoHigherGround(player);
                }
                else if (card == Card.Pacifism)
                {
                    silentPacifism = false;
                    if (args != null)
                    {
                        if (args.Contains("silent"))
                        {
                            silentPacifism = true;
                        }
                    }
                } else if (card == Card.NoRest)
                {
                    if (player.IsDead())
                    {
                        player.Respawn();
                    }
                } else if (card == Card.ViewLoot)
                {
                    ViewTargetPlayerInventory(player, admin);
                    TakeCard(player, Card.ViewLoot);
                } else if (card == Card.Hammer)
                {
                    GiveAdminHammer(player);
                    if (args.Contains("noloot"))
                    {
                        flag_kill_no_loot = true;
                        PrintToPlayer(admin, $"Hammer set to remove loot!");
                    } else
                    {
                        flag_kill_no_loot = false;
                    }
                } else if (card == Card.Woods)
                {
                    if (args.Contains("landmine"))
                    {
                        woodsHasLandmines.Add(player.userID);
                    } else
                    {
                        woodsHasLandmines.Remove(player.userID);
                    }
                } else if (card == Card.PotatoMode)
                {
                    DoPotato(player, admin, args);
                }
                
            }


        }

        bool silentPacifism = false;


        //| ==============================================================
        //| COMMAND Implementation
        //| ==============================================================
        string guid_potato = "guid_potato";
        Dictionary<ulong, int> currentFrameMap = new Dictionary<ulong, int>();
        Dictionary<ulong, Coroutine> potatoCoRoutines = new Dictionary<ulong, Coroutine>();
        void DoPotato(BasePlayer player, BasePlayer admin = null, string[] args = null, bool doRemove = false)
        {
            Coroutine routine;
            potatoCoRoutines.TryGetValue(player.userID, out routine);


            if (doRemove)
            {
                if (routine != null)
                {
                    Worker.GetSingleton().StopCoroutine(routine);
                    return;
                }
                //if (args.Contains("add")) { 
                //    //| contiue to add to the lag
                //} else
                //{
                //    int existing = 0;
                //    currentFrameMap.TryGetValue(player.userID, out existing);
                //    for (int i = 0; i <= existing; i ++)
                //    {
                //        CuiHelper.DestroyUi(player, guid_BSOD + i);
                //    }

                //    currentFrameMap.Remove(player.userID);
                //    CuiHelper.DestroyUi(player, guid_BSOD);
                //    return;
                //}
            }

            //if (!currentFrameMap.ContainsKey(player.userID))
            //{
            //    UI2.guids.Add(guid_BSOD);
            //    var elements = new CuiElementContainer();
            //    UI2.CreatePanel(elements, "Under", guid_BSOD, "1 1 1 0", UI2.vectorFullscreen, null, false, 0, 0, false);
            //    CuiHelper.AddUi(player, elements);
            //}


            UI2.guids.Add(guid_potato);
            var elements = new CuiElementContainer();
            UI2.CreatePanel(elements, "Under", guid_potato, "1 1 1 0", UI2.vectorFullscreen, null, false, 0, 0, false);
            CuiHelper.AddUi(player, elements);

            routine = Worker.StaticStartCoroutine(DoPotatoCo(player, args));
            potatoCoRoutines[player.userID] = routine;
        }
        IEnumerator DoPotatoCo(BasePlayer player, string[] args)
        {


            int batch = 10;

            //int totalFrames = 600;
            int currentFrames = 0;

            float rate = 100 / 1f;

            //if (args.Contains("crash"))
            //{
            //    intensity = 5;
            //    totalFrames = 5000;
            //}

            float ts = Time.realtimeSinceStartup;
            float startTime = ts;
            float elapsed;

            float currentRate;

            while (player != null && player.net != null &&  player.net.connection != null && player.net.connection.connected && HasCard(player.userID, Card.PotatoMode))
            {

                elapsed = Time.realtimeSinceStartup - startTime + Mathf.Epsilon;
                currentRate = currentFrames / elapsed;

                if (currentRate > rate)
                {
                    //Puts($"Paused: Current Frames: {currentFrames} rate {currentRate}");
                    yield return null;
                } else
                {
                    for (int j = 0; j < batch; j++)
                    {
                        UI2.guids.Add(guid_potato + currentFrames);

                        var elements = new CuiElementContainer();

                        UI2.CreatePanel(elements, guid_potato, guid_potato + currentFrames, "1 1 1 0", UI2.vectorFullscreen, null, false, 0, 0, false);

                        CuiHelper.AddUi(player, elements);

                        currentFrames++;

                    }
                }

                //Puts($"Current Frames: {currentFrames} rate {currentRate}");

                //| Ensure we take a break at least every 1/30s
                if (Time.realtimeSinceStartup - ts > 1/30f)
                {
                    ts = Time.realtimeSinceStartup;
                    yield return null;
                }

            }
        }



        Dictionary<ulong, float> woodsTimestamps = new Dictionary<ulong, float>();
        HashSet<ulong> woodsHasLandmines = new HashSet<ulong>();
        void DoWoods(BasePlayer target)
        {
            float ts = 0;
            woodsTimestamps.TryGetValue(target.userID, out ts);
            if (Time.realtimeSinceStartup - ts < 15)
            {
                return;
            }
            woodsTimestamps[target.userID] = Time.realtimeSinceStartup;
            //TakeCard(target, Card.Woods);

            int layermask = 1 << 15 | 1 << 16 | 1 << 17 | 1 << 23 | 1 << 27 | 1 << 8 | 1 << 21 | 1 << 12 | 1 << 0 | 1 << 30;

            bool foundSpot = false;
            int iterations = 0;
            while (!foundSpot && iterations < 100)
            {
                var pos = target.transform.position + target.eyes.HeadRay().direction * -1 * 20;
                pos += Vector3.up * 100;
                var ray = new Ray(pos, Vector3.down);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 1000, layermask))
                {
                    //var hits = Physics.SphereCastAll(hit.point, 1, Vector3.up);
                    var collliders = Physics.OverlapSphere(hit.point, 1);
                    bool tooClose = false;
                    if (collliders != null)
                    {
                        foreach (var c in collliders)
                        {

                            var ent = c.gameObject.GetComponent<BaseEntity>();
                            if (ent != null)
                            {
                                tooClose = true;
                            }
                        }
                    }

                    if (!tooClose)
                    {
                        foundSpot = true;
                        if (woodsHasLandmines.Contains(target.userID))
                        {
                            Worker.StaticStartCoroutine(AnimalAttackCo(target, hit.point + Vector3.up, new string[] { "bear", "landmine" }));
                        }
                        else
                        {
                            Worker.StaticStartCoroutine(AnimalAttackCo(target, hit.point + Vector3.up, new string[] { "bear"}));
                        }
                    }
                }
                iterations++;
            }
        }

        HashSet<BaseEntity> animals = new HashSet<BaseEntity>();
        HashSet<BaseEntity> noDamageEntities = new HashSet<BaseEntity>();

        IEnumerator AnimalAttackCo(BasePlayer player, Vector3 spawnposition, string[] args)
        {
            //Ray ray = new Ray(UnityEngine.Random.Range(-5f, 5f) * Vector3.forward + UnityEngine.Random.Range(-5f, 5f) * Vector3.left + spawnposition + Vector3.up * 20, Vector3.down);
            //Ray ray = new Ray(UnityEngine.Random.Range(-5f, 5f) * Vector3.forward + UnityEngine.Random.Range(-5f, 5f) * Vector3.left + spawnposition + Vector3.up * 20, Vector3.down);
            //if (Physics.Raycast(ray, out hit))
            //{
            string aiPrefab = "assets/rust.ai/agents/chicken/chicken.prefab";
            if (args.Contains("bear"))
            {
                aiPrefab = "assets/rust.ai/agents/bear/bear.prefab";
            }
            else if (args.Contains("boar"))
            {
                aiPrefab = "assets/rust.ai/agents/boar/boar.prefab";

            }
            else if (args.Contains("wolf"))
            {
                aiPrefab = "assets/rust.ai/agents/wolf/wolf.prefab";

            }
            else if (args.Contains("stag"))
            {
                aiPrefab = "assets/rust.ai/agents/stag/stag.prefab";
            }
            //assets/rust.ai/agents/wolf/wolf.prefab
            var entity = GameManager.server.CreateEntity(aiPrefab, spawnposition + Vector3.up * 0.2f);
            //var entity = GameManager.server.CreateEntity("assets/rust.ai/agents/wolf/wolf.prefab", hit.point + Vector3.up * 0.2f);
            //var entity = GameManager.server.CreateEntity("assets/rust.ai/agents/bear/bear.prefab", hit.point + Vector3.up * 0.2f);

            BaseAnimalNPC npc = entity as BaseAnimalNPC;
            entity.Spawn();

            var stats = npc.Stats;

            npc.AttackRange = 3;
            HashSet<BaseEntity> forceNetworkUpdates = new HashSet<BaseEntity>();

            if (args.Contains("landmine"))
            {
                for (int i = 0; i < 5; i ++)
                {
                    var mine = GameManager.server.CreateEntity("assets/prefabs/deployable/landmine/landmine.prefab", npc.transform.position + Vector3.up * 0.6f + npc.transform.forward * (1.3f - 0.1f * i));
                    Landmine landmine = mine as Landmine;
                    mine.Spawn();
                    landmine.Arm();
                    landmine.SendNetworkUpdateImmediate();
                    mine.transform.LookAt(npc.transform.position + npc.transform.up * 100);
                    mine.SetParent(entity, true);
                    noDamageEntities.Add(mine);
                    forceNetworkUpdates.Add(mine);
                }
                npc.AttackRange = 0.01f;
                stats.Speed *= 2.4f;
                npc.TargetSpeed *= 2.4f;
            }

            //chicken.Stats.Speed = 20;
            //npc.Stats.Speed = 200;
            stats.TurnSpeed = 100;
            //npc.Stats.Acceleration = 50;
            //npc.AttackDamage *= 2;
            stats.VisionRange = 300;

            animals.Add(npc);

            npc.AttackTarget = player;
            npc.ChaseTransform = player.transform;


            stats.AggressionRange = 100000;
            stats.DeaggroRange = 100000;
            stats.IsAfraidOf = new BaseNpc.AiStatistics.FamilyEnum[0];
            npc.Destination = player.transform.position;

            stats.VisionCone = -1;

            npc.Stats = stats;

            yield return new WaitForSeconds(0.25f);
            //chicken.LegacyNavigation = true;
            //chicken.Stats.DistanceVisibility = AnimationCurve.Linear(0, 0, 1, 1);
            npc.LegacyNavigation = true;

            bool doLoop = true;
            while (doLoop)
            {

                foreach (var ent in forceNetworkUpdates)
                {
                    if (ent != null)
                    {
                        if (ent.net.group.ID != npc.net.group.ID)
                        {
                            ent.net.SwitchGroup(npc.net.group);
                            ent.SendNetworkGroupChange();
                        }
                    }
                }

                if (npc != null && player != null)
                {
                    if (player.IsDead())
                    {
                        if (npc != null)
                        {
                            npc.Kill();
                        }
                        doLoop = false;
                    }
                    else
                    {
                        if (npc.NavAgent != null && npc.NavAgent.isOnNavMesh)
                        {
                            npc.ChaseTransform = player.transform;
                            npc.AttackTarget = player;
                            npc.Destination = player.transform.position;
                            npc.TargetSpeed = npc.Stats.Speed;

                        }
                    }
                    //Puts($"Attack target: {chicken.AttackTarget} Chase: {chicken.ChaseTransform} ARate: {chicken.AttackRate} CombatTarget: {chicken.CombatTarget}");
                    //chicken.TickNavigation();

                }
                else
                {
                    if (npc != null)
                    {
                        npc.Kill();
                    }
                    doLoop = false;
                }
                yield return null;
                //yield return new WaitForSeconds(0.25f);
            }


            timer.Once(120, () => {
                if (npc != null)
                {
                    npc.Kill();
                }
            });

            timer.Once(130f, () => {
                animals.RemoveWhere(x => x == null);
            });
            //}
        }

        #region OxideHooks
        //| ==============================================================
        //| OXIDE HOOKS
        //| ==============================================================
        private object OnPlayerViolation(BasePlayer player, AntiHackType type)
        {
            if (type == AntiHackType.InsideTerrain && HasAnyCard(player.userID)) return false;
            return null;
        }

        object OnPlayerDeath(BasePlayer player, HitInfo hitinfo)
        {
            if (HasAnyCard(player.userID))
            {

                if (player.isMounted)
                {
                    player.GetMounted().DismountPlayer(player, true);
                    //player.DismountObject();//for some reason this was required
                }

                if (HasCard(player.userID, Card.NoRest))
                {
                    timer.Once(3f, () => {
                        if (player != null)
                        {
                            if (player.IsDead())
                            {
                                player.Respawn();
                            }
                        }
                    });
                }
            }

            return null;
        }



        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null && HasCard(player.userID, Card.Sit))
            {
                TakeCard(player, Card.Sit);
            }
        }

        private void OnEntityTakeDamage(BaseEntity entity, HitInfo hitinfo)
        {
            if (entity == null || hitinfo == null) return;
            if (cardMap.Count == 0) return;//early out for maximum perf


            if (hitinfo != null)
            {

                if (sitChairMap.Values.Contains(entity))
                {
                    hitinfo.damageTypes.Clear();
                    hitinfo.DoHitEffects = false;
                }

                var player = entity as BasePlayer;
                var attacker = hitinfo.InitiatorPlayer;


                if (hitinfo.Initiator != null && noDamageEntities.Contains(hitinfo.Initiator))
                {
                    if (player != null && HasAnyCard(player.userID) || entity is BaseNpc) {
                        //| damage ok
                    } else
                    {
                        //| no damage
                        hitinfo.damageTypes.Clear();
                        hitinfo.DoHitEffects = false;
                    }
                }

                if (attacker != null && HasAnyCard(attacker.userID))
                {
                    var members = GetPlayerTeam(attacker.userID);
                    members.Remove(attacker.userID);

                    bool friendlyFire = false;
                    if (player != null)
                    {
                        friendlyFire = members.Contains(player.userID);
                    }

                    bool isSuicide = hitinfo.damageTypes.GetMajorityDamageType() == Rust.DamageType.Suicide;


                    if (player != null && attacker != null && attacker != player)
                    {

                        if (HasCard(attacker.userID, Card.InstantKarma))
                        {

                            if (!friendlyFire)
                            {

                                float newHealth = attacker.health - hitinfo.damageTypes.Total() * 0.35f;
                                if (newHealth < 5)
                                {
                                    attacker.Die();
                                }
                                else
                                {
                                    attacker.SetHealth(newHealth);
                                    attacker.metabolism.SendChangesToClient();
                                    attacker.SendNetworkUpdateImmediate();
                                    //PlaySound("assets/bundled/prefabs/fx/headshot.prefab", attacker, false);
                                    PlaySound("assets/bundled/prefabs/fx/headshot_2d.prefab", attacker, true);
                                }

                                hitinfo.damageTypes.Clear();
                                hitinfo.DoHitEffects = false;

                            }

                        }

                    }

                    if (HasCard(attacker.userID, Card.Pacifism) && attacker != player && player != null)
                    {

                        if (!friendlyFire)
                        {
                            hitinfo.damageTypes.Clear();
                            hitinfo.DoHitEffects = false;

                            if (config.notifyCheaterAttacking && !silentPacifism)
                            {
                                SendPlayerLimitedMessage(player.userID, $"You are being attacked by [{UI2.ColorText(attacker.displayName, "yellow")}] a known cheater!\n{UI2.ColorText("Tommygun's Payback Plugin", "#7A2E30")} has prevented all damage to you.");
                            }
                            //Puts($"{player.displayName} attacked by [{attacker.displayName}] a known cheater! Tommygun's Payback has prevented all damage from the cheater");

                        }

                    }

                    //
                    if (attacker != null && HasCard(attacker.userID, Card.Woods))
                    {
                        if (hitinfo.CanGather)
                        {
                            DoWoods(attacker);
                        }
                    }

                    //prevent damage to non-player entities
                    if (HasCard(attacker.userID, Card.Dud) && player == null)
                    {
                        hitinfo.damageTypes.Clear();
                        //hitinfo.DoHitEffects = false;
                        hitinfo.gatherScale = 0;
                    }


                }



            }
        }


        #endregion

        #region PaybackIO
        //| ==============================================================
        //| INPUT OUTPUT FUNCTIONALITY
        //| ==============================================================

        Dictionary<ulong, float> playerMessageTimestamps = new Dictionary<ulong, float>();
        void SendPlayerLimitedMessage(ulong userID, string message, float rate = 5)
        {
            float ts = float.NegativeInfinity;
            if (playerMessageTimestamps.TryGetValue(userID, out ts))
            {
                if (Time.realtimeSinceStartup - ts > rate)
                {
                    ts = Time.realtimeSinceStartup;
                    playerMessageTimestamps[userID] = ts;
                    SendReply(BasePlayer.FindByID(userID), message);
                }
            }
            else
            {
                playerMessageTimestamps[userID] = ts;
                SendReply(BasePlayer.FindByID(userID), message);
            }
        }


        void AdminCommandToggleCard(BasePlayer admin, Card card, string[] args)
        {

            //| Special Commands
            if (card == Card.Bag)
            {
                ulong userID;
                if (!ulong.TryParse(args[0], out userID))
                {
                    PrintToPlayer(admin, "usage: /bag <steamid>");
                    return;
                }
                DoBagSearch(userID, args, admin);
            }

            //| Requires target commands
            if (args.Length == 0 && admin != null)
            {

                var entity = RaycastFirstEntity(admin.eyes.HeadRay(), 100);
                if (entity is BasePlayer)
                {
                    var targetPlayer = entity as BasePlayer;
                    AdminToggleCard(admin, targetPlayer, card, args);
                }
                else
                {
                    //raycast target in front of you
                    //SendReply(admin, "did not find player from head raycast, either look at your target or do /<cardname> <playername>");
                    PrintToPlayer(admin, "did not find player from head raycast, either look at your target or do /<cardname> <playername>");
                }

                return;
            }

            if (args.Length >= 1)
            {
                var targetPlayer = GetPlayerWithName(args[0]);
                if (targetPlayer != null)
                {

                    if (args.Length == 2 && args[1] == "team")
                    {

                        var members = GetPlayerTeam(targetPlayer.userID);

                        string teamMatesPrintout = "";
                        foreach (var member in members)
                        {
                            BasePlayer p = BasePlayer.FindByID(member);
                            if (p != null && p.IsConnected)
                            {
                                teamMatesPrintout += p.displayName + " ";
                            }
                        }
                        PrintToPlayer(admin, $"Giving {card} to team {targetPlayer.displayName}  - {members.Count} team mates: {teamMatesPrintout}");

                        foreach (var member in members)
                        {
                            BasePlayer p = BasePlayer.FindByID(member);
                            if (p != null && p.IsConnected)
                            {
                                AdminToggleCard(admin, p, card, args);
                            }

                        }

                    }
                    else
                    {
                        AdminToggleCard(admin, targetPlayer, card, args);
                    }
                }
                else
                {

                    ulong userID;
                    if (ulong.TryParse(args[0], out userID))
                    {
                        targetPlayer = BasePlayer.FindByID(userID);
                        if (targetPlayer != null)
                        {

                            if (args.Length == 2 && args[1] == "team")
                            {

                                var members = GetPlayerTeam(targetPlayer.userID);
                                PrintToPlayer(admin, $"Giving {card} to team {targetPlayer.displayName} has {members.Count} team mates");
                                foreach (var member in members)
                                {
                                    BasePlayer p = BasePlayer.FindByID(member);
                                    if (p != null && p.IsConnected)
                                    {
                                        AdminToggleCard(admin, p, card, args);
                                    }

                                }

                            }
                            else
                            {
                                AdminToggleCard(admin, targetPlayer, card, args);
                            }


                            return;
                        }
                        else
                        {

                        }

                    }
                    else
                    {

                    }

                    PrintToPlayer(admin, $"could not find player : {args[0]}");
                }
            }
        }
        void AdminToggleCard(BasePlayer admin, BasePlayer targetPlayer, Card card, string[] args)
        {
            if (HasCard(targetPlayer.userID, card))
            {
                TakeCard(targetPlayer.userID, card, args, admin);
                PrintToPlayer(admin, $"Removed {card} from {targetPlayer.displayName}");
            }
            else
            {
                GiveCard(targetPlayer.userID, card, args, admin);
                PrintToPlayer(admin, $"Gave {card} to {targetPlayer.displayName}");
            }
        }



        [ConsoleCommand("payback2")]
        void Console_Payback(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player != null) {
                if (!IsAdmin(player)) return;
            }
            CommandPayback(player, "", arg.Args);
        }

        [ChatCommand("payback2")]
        void ChatCommandPayback(BasePlayer player, string cmd, string[] args)
        {
            if (!IsAdmin(player)) return;
            SendReply(player, "Check Payback2 output in F1 console!");
            CommandPayback(player, cmd, args);
        }
        void CommandPayback(BasePlayer player, string cmd, string[] args)
        {
            if (player != null && !IsAdmin(player)) return;
            // list all cards

            if (args == null || args.Length == 0)
            {
                DoPaybackPrintout(player, args);
                return;
            }

            List<string> argsList = new List<string>(args);
            if (argsList.FirstOrDefault(x => x == "show") != null)
            {
                string output = "Active Cards:\n";
                // show all active cards and players
                foreach (var userid in cardMap.Keys)
                {
                    var targetPlayer = BasePlayer.FindByID(userid);
                    string playername = "";
                    if (targetPlayer != null)
                    {
                        playername = targetPlayer.displayName;
                    }
                    HashSet<Card> cards = cardMap[userid];
                    output += $"{userid} : {playername}\n";
                    foreach (var card in cards)
                    {
                        output += $"\n{card.ToString()} : {UI2.ColorText(descriptions[card], "white")}";
                    }
                    output += "\n\n";
                }
                PrintToPlayer(player, output);

            }

            if (argsList.FirstOrDefault(x => x == "clear") != null)
            {

                foreach (var userid in new List<ulong>(cardMap.Keys))
                {
                    var targetPlayer = BasePlayer.FindByID(userid);
                    string playername = "";
                    if (targetPlayer != null)
                    {
                        playername = targetPlayer.displayName;
                    }

                    if (player != null)
                    {
                        HashSet<Card> cards = cardMap[userid];
                        foreach (var card in new HashSet<Card>(cards))
                        {
                            TakeCard(player, card);
                        }
                    }

                }

                cardMap.Clear();
                PrintToPlayer(player, "removed all cards from all players");
            }

        }

        const string PAYBACK_VERSION = "Payback2";
        void DoPaybackPrintout(BasePlayer player, string[] args)
        {


            Dictionary<Card, List<string>> cardToAliases = new Dictionary<Card, List<string>>();
            foreach (var alias in cardAliases.Keys)
            {
                Card c = cardAliases[alias];
                List<string> aliases;
                if (!cardToAliases.TryGetValue(c, out aliases))
                {
                    aliases = new List<string>();
                    cardToAliases[c] = aliases;
                }
                aliases.Add(alias);
            }

            var cards = Enum.GetValues(typeof(Card));
            string output = "";

            output += "\n" + "Add \"team\" after a command to apply the effect to target player's team as well as them.  Example: /bear <steamid> team";
            ////output += "\n" + "/setdroppercent <1-100>% to change the chance butterfingers would drop";
            output += "\n" + $"admins require the permisison {permission_admin} to use these commands!";
            output += "\n" + $"use '/{PAYBACK_VERSION} show' to see which players have which cards";
            output += "\n" + $"use '/{PAYBACK_VERSION} clear' to remove all cards from all players.";
            output += "\n" + $"It is NOT necessary to remove effects from players when finished.";
            //output += "\n" + $"Whitelist temp banned players with: bancheckexception <id>";

            output += "\n\nPayback Cards:";

            foreach (Card card in cards)
            {
                string desc;
                descriptions.TryGetValue(card, out desc);

                List<string> aliases = cardToAliases[card];
                string aliasesTogether = "";
                aliases.ForEach(x => aliasesTogether += $"[ {UI2.ColorText(x, "yellow")} ] ");


                output += "\n\n" + $"{aliasesTogether}: { UI2.ColorText(desc, "white")}";
            }

            if (Payback == null)
            {
                output += "\n\n " + UI2.ColorText("Payback (the original) not detected, did you know there's even more Payback available at https://payback.fragmod.com?", "white");
            }

            PrintToPlayer(player, output);
        }

        //| ==============================================================
        //| PAYBACK OPTIONS
        //| ==============================================================

        Dictionary<ulong, HashSet<Card>> cardMap = new Dictionary<ulong, HashSet<Card>>();
        public bool HasAnyCard(ulong userID)
        {
            HashSet<Card> cards = null;
            if (cardMap.TryGetValue(userID, out cards))
            {
                if (cards.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }
        public bool HasCard(ulong userID, Card card)
        {
            HashSet<Card> cards;
            if (cardMap.TryGetValue(userID, out cards))
            {
                if (cards.Contains(card))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public void TakeCard(BasePlayer player, Card card, string[] args = null, BasePlayer admin = null)
        {
            TakeCard(player.userID, card, args, admin);
        }
        public void TakeCard(ulong userID, Card card, string[] args = null, BasePlayer admin = null)
        {
            HashSet<Card> cards;
            if (!cardMap.TryGetValue(userID, out cards))
            {
                cards = new HashSet<Card>();
                cardMap[userID] = cards;
            }
            cards.Remove(card);

            var player = BasePlayer.FindByID(userID);

            if (cards.Count == 0)
            {
                cardMap.Remove(userID);
            }
            else if (card == Card.Sit)
            {
                if (player != null)
                {
                    DoSitCommand(player, admin);
                }
            } else if (card == Card.PotatoMode)
            {
                DoPotato(player, null, args, true);
            }
        }

        #endregion

        #region DiscordEmbeds
        void SendToDiscordWebhook(Dictionary<string, string> messageData, string title = "TEMP GAME BAN DETECTED")
        {
            if (config.webhooks == null || config.webhooks.Count == 0)
            {
                Puts($"Could not send Discord Webhook: webhook not configured");
                return;
            }

            string discordEmbedTitle = title;


            List<object> fields = new List<object>();

            foreach (var key in messageData.Keys)
            {
                string data = messageData[key];
                fields.Add(new { name = $"{key}", value = $"{data}", inline = false });
            }

            object f = fields.ToArray();


            foreach (var webhook in config.webhooks)
            {
                SendWebhook(webhook, (string)discordEmbedTitle, f);
            }
        }

        private void SendWebhook(string WebhookUrl, string title, object fields)
        {
            if (string.IsNullOrEmpty(WebhookUrl))
            {
                Puts("Error: Someone tried to use a command but the WebhookUrl is not set!");
                return;
            }

            //test
            string json = new SendEmbedMessage(13964554, title, fields).ToJson();

            webrequest.Enqueue(WebhookUrl, json, (code, response) =>
            {
                if (code == 429)
                {
                    Puts("Sending too many requests, please wait");
                    return;
                }

                if (code != 204)
                {
                    Puts(code.ToString());
                }
                if (code == 400)
                {
                    Puts(response + "\n\n" + json);
                }
            }, this, Oxide.Core.Libraries.RequestMethod.POST, new Dictionary<string, string> { ["Content-Type"] = "application/json" });
        }

        private class SendEmbedMessage
        {
            public SendEmbedMessage(int EmbedColour, string discordMessage, object _fields)
            {
                object embed = new[]
                {
                    new
                    {
                        title = discordMessage,
                        fields = _fields,
                        color = EmbedColour,
                        thumbnail = new Dictionary<object, object>() { { "url", "https://i.imgur.com/ruy7N2Z.png" } },
                    }
                };
                Embeds = embed;
            }

            [JsonProperty("embeds")] public object Embeds { get; set; }

            public string ToJson() => JsonConvert.SerializeObject(this);
        }
        #endregion

        #region Initialize
        
        //| ==============================================================
        //| INIT
        //| ==============================================================
        void Initialize()
        {
            Unsubscribe("OnPlayerVoice");
            Unsubscribe($"OnEntityKill");

            timer.Once(0.1f, () => {

                LoadData();

                permission.RegisterPermission(permission_admin, this);

                var cards = Enum.GetValues(typeof(Card));

                foreach (Card card in cards)
                {
                    cardAliases[card.ToString().ToLower()] = card;
                }
                foreach (var alias in cardAliases.Keys)
                {
                    //| Payback1 will handle all commands it can if it exists.
                    if (Payback != null)
                    {
                        if (cardsInPayback1.Contains(cardAliases[alias]))
                        {
                            continue;
                        }
                    }

                    //| add commands for this version
                    cmd.AddChatCommand(alias, this, nameof(GenericChatCommand));
                    cmd.AddConsoleCommand(alias, this, nameof(GenericConsoleCommand));
                }
            });
            
        }
        void GenericChatCommand(BasePlayer player, string cmd, string[] args)
        {
            if (!IsAdmin(player)) return;
            string argsTogether = "";
            foreach (var arg in args)
            {
                argsTogether += arg + " ";
            }
            //SendReply(player, $"cmd: {cmd} args {argsTogether}");
            Card card;
            if (cardAliases.TryGetValue(cmd.ToLower(), out card))
            {
                AdminCommandToggleCard(player, card, args);
            }
        }
        void GenericConsoleCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;

            if (player != null) {
                if (!IsAdmin(player)) return;
            }
            if (arg == null) return;
            if (arg.cmd == null) return;

            string argsTogether = "";
            foreach (var param in arg.Args)
            {
                argsTogether += param + " ";
            }

            string cmd = arg.cmd.Name;

            Card card;
            if (cardAliases.TryGetValue(cmd.ToLower(), out card))
            {
                AdminCommandToggleCard(player, card, arg.Args);
            }
        }
        void OnServerInitialized(bool serverIsNOTinitialized)
        {
            bool serverHasInitialized = !serverIsNOTinitialized;
            Initialize();
        }

        #endregion

        #region ViewInventoryCommands

        //| ==============================================================
        //| ViewInventory - Copied from Whispers88 and modified here
        //| ==============================================================
        private static List<string> _viewInventoryHooks = new List<string> { "OnLootEntityEnd", "CanMoveItem", "OnEntityDeath" };

        void ViewTargetPlayerInventory(BasePlayer target, BasePlayer admin)
        {
            if (admin == null) return;
            if (admin.IsSpectating())
            {
                PrintToPlayer(admin, $"{UI2.ColorText($"[PAYBACK WARNING] ", "yellow") } : {UI2.ColorText($"cannot open target's inventory while spectating! you must respawn", "white")}");
                return;
            }
            PrintToPlayer(admin, $"{UI2.ColorText($"[PAYBACK WARNING] ", "yellow") } : {UI2.ColorText($"you must exit the F1 console immediately after using the command to view inventory", "white")}");

            ViewInvCmd(admin.IPlayer, "ViewInvCmd", new string[] { $"{target.userID}" });
        }

        private void ViewInvCmd(IPlayer iplayer, string command, string[] args)
        {
            BasePlayer player = iplayer.Object as BasePlayer;
            if (player == null) return;

            //if (!HasPerm(player.UserIDString, permission_admin))
            //{
            //    ChatMessage(iplayer, GetLang("NoPerms"));
            //    return;
            //}


            if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
            {
                RaycastHit hitinfo;
                if (!Physics.Raycast(player.eyes.HeadRay(), out hitinfo, 3f, (int)Layers.Server.Players))
                {
                    ChatMessage(iplayer, "NoPlayersFoundRayCast");
                    return;
                }
                BasePlayer targetplayerhit = hitinfo.GetEntity().ToPlayer();
                if (targetplayerhit == null)
                {
                    ChatMessage(iplayer, "NoPlayersFoundRayCast");
                    return;
                }
                //ChatMessage(iplayer, "ViewingPLayer", targetplayerhit.displayName);
                ViewInventory(player, targetplayerhit);
                return;
            }
            IPlayer target = FindPlayer(args[0]);
            if (target == null)
            {
                //ChatMessage(iplayer, "NoPlayersFound", args[0]);
                return;
            }
            BasePlayer targetplayer = target.Object as BasePlayer;
            if (targetplayer == null)
            {
                //ChatMessage(iplayer, "NoPlayersFound", args[0]);
                return;
            }
            //ChatMessage(iplayer, "ViewingPLayer", targetplayer.displayName);
            ViewInventory(player, targetplayer);
        }

        #endregion Commands

        #region Methods
        private List<LootableCorpse> _viewingcorpse = new List<LootableCorpse>();
        private void ViewInventory(BasePlayer player, BasePlayer targetplayer)
        {
            if (_viewingcorpse.Count == 0)
                SubscribeToHooks();

            player.EndLooting();

            var corpse = GetLootableCorpse(targetplayer.displayName);
            corpse.SendAsSnapshot(player.Connection);

            timer.Once(1f, () =>
            {
                StartLooting(player, targetplayer, corpse);
            });
        }

        LootableCorpse GetLootableCorpse(string title = "")
        {
            LootableCorpse corpse = GameManager.server.CreateEntity(StringPool.Get(2604534927), Vector3.zero) as LootableCorpse;
            corpse.CancelInvoke("RemoveCorpse");
            corpse.syncPosition = false;
            corpse.limitNetworking = true;
            //corpse.playerName = targetplayer.displayName;
            corpse.playerName = title;
            corpse.playerSteamID = 0;
            corpse.enableSaving = false;
            corpse.Spawn();
            corpse.SetFlag(BaseEntity.Flags.Locked, true);
            Buoyancy bouyancy;
            if (corpse.TryGetComponent<Buoyancy>(out bouyancy))
            {
                UnityEngine.Object.Destroy(bouyancy);
            }
            Rigidbody ridgidbody;
            if (corpse.TryGetComponent<Rigidbody>(out ridgidbody))
            {
                UnityEngine.Object.Destroy(ridgidbody);
            }
            return corpse;
        }

        private void StartLooting(BasePlayer player, BasePlayer targetplayer, LootableCorpse corpse)
        {
            player.inventory.loot.AddContainer(targetplayer.inventory.containerMain);
            player.inventory.loot.AddContainer(targetplayer.inventory.containerWear);
            player.inventory.loot.AddContainer(targetplayer.inventory.containerBelt);
            player.inventory.loot.entitySource = corpse;
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.MarkDirty();
            player.inventory.loot.SendImmediate();
            player.ClientRPCPlayer<string>(null, player, "RPC_OpenLootPanel", "player_corpse");
            _viewingcorpse.Add(corpse);
        }
        private void StartLootingContainer(BasePlayer player, ItemContainer container, LootableCorpse corpse) {
            player.inventory.loot.AddContainer(container);
            player.inventory.loot.entitySource = corpse;
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.MarkDirty();
            player.inventory.loot.SendImmediate();
            player.ClientRPCPlayer<string>(null, player, "RPC_OpenLootPanel", "player_corpse");
            _viewingcorpse.Add(corpse);
        }

        #endregion Methods

        #region Hooks
        private void OnLootEntityEnd(BasePlayer player, LootableCorpse corpse)
        {
            if (!_viewingcorpse.Contains(corpse)) return;

            _viewingcorpse.Remove(corpse);
            if (corpse != null)
                corpse.Kill();

            if (_viewingcorpse.Count == 0)
                UnSubscribeFromHooks();

        }


        void OnEntityDeath(LootableCorpse corpse, HitInfo info)
        {
            if (!_viewingcorpse.Contains(corpse)) return;
            _viewingcorpse.Remove(corpse);
            if (corpse != null)
                corpse.Kill();
            if (_viewingcorpse.Count == 0)
                UnSubscribeFromHooks();
        }
        #endregion Hooks

        #region Helpers

        private IPlayer FindPlayer(string nameOrId)
        {
            return BasePlayer.activePlayerList.FirstOrDefault(x => x.UserIDString == nameOrId || x.displayName.Contains(nameOrId)).IPlayer;
        }

        private bool HasPerm(string id, string perm) => permission.UserHasPermission(id, perm);

        private string GetLang(string langKey, string playerId = null, params object[] args) => string.Format(lang.GetMessage(langKey, this, playerId), args);
        private void ChatMessage(IPlayer player, string langKey, params object[] args)
        {
            if (player.IsConnected) player.Message(GetLang(langKey, player.Id, args));
        }

        private void UnSubscribeFromHooks()
        {
            foreach (var hook in _viewInventoryHooks)
                Unsubscribe(hook);
        }

        private void SubscribeToHooks()
        {
            foreach (var hook in _viewInventoryHooks)
                Subscribe(hook);
        }
        #endregion

        #region UTILITIES


        //| ==============================================================
        //| UTILITIES
        //| ==============================================================
        float Random()
        {
            return UnityEngine.Random.Range(0f, 1f);
        }
        string TryGetDisplayName(ulong userID)
        {
            return covalence.Players.FindPlayerById(userID.ToString())?.Name;
        }


        public BasePlayer GetPlayerWithName(string displayName)
        {
            foreach (var p in BasePlayer.allPlayerList)
            {
                if (p.displayName.ToLower().Contains(displayName.ToLower()))
                {
                    return p;
                }
            }
            return null;
        }
        BaseEntity RaycastFirstEntity(Ray ray, float distance)
        {
            RaycastHit hit;
            if (Physics.Raycast(ray.origin, ray.direction, out hit, distance))
            {
                return hit.GetEntity();
            }
            return null;
        }

        void SetDespawnDuration(DroppedItem dropped, float seconds)
        {
            dropped.Invoke(new Action(dropped.IdleDestroy), seconds);//prevent dropped item from despawn
        }
        void DestroyGroundCheck(BaseEntity entity)
        {
            GameObject.DestroyImmediate(entity.GetComponentInChildren<DestroyOnGroundMissing>());
            GameObject.DestroyImmediate(entity.GetComponentInChildren<GroundWatch>());
        }

        [ChatCommand("sound")]
        void SoundCommand(BasePlayer player, string cmd, string[] args)
        {
            if (!IsAdmin(player)) return;

            if (args.Length == 0)
            {
                SendReply(player, "/sound <asset>");
                return;
            }
            for (int i = 0; i < args.Length; i++)
            {
                string sound = args[i];
                PlaySound(sound, player, false);
            }
        }

        void PrintToPlayer(BasePlayer player, string text)
        {
            if (player == null) {
                Puts($"{text}");
                return;
            }
            //SendReply(player, text);
            player.SendConsoleCommand($"echo {text}");
        }
        public HashSet<ulong> GetPlayerTeam(ulong userID)
        {
            BasePlayer player = BasePlayer.FindByID(userID);

            RelationshipManager.PlayerTeam existingTeam = RelationshipManager.ServerInstance.FindPlayersTeam(player.userID);
            if (existingTeam != null)
            {
                return new HashSet<ulong>(existingTeam.members);
            }
            return new HashSet<ulong>() { userID };
        }

        public void PlaySound(List<string> effects, BasePlayer player, Vector3 worldPosition, bool playlocal = true)
        {
            if (player == null) return;//ai
            foreach (var effect in effects)
            {
                //var sound = new Effect(effect, player, 0, localPosition, localPosition.normalized);
                var sound = new Effect(effect, worldPosition, Vector3.up);
                if (playlocal)
                {
                    EffectNetwork.Send(sound, player.net.connection);
                }
                else
                {
                    EffectNetwork.Send(sound);
                }
            }
        }

        public void PlaySound(List<string> effects, BasePlayer player, bool playlocal = true)
        {
            if (player == null) return;//ai
            foreach (var effect in effects)
            {
                var sound = new Effect(effect, player, 0, Vector3.zero + Vector3.up * 0.5f, Vector3.forward);
                if (playlocal)
                {
                    EffectNetwork.Send(sound, player.net.connection);
                }
                else
                {
                    EffectNetwork.Send(sound);
                }
            }
        }
        public void PlaySound(string effect, ListHashSet<BasePlayer> players, bool playlocal = true)
        {
            //all players
            foreach (var player in players)
            {
                PlaySound(effect, player, playlocal);
            }
        }

        bool test = false;

        public void PlaySound(string effect, BasePlayer player, bool playlocal = true, Vector3 posLocal = default(Vector3))
        {
            if (player == null) return;//ai

            var sound = new Effect(effect, player, 0, Vector3.zero, Vector3.forward);
            
            if (posLocal != Vector3.zero)
            {
                sound = new Effect(effect, player.transform.position + posLocal, Vector3.forward);
            }


            if (playlocal)
            {
                EffectNetwork.Send(sound, player.net.connection);
            }
            else
            {
                EffectNetwork.Send(sound);
            }
        }

        public void PlayGesture(BasePlayer target, string gestureName, bool canCancel = false)
        {
            if (target == null) return;
            if (target.gestureList == null) return;
            var gesture = target.gestureList.StringToGesture(gestureName);
            if (gesture == null) {
                return;
            }
            bool saveCanCancel = gesture.canCancel;
            gesture.canCancel = canCancel;
            target.SendMessage("Server_StartGesture", gesture);
            gesture.canCancel = saveCanCancel;
        }

        public class Worker : MonoBehaviour
        {
            public static Worker GetSingleton()
            {
                if (_singleton == null)
                {
                    GameObject worker = new GameObject();
                    worker.name = "Worker Singleton";
                    _singleton = worker.AddComponent<Worker>();
                }
                return _singleton;
            }
            static Worker _singleton;
            public static Coroutine StaticStartCoroutine(IEnumerator c)
            {
                return Worker.GetSingleton().StartCoroutine(c);
            }

        }
        #endregion

        #region ESSENTIALPAYBACK
        //| ==============================================================
        //| ESSENTIAL PAYBACK FUNCTIONS
        //| ==============================================================

        HashSet<BaseNetworkable> entitiesWatchingForKilledMounts = new HashSet<BaseNetworkable>();

        public const string invisibleChairPrefab = "assets/bundled/prefabs/static/chair.invisible.static.prefab";

        HashSet<BaseMountable> chairsPreventingDismount = new HashSet<BaseMountable>();


        //| normal chair or secretlabs
        //string chairPrefab = "assets/prefabs/deployable/chair/chair.deployed.prefab";
        string chairPrefab = "assets/prefabs/deployable/secretlab chair/secretlabchair.deployed.prefab";

        Dictionary<ulong, BaseEntity> sitChairMap = new Dictionary<ulong, BaseEntity>();

        BaseEntity InvisibleSit(BasePlayer targetPlayer)
        {
            var chair = GameManager.server.CreateEntity(invisibleChairPrefab, targetPlayer.transform.position);
            var mount = chair as BaseMountable;
            chair.Spawn();

            chairsPreventingDismount.Add(mount);

            GameObject.DestroyImmediate(chair.GetComponentInChildren<DestroyOnGroundMissing>());
            GameObject.DestroyImmediate(chair.GetComponentInChildren<GroundWatch>());

            if (targetPlayer.isMounted)
            {
                targetPlayer.DismountObject();
            }

            Timer t = null;
            t = timer.Every(0.25f, () => {
                if (chair == null || chair.IsDestroyed)
                {
                    t.Destroy();
                    return;
                }
                if (targetPlayer != null)
                {
                    if (!targetPlayer.isMounted)
                    {
                        targetPlayer.Teleport(chair.transform.position);
                        mount.MountPlayer(targetPlayer);
                        chair.SendNetworkUpdateImmediate();
                    }
                }
                else
                {
                    //Puts("Attempted to mount player to chair, but they were null!");
                    chair.Kill();
                    t.Destroy();
                }

            });
            return chair;
        }


        void DoBagSearch(ulong userID, string[] args, BasePlayer admin = null)
        {
            if (userID == 0) return;
            TakeCard(userID, Card.Bag);

            if (args.Contains("discord"))
            {
                Worker.StaticStartCoroutine(BagSearchCo(userID, true, admin));
            }
            else
            {
                Worker.StaticStartCoroutine(BagSearchCo(userID, false, admin));
            }
        }
        IEnumerator BagSearchCo(ulong userID, bool logToDiscord = false, BasePlayer admin = null)
        {
            yield return null;


            float timestamp = Time.realtimeSinceStartup;
            float maxTimeBetweenFrames = 1 / 20f;

            //| Get bags owned by player

            var allBags = BaseNetworkable.serverEntities.OfType<SleepingBag>();
            //var deployedByTargetBags = new List<SleepingBag>();

            var useridsBaggedByTarget = new HashSet<ulong>();
            var useridsWhoBaggedTarget = new HashSet<ulong>();

            //find the bags that target placed
            foreach (var bag in allBags)
            {
                //| ==============================================================
                if (Time.realtimeSinceStartup - timestamp > maxTimeBetweenFrames)
                {
                    yield return null;
                    timestamp = Time.realtimeSinceStartup;
                }
                //| ==============================================================

                ulong ownerid = 0;
                var creator = bag.creatorEntity;
                if (creator != null)
                {
                    var player = creator as BasePlayer;
                    if (player != null)
                    {
                        ownerid = player.userID;
                    }
                }
                else
                {
                    ownerid = bag.OwnerID;
                }

                //target bagged someone else
                if (ownerid == userID && bag.deployerUserID != userID)
                {
                    //deployedByTargetBags.Add(bag);
                    useridsBaggedByTarget.Add(bag.deployerUserID);
                }

                //someone bagged in target
                if (userID == bag.deployerUserID && ownerid != userID)
                {
                    useridsWhoBaggedTarget.Add(ownerid);
                }
            }

            var messageData = new Dictionary<string, string>();
            string targetInfo = $"{TryGetDisplayName(userID)}";
            string baggedByString = "";
            string output = $"Players bagged by {targetInfo}:";
            foreach (var userid in useridsBaggedByTarget)
            {
                var displayname = TryGetDisplayName(userid);
                output += $"\n{userid} : {displayname}";

                baggedByString += $"{userid} : {displayname}\n";
            }
            if (baggedByString.Length > 0)
            {
                messageData.Add($"Players bagged by {targetInfo}", baggedByString);
            }
            else
            {
                messageData.Add($"Players bagged by {targetInfo}", "none");
            }

            output += $"\nSteamids who bagged in {targetInfo}:";
            string baggedInString = "";
            foreach (var userid in useridsWhoBaggedTarget)
            {
                var displayname = TryGetDisplayName(userid);
                output += $"\n{userid} : {displayname}";
                baggedInString += $"\n{userid} : {displayname}";
            }
            if (baggedInString.Length > 0)
            {
                messageData.Add($"Players who bagged in {targetInfo}", baggedInString);
            }
            else
            {
                messageData.Add($"Players who bagged in {targetInfo}", "none");
            }

            PrintToPlayer(admin, $"{output}");

            if (logToDiscord)
            {
                SendToDiscordWebhook(messageData, $"Bag Search [{userID}]");
            }

        }

        bool flag_kill_no_loot = false;

        void GiveAdminHammer(BasePlayer admin)
        {
            if (admin == null) return;
            var item = ItemManager.CreateByName("hammer", 1, 2375073548);
            if (item != null)
            {
                GiveItemOrDrop(admin, item, false);
            }
        }
        object OnStructureRepair(BaseCombatEntity entity, BasePlayer player)
        {

            if (HasCard(player.userID, Card.Hammer))
            {
                Worker.StaticStartCoroutine(DeleteByCo(entity.OwnerID, player.transform.position, player));
            }
            return null;
        }

        IEnumerator DeleteByCo(ulong steamid, Vector3 position, BasePlayer admin = null)
        {
            yield return null;
            if (steamid == 0UL)
            {
                yield break;
            }


            float maxTimeBetweenFrames = 1 / 60f;
            int maxEntitiesPerFrame = 1;
            float delayBetweenFrames = 1 / 20f;
            float timestamp = Time.realtimeSinceStartup;

            var entities = new List<BaseNetworkable>(BaseNetworkable.serverEntities);

            float fxTimestamp = Time.realtimeSinceStartup;
            float fxCooldown = 0.75f;
            //float fxCooldown = 0.2f;

            var ownedEntities = new List<BaseEntity>();
            foreach (var x in entities)
            {
                var entity = x as BaseEntity;
                if (!(entity == null) && entity.OwnerID == steamid)
                {
                    ownedEntities.Add(entity);
                }
                if (Time.realtimeSinceStartup - timestamp > maxTimeBetweenFrames)
                {
                    yield return null;
                    timestamp = Time.realtimeSinceStartup;
                }
            }

            ownedEntities.Sort((x, y) => Vector3.Distance(x.transform.position, position).CompareTo(Vector3.Distance(y.transform.position, position)));

            timestamp = Time.realtimeSinceStartup;

            int i = 0;

            int count = 0;

            bool playSound = true;

            if (admin != null)
                PlaySound("assets/bundled/prefabs/fx/headshot.prefab", admin, false);

            Vector3 lastPosition = Vector3.zero;


            //| LOOT REMOVAL PASS
            if (flag_kill_no_loot)
            {
                foreach (var baseEntity in ownedEntities)
                {
                    var storage = baseEntity as StorageContainer;
                    if (storage != null)
                    {
                        foreach (var item in new List<Item>(storage.inventory.itemList))
                        {
                            //PrintToPlayer(admin, $"Removing: {item.info.displayName.english}");
                            item.GetHeldEntity()?.KillMessage();
                            //item.DoRemove();
                            //item.Remove();
                            ItemManager.RemoveItem(item);
                        }
                        ItemManager.DoRemoves();
                        //storage.inventory.Clear();
                    }
                }
            }


            while (i < ownedEntities.Count)
            {
                if (Time.realtimeSinceStartup - timestamp > maxTimeBetweenFrames || count >= maxEntitiesPerFrame)
                {
                    yield return new WaitForSeconds(delayBetweenFrames);
                    timestamp = Time.realtimeSinceStartup;
                    count = 0;
                }

                var baseEntity = ownedEntities[i];
                if (!(baseEntity == null) && baseEntity.OwnerID == steamid)
                {

                    if (admin != null && playSound)
                    {
                        if (Time.realtimeSinceStartup - fxTimestamp > fxCooldown)
                        {
                            PlaySound("assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab", admin, true);
                            fxTimestamp = Time.realtimeSinceStartup;
                        }
                    }
                    lastPosition = baseEntity.transform.position;

                    baseEntity.Kill(BaseNetworkable.DestroyMode.Gib);

                    count++;
                }
                i++;

            }
            if (admin != null)
            {
                PlaySound("assets/prefabs/locks/keypad/effects/lock.code.lock.prefab", admin, true);

                timer.Once(0.75f, () => {
                    PlaySound("assets/prefabs/npc/autoturret/effects/targetacquired.prefab", admin, true);

                    if (!flag_kill_no_loot)
                    {
                        var effect = GameManager.server.CreateEntity("assets/prefabs/deployable/fireworks/mortarred.prefab", lastPosition);
                        effect.Spawn();
                        var firework = effect as BaseFirework;
                        firework.fuseLength = 0;
                        firework.Ignite();
                    }

                });
            }
            yield return null;
        }
        void ResolveConflictingCommands(BasePlayer player, BasePlayer admin = null)
        {
            bool hasSit = false;
            if (HasCard(player.userID, Card.Sit))
            {
                hasSit = true;
                TakeCard(player, Card.Sit);
            }

            if (player.isMounted)
            {
                player.GetMounted().DismountPlayer(player, true);
                var car = player.GetMountedVehicle();
                if (car != null)
                {
                    car.Kill(BaseNetworkable.DestroyMode.Gib);
                }
            }
            if (Payback != null)
            {
                ConsoleSystem.Run(ConsoleSystem.Option.Server, $"payback clear", new object[0]);
            }
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            if (HasCard(player.userID, Card.NoRest))
            {
                player.EndSleeping();
                player.SendNetworkUpdate();
            }
        }

        void OnEntityKill(BaseNetworkable entity, HitInfo info)
        {

            if (entity == null) return;
            if (entitiesWatchingForKilledMounts.Contains(entity))
            {
                var chair = entity.GetComponentInChildren<BaseMountable>();
                if (chair.IsMounted())
                {
                    var player = chair.GetMounted();
                    player.DismountObject();
                    player.Teleport(chair.transform.position);
                    player.Die();
                }
                entitiesWatchingForKilledMounts.Remove(entity);

                timer.Once(0.5f, () => {
                    if (entitiesWatchingForKilledMounts.Count == 0)
                        Unsubscribe($"OnEntityKill");
                });
            }
        }

        void GiveItemOrDrop(BasePlayer player, Item item, bool stack = false)
        {
            bool success = item.MoveToContainer(player.inventory.containerBelt, -1, stack);
            if (!success)
            {
                success = item.MoveToContainer(player.inventory.containerMain, -1, stack);
            }
            if (!success)
            {
                success = item.MoveToContainer(player.inventory.containerWear, -1, stack);
            }
            if (!success)
            {
                item.Drop(player.transform.position + Vector3.up, Vector3.zero);
            }
        }
        void DoHigherGround(BasePlayer player)
        {
            player.Teleport(player.transform.position + Vector3.up * 100);
            TakeCard(player, Card.HigherGround);
        }


        void DoSitCommand(BasePlayer targetPlayer, BasePlayer adminPlayer)
        {
            if (targetPlayer == null) return;

            if (HasCard(targetPlayer.userID, Card.Sit))
            {
                if (adminPlayer == null) return;

                if (targetPlayer.isMounted)
                {
                    var car = targetPlayer.GetMountedVehicle();
                    if (car != null)
                    {
                        car.Kill(BaseNetworkable.DestroyMode.Gib);
                    }

                    BaseEntity chair = null;
                    if (sitChairMap.TryGetValue(targetPlayer.userID, out chair))
                    {
                        chair?.Kill();
                    }
                    targetPlayer.DismountObject();
                }

                RaycastHit hitinfo;
                if (Physics.Raycast(adminPlayer.eyes.HeadRay(), out hitinfo, 50))
                {


                    var chair = GameManager.server.CreateEntity(chairPrefab, hitinfo.point);
                    var mount = chair as BaseMountable;
                    chair.Spawn();
                    sitChairMap[targetPlayer.userID] = chair;
                    //targetPlayer.Teleport(chair.transform.position + chair.transform.forward * 0.5f);
                    targetPlayer.EndSleeping();

                    GameObject.DestroyImmediate(chair.GetComponentInChildren<DestroyOnGroundMissing>());
                    GameObject.DestroyImmediate(chair.GetComponentInChildren<GroundWatch>());

                    Vector3 lookAtPosition = adminPlayer.transform.position;
                    lookAtPosition.y = mount.transform.position.y;

                    timer.Once(0.25f, () => {

                        if (targetPlayer != null)
                        {
                            mount.MountPlayer(targetPlayer);


                            chair.transform.LookAt(lookAtPosition);
                            chair.SendNetworkUpdateImmediate();

                            Worker.StaticStartCoroutine(SitCo(targetPlayer));
                        }
                        else
                        {
                            //Puts("Attempted to mount player to chair, but they were null!");
                            chair.Kill();
                        }

                    });

                }

            }
            else
            {
                BaseEntity chair = null;
                if (sitChairMap.TryGetValue(targetPlayer.userID, out chair))
                {
                    if (chair != null)
                    {
                        chair.Kill();
                    }
                }

            }
        }

        IEnumerator SitCo(BasePlayer player)
        {
            yield return new WaitForSeconds(0.25f);
            BaseEntity chair;
            sitChairMap.TryGetValue(player.userID, out chair);
            BaseMountable mount = chair as BaseMountable;

            while (player != null && chair != null && HasCard(player.userID, Card.Sit))
            {
                if (player != null)
                {
                    if (player.IsSleeping())
                    {
                        player.EndSleeping();
                    }
                    if (player.isMounted)
                    {
                        var playerMount = player.GetMounted();
                        if (playerMount != mount)
                        {
                            player.DismountObject();
                            //PrintToChat($"Dismount player for sit: {playerMount}");

                        }
                    }

                    var dist = Vector3.Distance(chair.transform.position, player.transform.position);
                    if (dist > 2)
                    {
                        player.Teleport(chair.transform.position + chair.transform.forward * 0.5f);
                        //yield return new WaitForSeconds(1);
                    }
                    if (!player.isMounted && dist < 2)
                    {

                        //mount.AttemptMount(player, false);
                        player.MountObject(mount);

                        //PrintToChat($"Attempt mount: {mount} pmount:  {player.GetMounted()}");
                        //yield return new WaitForSeconds(0.25f);
                    }

                }
                else
                {
                    chair.Kill();
                }
                yield return new WaitForSeconds(0.25f);
            }
            if (chair != null)
            {
                chair.Kill();
            }

        }

        object CanDismountEntity(BasePlayer player, BaseMountable entity)
        {
            if (cardMap.Count == 0 && chairsPreventingDismount.Count == 0) return null;//early out for maximum perf

            if (HasCard(player.userID, Card.Sit))
            {
                return false;
            }

            //cleanup dead chairs
            foreach (var chair in new HashSet<BaseMountable>(chairsPreventingDismount))
            {
                if (chair == null || chair.IsDestroyed)
                {
                    chairsPreventingDismount.Remove(chair);
                }
            }

            if (chairsPreventingDismount.Contains(entity))
            {
                return false;
            }

            return null;
        }

        public HashSet<Card> cardsInPayback1 = new HashSet<Card>() {
            Card.Pacifism,
            Card.InstantKarma,
            Card.Dud,
            Card.Sit,
            Card.HigherGround,
            Card.NoRest,
            Card.ViewLoot,
            Card.Hammer,
            Card.Bag,
        };
        #endregion

        #region Config

        private void Init()
        {
            LoadConfig();
        }

        private PluginConfig config;

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<PluginConfig>();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig
            {

            };
            SaveConfig();
        }

        private class PluginConfig
        {

            [JsonProperty("These discord webhooks will get notified. Dont forget the [\"\"] Format: \"webhooks\" : [\"hook\"],")]
            public List<string> webhooks = new List<string>();

            [JsonProperty("Notify player when attacked by cheater")]
            public bool notifyCheaterAttacking = true;
        }



        #endregion Config

        #region Data
        //| ==============================================================
        //| DATA
        //| ==============================================================


        string filename_data {
            get
            {
                return $"{PAYBACK_VERSION}/{PAYBACK_VERSION}.dat";
            }
        }


        DynamicConfigFile file_payback_data;

        public PaybackData paybackData = new PaybackData();

        public class PaybackData
        {

        }

        void Unload()
        {
            //Puts("Unload Tommygun's Payback");

            Worker.GetSingleton()?.StopAllCoroutines();
            GameObject.Destroy(Worker.GetSingleton());

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                UI2.ClearUI(player);
            }
            foreach (var npc in animals)
            {
                npc?.Kill();
            }
            foreach (var ent in noDamageEntities)
            {
                ent?.Kill();
            }

            SaveData();
        }



        private void SaveData()
        {
            //| WRITE SERVER FILE
            file_payback_data.WriteObject(paybackData);
        }
        private void LoadData()
        {
            //Puts("Load Data");

            ReadDataIntoDynamicConfigFiles();
            LoadFromDynamicConfigFiles();
        }
        void ReadDataIntoDynamicConfigFiles()
        {
            file_payback_data = Interface.Oxide.DataFileSystem.GetFile(filename_data);
        }
        void LoadFromDynamicConfigFiles()
        {
            try
            {
                paybackData = file_payback_data.ReadObject<PaybackData>();
            }
            catch (Exception e)
            {
                paybackData = new PaybackData();
                //Puts($"Creating new data {e}");
            }

        }


        public const string permission_admin = "payback.admin";

        public bool IsAdmin(BasePlayer player)
        {
            if (permission.UserHasPermission(player.Connection.userid.ToString(), permission_admin))
            {
                return true;
            }
            return false;
        }
        #endregion

        #region UICODE

        //| ===================

        //| =======================================
        //| TOMMYGUN'S PROPRIETARY UI CLASSES
        //| =======================================
        //| 
        //| Code contained below this line is not licensed to be used, copied, or modified.
        //| 
        //| 
        //| =======================================

        //| ===================
        public class UI2
        {
            public static Vector4 vectorFullscreen = new Vector4(0, 0, 1, 1);

            public static string ColorText(string input, string color)
            {
                return "<color=" + color + ">" + input + "</color>";
            }

            public static void ClearUI(BasePlayer player)
            {
                foreach (var guid in UI2.guids)
                {
                    CuiHelper.DestroyUi(player, guid);
                }
            }

            //| =============================
            //| DIRT 
            //| =============================
            public static Dictionary<ulong, HashSet<string>> dirtyMap = new Dictionary<ulong, HashSet<string>>();
            public static HashSet<string> GetDirtyBitsForPlayer(BasePlayer player)
            {
                if (player == null) return new HashSet<string>();
                if (!dirtyMap.ContainsKey(player.userID))
                {
                    dirtyMap[player.userID] = new HashSet<string>();
                }
                return dirtyMap[player.userID];
            }

            //| =============================
            //| LAYOUT 
            //| =============================

            public class Layout
            {

                public Vector2 startPosition;

                public Vector4 cellBounds;
                public Vector2 padding;
                public Vector4 cursor;
                public int maxRows;

                public int row = 0;
                public int col = 0;

                public void Init(Vector2 _startPosition, Vector4 _cellBounds, int _maxRows, Vector2 _padding = default(Vector2))
                {
                    startPosition = _startPosition;
                    cellBounds = _cellBounds;
                    maxRows = _maxRows;
                    padding = _padding;
                    row = 0;
                    col = 0;
                }

                public void NextCell(System.Action<Vector4, int, int> populateAction)
                {
                    float cellX = startPosition.x + (col * (cellBounds.z + padding.x)) + padding.x / 2f;
                    float cellY = startPosition.y - (row * (cellBounds.w + padding.y)) - cellBounds.w - padding.y;

                    cursor = new Vector4(cellX, cellY, cellX, cellY);

                    populateAction(cursor, row, col);

                    //move to next element
                    row++;
                    if (row == maxRows)
                    {
                        row = 0;
                        col++;
                    }

                }

                public void Reset()
                {
                    row = 0;
                    col = 0;
                }
            }



            //| =============================
            //| COLOR FUNCTIONS
            //| =============================

            public static string ColorToHex(Color color)
            {
                return ColorUtility.ToHtmlStringRGB(color);
            }
            public static string HexToRGBAString(string hex)
            {
                Color color = Color.white;
                ColorUtility.TryParseHtmlString("#" + hex, out color);
                string c = $"{String.Format("{0:0.000}", color.r)} {String.Format("{0:0.000}", color.g)} {String.Format("{0:0.000}", color.b)} {String.Format("{0:0.000}", color.a)}";
                return c;
            }


            //| =============================
            //| RECT FUNCTIONS
            //| =============================
            public static Vector4 GetOffsetVector4(Vector2 offset)
            {
                return new Vector4(offset.x, offset.y, offset.x, offset.y);
            }
            public static Vector4 GetOffsetVector4(float x, float y)
            {
                return new Vector4(x, y, x, y);
            }

            public static Vector4 SubtractPadding(Vector4 input, float padding)
            {
                float verticalPadding = GetSquareFromWidth(padding);
                return new Vector4(input.x + padding / 2f, verticalPadding / 2f, input.z - padding / 2f, input.w - verticalPadding / 2f);
            }

            public static float GetSquareFromWidth(float width, float aspect = 16f / 9f)
            {
                //return width * 1f / aspect;
                return width * aspect;
            }
            public static float GetSquareFromHeight(float height, float aspect = 16f / 9f)
            {
                //return height * aspect;
                return height * 1f / aspect;
            }

            //specify the screen-space x1, x2, y1 and it will populate y2
            public static Vector4 MakeSquareFromWidth(Vector4 bounds, float aspect = 16f / 9f)
            {
                return new Vector4(bounds.x, bounds.y, bounds.z, bounds.y + GetSquareFromWidth(bounds.z - bounds.x));
            }
            //specify the screen-space x1, y1, and y2 and it will populate the x2
            public static Vector4 MakeSquareFromHeight(Vector4 bounds, float aspect = 16f / 9f)
            {
                return new Vector4(bounds.x, bounds.y, bounds.x + GetSquareFromHeight(bounds.z - bounds.y), bounds.w);
            }
            //make any sized rect from x1, x2, and y1
            public static Vector4 MakeRectFromWidth(Vector4 bounds, float ratio, float aspect = 16f / 9f)
            {
                Vector4 square = MakeSquareFromWidth(bounds, aspect);
                return new Vector4(square.x, square.y, square.z, square.y + (square.w - square.y) * ratio);
            }
            //make any sized rect from y1, y2 and x1
            public static Vector4 MakeRectFromHeight(Vector4 bounds, float ratio, float aspect = 16f / 9f)
            {
                Vector4 square = MakeSquareFromHeight(bounds, aspect);
                return new Vector4(square.x, square.y, square.x + (square.z - square.x) * ratio, square.w);
            }


            //| =============================
            //| UI PANELS
            //| =============================
            public static HashSet<string> guids = new HashSet<string>();

            public static string GetMinUI(Vector4 panelPosition)
            {
                return panelPosition.x.ToString("0.####") + " " + panelPosition.y.ToString("0.####");
            }
            public static string GetMaxUI(Vector4 panelPosition)
            {
                return panelPosition.z.ToString("0.####") + " " + panelPosition.w.ToString("0.####");
            }
            public static string GetColorString(Vector4 color)
            {
                return color.x.ToString("0.####") + " " + color.y.ToString("0.####") + " " + color.z.ToString("0.####") + " " + color.w.ToString("0.####");
            }
            public static CuiElement CreateInputField(CuiElementContainer container, string parent, string panelName, string message, int textSize, string color, Vector4 bounds, string command)
            {

                CuiElement element = new CuiElement
                {
                    Name = panelName,
                    Parent = parent,
                    Components = {
                        new CuiInputFieldComponent {
                            Align = TextAnchor.MiddleLeft,
                            Color = color,
                            Command = command,
							//Text = message,
							FontSize = textSize,
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = GetMinUI(bounds),
                            AnchorMax = GetMaxUI(bounds),
                        }
                    }
                };
                container.Add(element
                );

                return element;
            }

            public static void CreateOutlineLabel(CuiElementContainer container, string parent, string panelName, string message, string color, int size, Vector4 bounds, TextAnchor textAlignment = TextAnchor.MiddleCenter, float fadeOut = 0, float fadeIn = 0, string outlineColor = "0 0 0 0.8", string outlineDistance = "0.7 -0.7")
            {

                container.Add(new CuiElement
                {
                    Name = panelName,
                    Parent = parent,
                    FadeOut = fadeOut,
                    Components = {

                        new CuiTextComponent {
                            Align = textAlignment,
                            Color = color,
                            FadeIn = fadeIn,
                            FontSize = size,
                            Text = message
                        },
                        new CuiOutlineComponent {
                            Color = outlineColor,
                            Distance = outlineDistance,
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = GetMinUI(bounds),
                            AnchorMax = GetMaxUI(bounds),
                        }
                    }
                });
            }

            public static void CreateLabel(CuiElementContainer container, string parent, string panelName, string message, string color, int size, string aMin, string aMax, TextAnchor textAlignment = TextAnchor.MiddleCenter, float fadeIn = 0, float fadeOut = 0)
            {


                CuiLabel label = new CuiLabel();
                label.Text.Text = message;
                label.RectTransform.AnchorMin = aMin;
                label.RectTransform.AnchorMax = aMax;
                label.Text.Align = textAlignment;
                label.Text.Color = color;
                label.Text.FontSize = size;
                label.Text.FadeIn = fadeIn;
                label.FadeOut = fadeOut;

                container.Add(label, parent, panelName);

            }
            public static CuiButton CreateButton(CuiElementContainer container, string parent, string panelName, string color, string text, int size, Vector4 bounds, string command, TextAnchor align = TextAnchor.MiddleCenter, string textColor = "1 1 1 1")
            {

                container.Add(new CuiElement
                {
                    Name = panelName,
                    Parent = parent,
                    Components = {


                            new CuiButtonComponent {
                                Color = color,
                                Command = command,
                            },

                            new CuiRectTransformComponent
                            {
                                AnchorMin = GetMinUI(bounds),
                                AnchorMax = GetMaxUI(bounds),
                            }
                        }
                });

                CreateOutlineLabel(container, panelName, "text", text, textColor, size, new Vector4(0, 0, 1, 1), align);

                return null;

            }


            public static CuiPanel CreatePanel(CuiElementContainer container, string parent, string panelName, string color, Vector4 bounds, string imageUrl = "", bool cursor = false, float fadeOut = 0, float fadeIn = 0, bool png = false, bool blur = false, bool outline = true)
            {

                if (!string.IsNullOrEmpty(imageUrl))
                {
                    //hack to get images working
                    if (png)
                    {
                        if (outline)
                        {
                            container.Add(new CuiElement
                            {
                                Name = panelName,
                                Parent = parent,
                                FadeOut = fadeOut,
                                Components = {
																
								//new CuiRawImageComponent { Color = "0 0 0 0.5", Sprite = "assets/content/materials/highlight.png", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },

								new CuiRawImageComponent
                                {
                                    Color = color,
                                    Png = imageUrl,
                                    FadeIn = fadeIn
                                },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = GetMinUI(bounds),
                                    AnchorMax = GetMaxUI(bounds),
                                },
                                new CuiOutlineComponent {
                                    Color = "0 0 0 0.9",
                                    Distance = "0.7 -0.7",
                                },
                            }
                            });
                        }
                        else
                        {
                            container.Add(new CuiElement
                            {
                                Name = panelName,
                                Parent = parent,
                                FadeOut = fadeOut,
                                Components = {
																
								//new CuiRawImageComponent { Color = "0 0 0 0.5", Sprite = "assets/content/materials/highlight.png", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },

								new CuiRawImageComponent
                                {
                                    Color = color,
                                    Png = imageUrl,
                                    FadeIn = fadeIn
                                },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = GetMinUI(bounds),
                                    AnchorMax = GetMaxUI(bounds),
                                }
                            }
                            });
                        }


                    }
                    else
                    {
                        container.Add(new CuiElement
                        {
                            Name = panelName,
                            Parent = parent,
                            FadeOut = fadeOut,
                            Components = {


                                new CuiRawImageComponent
                                {
                                    Color = color,
                                    Url = imageUrl,
                                    FadeIn = fadeIn
                                },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = GetMinUI(bounds),
                                    AnchorMax = GetMaxUI(bounds),
                                }
                            }
                        });
                    }


                    return null;

                }
                else
                {

                    if (blur)
                    {

                        //BLURS
                        //assets/content/ui/uibackgroundblur-ingamemenu.mat
                        //assets/content/ui/uibackgroundblur-notice.mat
                        //assets/content/ui/uibackgroundblur.mat
                        // dirty bg blur, can't stretch large
                        string mat = "assets/content/ui/uibackgroundblur-ingamemenu.mat";// MEDIUM BLURRY 
                                                                                         //string mat = "assets/content/ui/uibackgroundblur.mat";//VERY BLURRY

                        //string sprite = "assets/content/ui/ui.white.tga";//kind of boxy outline
                        //string sprite = "assets/content/ui/ui.white.tga";//


                        container.Add(new CuiElement
                        {
                            Name = panelName,
                            Parent = parent,
                            FadeOut = fadeOut,
                            Components = {
                                    new CuiImageComponent {
                                        Color = color,
                                        Material = mat,
                                        FadeIn = fadeIn
                                    },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = GetMinUI(bounds),
                                        AnchorMax = GetMaxUI(bounds),
                                    }
                                }
                        });

                    }
                    else
                    {

                        CuiPanel element = new CuiPanel();
                        element.RectTransform.AnchorMin = GetMinUI(bounds);
                        element.RectTransform.AnchorMax = GetMaxUI(bounds);
                        //element.FadeOut = 1f;
                        element.Image.Color = color;
                        element.CursorEnabled = cursor;
                        element.Image.FadeIn = fadeIn;
                        element.FadeOut = fadeOut;

                        container.Add(element, parent, panelName);
                        return element;

                    }

                    return null;

                }

            }

        }

        #endregion
    }
}
