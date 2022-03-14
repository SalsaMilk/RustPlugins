using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oxide.Game.Rust.Cui;
using System.Globalization;
using System;
using Network;
using Network.Visibility;
using Oxide.Core.Plugins;
using System.Text;

namespace Oxide.Plugins
{
    [Info("BoogieBomb", "Cameron", "2.0.1")]
    [Description("Why kill enemys when you can make them dance!")]

    public class BoogieBomb : CovalencePlugin
    {

        uint boogieSkin = 2400277081;
        bool allGrenades = false;
        float bombRadius = 25.0f;
        float timeToLoop = 6.0f;
        HashSet<TimedExplosive> setBoogieBombs = new HashSet<TimedExplosive>();
        GameObjectRef bounceEffect;

        bool useLegacy = false;
        int radioToUse = 2;
        private void Init()
        {
            permission.RegisterPermission("BoogieBomb.use", this);
            permission.RegisterPermission("BoogieBomb.me", this);
            permission.RegisterPermission("BoogieBomb.target", this);

            

            boogieSkin = uint.Parse(Config["SkinForBoogieBomb"].ToString());
            allGrenades = (bool) Config["AllF1GrenadesAreBoogie"];
            bombRadius = float.Parse(Config["DanceFloorSize"].ToString());
            timeToLoop = float.Parse(Config["DanceDurationSeconds"].ToString());

            useLegacy = (bool)Config["LegacyBoogieBomb"];
            radioToUse = int.Parse(Config["RadioToUse"].ToString());
            
            
        }
        void OnServerInitialized(bool initial)
        {
            timer.Once(5.0f, () =>
            {
                if (radioToUse < 0 || radioToUse >= BoomBox.ValidStations.Count)
                {
                    UnityEngine.Debug.LogError($"Invalid radio station! Enter between 0 and {BoomBox.ValidStations.Count - 1}. Setting to 2");
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"Radio set to {BoomBox.ValidStations.ElementAt(radioToUse).Key}");
                }
            });
            
        }
        object OnExplosiveFuseSet(TimedExplosive ent, float fuseLength)
        {
            if(ent.skinID == boogieSkin || (allGrenades && ent.ShortPrefabName == "grenade.f1.deployed"))
            {
                if(bounceEffect == null)
                {
                    CollectableEasterEgg egg = GameManager.server.CreateEntity("assets/prefabs/misc/easter/painted eggs/collectableegg.prefab", new Vector3(), new Quaternion(), true) as CollectableEasterEgg;
                    bounceEffect = egg.pickupEffect;
                }
                ent.bounceEffect = bounceEffect;
                setBoogieBombs.Add(ent);

                
                timer.Once(fuseLength - 0.05f,() =>{
                    //assets/prefabs/misc/easter/egghunt.prefab
                    List<BaseEntity> music = new List<BaseEntity>();
                    if (useLegacy)
                    {
                        RustigeEgg musicBox = GameManager.server.CreateEntity("assets/prefabs/misc/easter/faberge_egg_d/rustigeegg_d.deployed.prefab", ent.transform.position, new Quaternion(), true) as RustigeEgg;
                        musicBox.Spawn();
                        musicBox.SetFlag(BaseEntity.Flags.Open, true, networkupdate: false);

                        musicBox.SendNetworkUpdateImmediate();
                        music.Add(musicBox as BaseEntity);
                    }
                    else
                    {
                        DeployableBoomBox musicBox = GameManager.server.CreateEntity("assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab", ent.transform.position + (Vector3.up * 0.1f), new Quaternion(), true) as DeployableBoomBox;
                        musicBox.Spawn();
                        musicBox.PowerUsageWhilePlaying = 0;
                        
                        BasePlayer player = ent.creatorEntity as BasePlayer;
                        BaseNetworkable.LoadInfo info = new BaseNetworkable.LoadInfo() { msg = new ProtoBuf.Entity() { boomBox = new ProtoBuf.BoomBox() { radioIp = BoomBox.ValidStations.ElementAt(radioToUse).Value, assignedRadioBy = player.userID } } };
                        musicBox.Load(info);
                        musicBox.BoxController.ServerTogglePlay(true);
                        music.Add(musicBox as BaseEntity);

                        //assets/prefabs/voiceaudio/discoball/discoball.deployed.prefab
                        IOEntity disco = GameManager.server.CreateEntity("assets/prefabs/voiceaudio/discoball/discoball.deployed.prefab", ent.transform.position + (Vector3.up * 2.5f), new Quaternion(), true) as IOEntity;
                
                        disco.Spawn();
                        disco.SetFlag(BaseEntity.Flags.Reserved8, true, networkupdate: false);
                        disco.SendNetworkUpdateImmediate();
                        music.Add(disco);

                        DiscoFloor floor = GameManager.server.CreateEntity("assets/prefabs/voiceaudio/discofloor/discofloor.deployed.prefab", ent.transform.position, new Quaternion(), true) as DiscoFloor;

                         
                        floor.Spawn();
                        
                        floor.SetFlag(BaseEntity.Flags.Reserved8, true, networkupdate: false);
                        floor.SendNetworkUpdateImmediate();
                        music.Add(floor);

                        music.Add(SpawnLaser(ent.transform.position + (Vector3.right * 2.5f), new Quaternion(), ent.transform.position));
                        music.Add(SpawnLaser(ent.transform.position + (Vector3.left * 2.5f), new Quaternion(), ent.transform.position));
                        music.Add(SpawnLaser(ent.transform.position + (Vector3.forward * 2.5f), new Quaternion(), ent.transform.position));
                        music.Add(SpawnLaser(ent.transform.position + (Vector3.back * 2.5f), new Quaternion(), ent.transform.position));
                    }
                    List<BasePlayer> playersNear = FindAllPlayersNear(ent.transform.position, bombRadius);
                    
                    foreach (BasePlayer item in playersNear)
                    {
                        if (item == null || item.gestureList == null) continue;
                        DancePlayer(item);
                    }
                  
                    ent.Kill();
                    timer.Once(timeToLoop, () =>{
                        if(music != null){
                            //RunEffect("assets/bundled/prefabs/fx/dig_effect.prefab",musicBox.transform.position);
                            RunEffect("assets/prefabs/misc/easter/easter basket/effects/eggexplosion.prefab",music[0].transform.position);
                            foreach (BaseEntity entit in music)
                            {
                                entit.Kill();
                            }
                            
                            
                        }
                    });
                });

                return false;
            }
            return false;
        }

        private LaserLight SpawnLaser(Vector3 pos, Quaternion rot, Vector3 target)
        {
            LaserLight laser = GameManager.server.CreateEntity("assets/prefabs/voiceaudio/laserlight/laserlight.deployed.prefab", pos + (Vector3.up * 10), Quaternion.Euler(180,0,0), true) as LaserLight;
            
            laser.Spawn();
            laser.SetFlag(BaseEntity.Flags.Reserved8, true, networkupdate: false);
            laser.SendNetworkUpdateImmediate();
            return laser;
        }
        private void DancePlayer(BasePlayer player)
        {
            
            RunEffect("assets/prefabs/misc/easter/painted eggs/effects/eggpickup.prefab", player.transform.position);
            RunEffect("assets/prefabs/misc/halloween/lootbag/effects/gold_open.prefab", player.transform.position + (Vector3.up * 2f));

                Server_StartGesture(player, player.gestureList.IdToGesture(834887525));
                Timer reapeatTimer = timer.Every(6.0f, () =>
                {
                    Server_StartGesture(player, player.gestureList.IdToGesture(834887525));
                });
                timer.Once(timeToLoop, () =>
                {
                    StopGesture(player);
                    reapeatTimer.Destroy();
                });
            
        }
        
        private List<BasePlayer> FindAllPlayersNear(Vector3 pos,float radius)
        {
            Collider[] cast = Physics.OverlapSphere(pos, radius);
            List<BasePlayer> ents = new List<BasePlayer>();
            foreach (Collider item in cast)
            {
                BaseEntity entity = item.gameObject.ToBaseEntity();
                if (entity.IsValid() && entity is BasePlayer && entity is Scientist == false && entity.IsVisible(pos))
                {
                    ents.Add(entity as BasePlayer);

                }

            }
            return ents;
        }
        private void StopGesture(BasePlayer player)
        {            
            player.SignalBroadcast(BaseEntity.Signal.Gesture, "cancel");
        }
        private static void Server_StartGesture(BasePlayer player, GestureConfig toPlay)//Magic method
        {
            
            if (toPlay == null|| player == null || player.IsDead())
                return;
            if (toPlay.animationType == GestureConfig.AnimationType.OneShot)
                player.ClientRPC<uint>((Network.Connection)null, "Client_StartGesture", toPlay.gestureId);

        }
        
        private void RunEffect(string effect,Vector3 pos){
            Effect.server.Run(effect, pos,Vector3.up, broadcast: true);
        }

        public void GivePlayerBoogieBomb(BasePlayer player)
        {
            player.inventory.GiveItem(ItemManager.CreateByName("grenade.f1", 1, boogieSkin),
                          player.inventory.containerBelt);
        }

        [Command("boogiebomb")]
        private void BoogieBombCommand(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.HasPermission("BoogieBomb.use"))
            {
                BasePlayer player = iplayer.Object as BasePlayer;
                GivePlayerBoogieBomb(player);
                
            }
            else{
                iplayer.Message("You dont have permission to do that!");
            }
        }
        [Command("dance")]
        private void DanceCommand(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.HasPermission("BoogieBomb.me"))
            {
                BasePlayer player = iplayer.Object as BasePlayer;
                Server_StartGesture(player, player.gestureList.IdToGesture(834887525));
            }
            else
            { 
                iplayer.Message("You dont have permission to do that!");
            }
        }
        [Command("dancetarget")]
        private void DanceTargetCommand(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.HasPermission("BoogieBomb.target"))
            {
                if (args.Length <= 0) { iplayer.Message("Please specify a player"); return; }
                BasePlayer playerToAdd = BasePlayer.Find(args[0]);

                if (playerToAdd != null && playerToAdd.IsConnected)
                {
                    Server_StartGesture(playerToAdd, playerToAdd.gestureList.IdToGesture(834887525));
                }
                else
                {
                    iplayer.Message("No player found!");
                }

            }
            else
            {
                iplayer.Message("You dont have permission to do that!");
            }
        }
        protected override void LoadDefaultConfig()
        {  
            LogWarning("Creating a new configuration file");
            Config["SkinForBoogieBomb"] = 2400277081;
            Config["AllF1GrenadesAreBoogie"] = false;
            Config["DanceFloorSize"] = 10.0f;
            Config["DanceDurationSeconds"] = 6.0f;
            Config["LegacyBoogieBomb"] = false;
            Config["RadioToUse"] = 2;
        }
    }
}