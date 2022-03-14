using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using Newtonsoft.Json;
using Rust;
using Network;

namespace Oxide.Plugins
{
    [Info("Arena", "k1lly0u", "0.1.148")]
    [Description("A multi arena plugin capable of running many instances of various events at one time")]
    class Arena : RustPlugin
    {
        #region Fields
        [PluginReference] Plugin Spawns, ZoneManager, Kits, ImageLibrary, LustyMap, RotatingPickups, Economics, ServerRewards, NoEscape;

        public static ArenaUI UI;
        public static ArenaStatistics Statistics;
        public static ArenaEvents Events;
        public static ArenaLootSpawns Loot;

        private ArenaData arenaData;
        private RestoreData restoreData;
        private DynamicConfigFile eventData, restorationData;

        private static Arena ins;

        private Dictionary<ulong, Timer> killTimers;  
        public Dictionary<ulong, Timer> respawnTimers;
        public Dictionary<string, EventManager> events;
        public Dictionary<string, List<AdditionalParamData>> additionalParameters;

        private Hash<ulong, Action> pendingTP = new Hash<ulong, Action>();
        private Hash<ulong, double> cooldownTP = new Hash<ulong, double>();

        public List<string> eventTypes; 
                
        public bool isInitialized;
        public static bool isUnloading;        
        #endregion

        #region Oxide Hooks
        private void Loaded()
        {
            eventData = Interface.Oxide.DataFileSystem.GetFile("Arena/event_data");
            restorationData = Interface.Oxide.DataFileSystem.GetFile("Arena/restoration_data");

            killTimers = new Dictionary<ulong, Timer>();
            respawnTimers = new Dictionary<ulong, Timer>();            
            events = new Dictionary<string, EventManager>();            
            additionalParameters = new Dictionary<string, List<AdditionalParamData>> ();
            eventTypes = new List<string>();

            permission.RegisterPermission("arena.admin", this);
            permission.RegisterPermission("arena.blacklisted", this);
            lang.RegisterMessages(Messages, this); 
            isUnloading = false;
        }

        private void OnServerInitialized()
        {
            ins = this;
                 
            LoadData();

            cmd.AddChatCommand(configData.ServerSettings.ChatCommand, this, cmdEventMenu);

            if (!configData.EventSettings.Continual)
            {
                //Unsubscribe(nameof(OnPlayerInput));
                Unsubscribe(nameof(OnPlayerTick));
            }

            if (!configData.EventSettings.AddToTeams)
            {
                Unsubscribe(nameof(OnTeamDisband));
                Unsubscribe(nameof(OnTeamLeave));
                Unsubscribe(nameof(OnTeamInvite));
            }
             
            timer.In(2, InitializePluginReferences);            
        }

        private void Unload()
        {
            isUnloading = true;

            SaveRestoreData();

            EventManager[] eventGames = UnityEngine.Object.FindObjectsOfType<EventManager>();
            if (eventGames != null)
            {
                foreach (EventManager eventGame in eventGames)
                    UnityEngine.Object.Destroy(eventGame);
            }

            EventPlayer[] eventPlayers = UnityEngine.Object.FindObjectsOfType<EventPlayer>();
            if (eventPlayers != null)
            {
                foreach (EventPlayer eventPlayer in eventPlayers)
                {
                    UnityEngine.Object.DestroyImmediate(eventPlayer);
                }
            }

            events.Clear();

            ins = null;
            Events = null;
            Statistics = null;
            UI = null;
            Loot = null;
        }

        private void OnServerSave() => SaveRestoreData();        
        
        private void OnPlayerConnected(BasePlayer player)
        {
            if (!isInitialized || player == null)
                return;

            if (player.IsDead() || player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.Once(1.5f, () => OnPlayerConnected(player));
                return;
            }

            UnlockInventory(player);

            if (configData.ServerSettings.EventOnly)
            {
                EventPlayer eventPlayer = ToEventPlayer(player);
                if (eventPlayer != null)
                    UnityEngine.Object.DestroyImmediate(eventPlayer);

                if (!string.IsNullOrEmpty(configData.LobbySettings.LobbyZoneID))
                {
                    if (!(bool)ZoneManager.Call("isPlayerInZone", configData.LobbySettings.LobbyZoneID, player))
                        SendToWaiting(player);
                }
                if (configData.ServerSettings.MenuOnJoin && events.Count > 0)
                    UI.OpenMainMenu(player, events.First().Value.config.eventType);
            }
            else
            {
                if (restoreData.HasRestoreData(player.userID))
                    restoreData.RestorePlayer(player);
            }
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (configData.ServerSettings.EventOnly)
            {
                EventPlayer eventPlayer = ToEventPlayer(player);
                if (eventPlayer != null && eventPlayer.hasEnteredEvent)
                {
                    EventManager manager = FindManagerByName(eventPlayer.currentEvent);
                    if (manager != null && manager.eventPlayers.Contains(eventPlayer))
                        manager.LeaveEvent(eventPlayer);
                    UnityEngine.Object.DestroyImmediate(eventPlayer);
                }

                UnlockInventory(player);
                SendToWaiting(player);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            EventPlayer eventPlayer = ToEventPlayer(player);
            if (eventPlayer != null)
            {
                EventManager manager = FindManagerByName(eventPlayer.currentEvent);
                if (manager != null)
                    manager.LeaveEvent(eventPlayer);

                UnityEngine.Object.Destroy(eventPlayer);
                
                UnlockInventory(player);
                UI.DestroyAllUI(player);

                player.Die();
            }            
        }

        //private void OnPlayerInput(BasePlayer player, InputState input)
        //{
        //    if (player == null || input == null)
        //        return;

        //    EventPlayer eventPlayer = player.GetComponent<EventPlayer>();
        //    if (eventPlayer != null)
        //    {
        //        if (eventPlayer.player.IsSpectating())
        //        {
        //            if (input.WasJustPressed(BUTTON.JUMP))
        //                eventPlayer.UpdateSpectateTarget(1);

        //            else if (input.WasJustPressed(BUTTON.DUCK))
        //                eventPlayer.UpdateSpectateTarget(-1);
        //        }
        //    }
        //}

        private object CanSpectateTarget(BasePlayer player, string name)
        {
            EventPlayer eventPlayer = player.GetComponent<EventPlayer>();
            if (eventPlayer != null && eventPlayer.Player.IsSpectating())
            {
                eventPlayer.UpdateSpectateTarget(1);
                return false;
            }

            return null;
        }

        private object OnPlayerTick(BasePlayer player, PlayerTick msg, bool wasPlayerStalled)
        {
            EventPlayer eventPlayer = player.GetComponent<EventPlayer>();
            if (eventPlayer != null)
            {
                if (eventPlayer.Player.IsSpectating())
                    return false;
            }
            return null;
        }

        private void OnEntityTakeDamage(BaseEntity entity, HitInfo info)
        {
            if (entity == null || info == null)
                return;

            EventPlayer victim = ToEventPlayer(entity.ToPlayer());
            if (victim != null)
            {
                if (!victim.hasEnteredEvent)
                    return;

                if (victim.IsInvincible)
                {
                    NullifyDamage(info);
                    return;
                }

                FindManagerByName(victim.currentEvent)?.OnPlayerTakeDamage(victim, info);
                return;
            }

            EventPlayer attacker = ToEventPlayer(info.InitiatorPlayer);
            if (attacker != null)
            {
                if (!attacker.hasEnteredEvent)
                    return;

                if (!FindManagerByName(attacker.currentEvent)?.CanPlayerDealDamage(attacker, entity, info) ?? false)
                    NullifyDamage(info);
            }
        }
        
        private object CanBeWounded(BasePlayer player, HitInfo info)
        {
            if (player != null)
            {
                EventPlayer eventPlayer = ToEventPlayer(player);
                if (eventPlayer != null)
                {
                    if (!eventPlayer.hasEnteredEvent)
                        return null;

                    if (FindManagerByName(eventPlayer.currentEvent) != null)
                        return false;
                }
            }
            return null;
        }

        private object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player != null)
            {
                EventPlayer eventPlayer = ToEventPlayer(player);
                if (eventPlayer != null)
                {
                    if (!eventPlayer.hasEnteredEvent)
                        return null;

                    EventManager manager = FindManagerByName(eventPlayer.currentEvent);
                    if (manager != null)
                    {
                        if (!eventPlayer.isDead)
                            manager.OnPlayerDeath(eventPlayer, info);
                        return false;
                    }
                }
            }
            return null;
        }

        private void OnRunPlayerMetabolism(PlayerMetabolism metabolism, BaseCombatEntity entity, float delta)
        {
            if (isInitialized)
            {
                EventPlayer eventPlayer = ToEventPlayer(entity?.ToPlayer());
                if (eventPlayer == null)
                    return;
                if (eventPlayer.isDead)
                {
                    metabolism.bleeding.value = 0;
                    metabolism.poison.value = 0;
                    metabolism.radiation_level.value = 0;
                    metabolism.radiation_poison.value = 0;
                    metabolism.wetness.value = 0;
                }
            }           
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (isInitialized)
            {
                if (entity == null)
                    return;

                if (configData.ServerSettings.NoHelicopters && entity is BaseHelicopter)
                {
                    NextTick(() =>
                    {
                        if (entity != null)
                            (entity as BaseHelicopter).DieInstantly();
                        return;
                    });
                }
                if (configData.ServerSettings.NoAirdrops && entity is CargoPlane)
                {
                    NextTick(() =>
                    {
                        if (entity != null)
                            entity.Kill();
                        return;
                    });
                }
                if (configData.ServerSettings.NoAnimals && entity is BaseNpc)
                {
                    NextTick(() =>
                    {
                        if (entity != null)
                            (entity as BaseNpc).DieInstantly();
                        return;
                    });
                }               
            }
        }

        private object OnCreateWorldProjectile(HitInfo info, Item item)
        {
            if (isInitialized)
            {
                if (info == null) return null;
                if (info.InitiatorPlayer != null)
                {
                    EventPlayer eventPlayer = ToEventPlayer(info.InitiatorPlayer);
                    if (eventPlayer != null)
                    {
                        if (!eventPlayer.hasEnteredEvent)
                            return null;
                        return false;
                    }
                }
                if (info.HitEntity?.ToPlayer() != null)
                {
                    EventPlayer eventPlayer = ToEventPlayer(info.HitEntity.ToPlayer());
                    if (eventPlayer != null)
                    {
                        if (!eventPlayer.hasEnteredEvent)
                            return null;
                        return false;
                    }
                }
            }
            return null;
        }

        private object CanDropActiveItem(BasePlayer player)
        {
            EventPlayer eventPlayer = player.GetComponent<EventPlayer>();
            if (eventPlayer != null)
            {
                if (!eventPlayer.hasEnteredEvent)
                    return null;

                return false;
            }
            return null;
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container) => CanPlayerLoot(player, false, true);

        private object CanLootEntity(BasePlayer player, DroppedItemContainer container) => CanPlayerLoot(player, true);

        private object CanLootEntity(BasePlayer player, LootableCorpse container) => CanPlayerLoot(player);

        private object CanLootEntity(BasePlayer player, ResourceContainer container) => CanPlayerLoot(player, false, true);

        private object CanPlayerLoot(BasePlayer player, bool isCorpse = false, bool isContainer = false)
        {
            if (player != null)
            {
                EventPlayer eventPlayer = ToEventPlayer(player);
                if (eventPlayer != null)
                {
                    if (!eventPlayer.hasEnteredEvent)                    
                        return null;
                    
                    EventManager manager = FindManagerByName(eventPlayer.currentEvent);
                    if (manager != null)
                    {
                        if (manager.status != EventStatus.Started)
                        {
                            player.ChatMessage(msg("nolooting", player.userID));
                            return false;
                        }

                        if (isCorpse && manager.CanDropBackpack())                        
                            return null;
                        
                        if (isContainer && manager.CanLootContainers())                        
                            return null;

                        player.ChatMessage(msg("nolooting_event", player.userID));
                        return false;
                    }
                }
            }

            return null;
        }

        private object OnItemPickup(Item item, BasePlayer player)
        {
            if (player != null)
            {
                if (item?.info?.category == ItemCategory.Weapon || item?.info?.category == ItemCategory.Tool)
                {
                    EventPlayer eventPlayer = ToEventPlayer(player);
                    if (eventPlayer != null)
                    {
                        if (!eventPlayer.hasEnteredEvent)
                            return null;

                        EventManager manager = FindManagerByName(eventPlayer.currentEvent);
                        if (manager != null)
                        {
                            if (manager.status != EventStatus.Started)
                            {
                                player.ChatMessage(msg("nolooting", player.userID));
                                return false;
                            }

                            if (!manager.CanPickupWeapons())
                            {
                                player.ChatMessage(msg("nolooting_event", player.userID));
                                return false;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private void OnEntityBuilt(Planner planner, GameObject gObject)
        {
            BasePlayer player = planner?.GetOwnerPlayer();
            if (player == null)
                return;
           
            BaseEntity entity = gObject?.ToBaseEntity();
            if (entity == null)
                return;

            if (entity is BaseCombatEntity)
            {
                EventPlayer eventPlayer = ToEventPlayer(player);
                if (eventPlayer == null)
                    return;

                EventManager manager = FindManagerByName(eventPlayer.currentEvent);
                if (manager == null)
                    return;

                manager.OnEntityDeployed(entity as BaseCombatEntity);
            }
        }

        private void OnItemDeployed(Deployer deployer, BaseEntity deployedEntity)
        {
            BasePlayer player = deployer.GetOwnerPlayer();
            if (player == null)
                return;

            if (deployedEntity is BaseCombatEntity)
            {
                EventPlayer eventPlayer = ToEventPlayer(player);
                if (eventPlayer == null)
                    return;

                EventManager manager = FindManagerByName(eventPlayer.currentEvent);
                if (manager == null)
                    return;

                manager.OnEntityDeployed(deployedEntity as BaseCombatEntity);
            }
        }

        private object CanNetworkTo(BasePlayer player, BasePlayer target)
        {
            if (isInitialized)
            {
                EventPlayer eventPlayer = ToEventPlayer(player);               
                if (eventPlayer != null && eventPlayer.isDead)
                    return false;                                  
            }
            return null;
        }

        private object OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            if (!configData.ServerSettings.UseChat)
                return null;

            if (player != null)
            {
                if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, "arena.admin"))
                    return null;

                EventPlayer eventPlayer = ToEventPlayer(player);
                if (eventPlayer != null && eventPlayer.hasEnteredEvent)
                    FindManagerByName(eventPlayer.currentEvent)?.ChatToPlayers(eventPlayer.Player, message);
                else
                {
                    foreach (BasePlayer otherPlayer in BasePlayer.activePlayerList.Where(x => ToEventPlayer(x) == null))
                        otherPlayer.SendConsoleCommand("chat.add", new object[] { 0, player.UserIDString, $"<color={(player.IsAdmin ? "#aaff55" : player.IsDeveloper ? "#fa5" : "#55AAFF")}>{player.displayName}</color>: {message}" });
                }
                return false;
            }
            return false;
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            EventPlayer eventPlayer = ToEventPlayer(player);

            if (player == null || player.IsAdmin || eventPlayer == null || !eventPlayer.hasEnteredEvent)
                return null;

            if (configData.EventSettings.CommandBlacklist.Any(x => command.ToLower().Equals(x.ToLower())))
            {
                SendReply(player, msg("blacklistcmd", player.userID));
                return false;
            }
            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            EventPlayer eventPlayer = ToEventPlayer(player);

            if (player == null || player.IsAdmin || eventPlayer == null || !eventPlayer.hasEnteredEvent || arg.Args == null)
                return null;

            if (configData.EventSettings.CommandBlacklist.Any(x => arg.cmd.FullName.ToLower().Equals(x.ToLower())))
            {
                SendReply(player, msg("blacklistcmd", player.userID));
                return false;
            }
            return null;
        }

        private object OnTeamLeave(RelationshipManager.PlayerTeam playerTeam, BasePlayer player)
        {
            EventPlayer eventPlayer = ToEventPlayer(player);
            if (eventPlayer == null)
                return null;

            EventManager eventManager = FindManagerByName(eventPlayer.currentEvent);
            if (Events.IsTeamEvent(eventManager.config.eventType))
            {
                return true;
            }
            return null;
        }

        private object OnTeamDisband(RelationshipManager.PlayerTeam playerTeam)
        {
            foreach(KeyValuePair<string, EventManager> kvp in events)
            {
                if (playerTeam == kvp.Value.playerTeamA || playerTeam == kvp.Value.playerTeamB)
                    return true;
            }
            return null;
        }

        private object OnTeamInvite(BasePlayer player, BasePlayer other)
        {
            EventPlayer eventPlayer = ToEventPlayer(player);
            if (eventPlayer != null)
                return true;
            return null;
        }

        private object OnTeamCreate(BasePlayer player)
        {
            EventPlayer eventPlayer = ToEventPlayer(player);
            if (eventPlayer != null)
                return true;
            return null;
        }
        #endregion

        #region Deathnotes
        private object OnDeathNotice(Dictionary<string, object> data, string message)
        {
            object obj;
            if (data.TryGetValue("VictimEntity", out obj))
            {
                BaseCombatEntity victim = obj as BaseCombatEntity;
                if (victim != null)
                {
                    if (victim.GetComponent<EventPlayer>() /*|| victim.GetComponent<ArenaEvents.NPCSurvival.NPCController>()*/)
                        return false;
                }
            }

            return null;
        }
        #endregion

        #region Classes and Components        
        public enum EventStatus { Pending, Started, Finishing, Finished }
        public enum ScoreType { Kills, Deaths, KD }
        public enum Team { A, B, None }

        public class EventManager : MonoBehaviour 
        {           
            #region Fields
            public string eventName;
            public ArenaData.EventConfig config;
            public EventStatus status;

            private ArenaTeleport arenaTeleporter;

            internal GameTimer gameTimer;
            internal SpawnManager spawnManagerA;
            internal SpawnManager spawnManagerB;
            private CuiElementContainer scoreContainer = null;

            public List<ScoreEntry> scoreData;  
            public List<EventPlayer> eventPlayers = new List<EventPlayer>();

            internal RelationshipManager.PlayerTeam playerTeamA;
            internal RelationshipManager.PlayerTeam playerTeamB;

            internal bool showKillFeed;
            public bool showGameTimers;
            public bool godEnabled;
            public bool isContinual;
            public bool deathKick;
            private bool dropAmmo;
            private bool dropWeapon;
            private bool dropBackpack;
                        
            public string currentKit;
            private double timeRemaining;

            public bool pendingDisable = false;

            private Dictionary<string, ArenaData.EventConfig> closedEvents = new Dictionary<string, ArenaData.EventConfig>();

            private List<BaseCombatEntity> deployedObjects = new List<BaseCombatEntity>();
            #endregion            

            #region Component Management
            private void Awake()
            {          
                dropAmmo = ins.configData.EventSettings.DropAmmo;
                dropWeapon = ins.configData.EventSettings.DropWeapon;
                dropBackpack = ins.configData.EventSettings.DropBackpack;
                showKillFeed = ins.configData.EventSettings.UseKillFeed;
                showGameTimers = ins.configData.GameTimers.ShowGameTimer;
                isContinual = ins.configData.EventSettings.Continual;
                deathKick = ins.configData.EventSettings.DeathKick;
                enabled = false;

                gameTimer = gameObject.AddComponent<GameTimer>();
                gameTimer.Register(this, TimerExpired, showGameTimers);
            }

            public virtual void OnDestroy()
            {
                RespawnAllPlayers();
                EjectAllPlayers();

                DestroyArenaTeleporter();

                if (gameTimer != null)
                    Destroy(gameTimer);
            }

            public virtual void InitializeEvent(string eventName, ArenaData.EventConfig config)
            {
                this.eventName = eventName;
                this.config = config;

                spawnManagerA = new SpawnManager();
                spawnManagerA.SetSpawnFile(config.eventName, config.teamA.spawnfile);

                if (Events.IsTeamEvent(config.eventType))
                {
                    spawnManagerB = new SpawnManager();
                    spawnManagerB.SetSpawnFile(config.eventName, config.teamB.spawnfile);
                }

                if (config.eventKits.Count > 0)
                    currentKit = config.eventKits[0];
                godEnabled = true;
                
                status = EventStatus.Finished;
                InvokeHandler.Invoke(this, WaitMessage, 15f);

                if (!InvokeHandler.IsInvoking(this, BroadcastWaiting))
                    BroadcastWaiting();

                if (Events.IsTeamEvent(config.eventType) && ins.configData.EventSettings.AddToTeams)
                {
                    playerTeamA = RelationshipManager.ServerInstance.CreateTeam();
                    playerTeamB = RelationshipManager.ServerInstance.CreateTeam();
                }

                CreateArenaTeleporter();
            }
            
            public void CheckValidTeams()
            {
                if (playerTeamA == null)
                    playerTeamA = RelationshipManager.ServerInstance.CreateTeam();
                if (playerTeamB == null)
                    playerTeamB = RelationshipManager.ServerInstance.CreateTeam();
            }

            public void CreateArenaTeleporter()
            {
                if (!ins.configData.LobbySettings.LobbyEnabled || string.IsNullOrEmpty(ins.configData.LobbySettings.LobbySpawnfile))
                    return;

                if (config.arenaTeleporter != null)
                {
                    arenaTeleporter = ArenaTeleport.Create(config.arenaTeleporter.Position, 0.75f);
                    arenaTeleporter.Initialize(this);
                }
            }

            public void DestroyArenaTeleporter()
            {
                if (arenaTeleporter != null)
                    Destroy(arenaTeleporter);
            }
            #endregion

            #region Event Management
            public void TimerExpired()
            {
                switch (status)
                {
                    case EventStatus.Pending:
                        StartMatch();
                        return;
                    case EventStatus.Started:
                        EndMatch();
                        return;
                    case EventStatus.Finished:
                        Prestart();
                        return;                    
                }
            }

            public virtual void Prestart()
            {
                if (!HasValidPlayers())
                    return;

                InvokeHandler.CancelInvoke(this, WaitMessage);
                DestroyAllPopups();
                scoreData = new List<ScoreEntry>();
                                
                status = EventStatus.Pending;
                gameTimer.StartTimer(ins.configData.GameTimers.MatchPrestart[config.eventType], msg("roundstarts"));

                if (!string.IsNullOrEmpty(config.zoneId))
                    Loot.OnEventStarted(config.zoneId);

                foreach (EventPlayer eventPlayer in eventPlayers)                                  
                    SpawnPlayer(eventPlayer, false, true);                
            }

            public virtual void StartMatch()
            {
                if (!HasValidPlayers())
                    return;
                               
                status = EventStatus.Started;
                godEnabled = false;
                                
                if (config.timeLimit > 0)                                    
                    gameTimer.StartTimer(config.timeLimit * 60);                

                foreach (EventPlayer eventPlayer in eventPlayers)
                {
                    UI.DestroyUI(eventPlayer.Player, ArenaUI.UIPanel.Menu);
                    eventPlayer.ResetStats();
                    SpawnPlayer(eventPlayer);                    
                }
                UpdateScoreboard();
            }

            public virtual void EndMatch()
            {
                gameTimer.StopTimer();

                if (!string.IsNullOrEmpty(config.zoneId))
                    Loot.OnEventFinished(config.zoneId);

                status = EventStatus.Finishing;
                godEnabled = true;

                RespawnAllPlayers();

                if (!isUnloading)
                {
                    ins.NextTick(() =>
                    {
                        CleanupEntities();

                        foreach (EventPlayer eventPlayer in eventPlayers)
                        {
                            if (eventPlayer.Player == null) continue;

                            Statistics.OnGamePlayed(eventPlayer.Player.userID, config);

                            StripInventory(eventPlayer.Player);
                            if (eventPlayer.isOOB)
                            {
                                eventPlayer.isOOB = false;
                                if (ins.killTimers.ContainsKey(eventPlayer.Player.userID))
                                {
                                    ins.killTimers[eventPlayer.Player.userID].Destroy();
                                    ins.killTimers.Remove(eventPlayer.Player.userID);
                                }
                            }

                            UI.DestroyUI(eventPlayer.Player, ArenaUI.UIPanel.Menu);
                            DisplayFinalScoreboard();
                        }
                        Statistics.OnGameFinished(config.eventType, config.eventName);

                        int disableFor;
                        if (ins.configData.EventSettings.DisableAfterPlayed.TryGetValue(eventName, out disableFor))
                        {
                            pendingDisable = true;

                            InvokeHandler.Invoke(this, ()=>
                            {
                                status = EventStatus.Finished;
                                ins.TemporarilyDisableEvent(eventName, disableFor);
                            }, 30f);                            
                            return;
                        }

                        if (isContinual)
                        {
                            SwitchTeams();
                            InvokeHandler.Invoke(this, ()=>
                            {
                                status = EventStatus.Finished;
                                ResetMatch();
                            }, ins.configData.GameTimers.TimeBetweenRounds);
                        }
                        else InvokeHandler.Invoke(this, ()=>
                        {
                            status = EventStatus.Finished;
                            EjectAllPlayers();
                        }, 30f);
                    });
                }
            }

            private void SwitchTeams()
            {                
                if (!Events.IsTeamEvent(config.eventType) || !config.switchTeamOnRepeat)
                    return;

                ArenaData.EventConfig.TeamEntry teamA = config.teamA;
                ArenaData.EventConfig.TeamEntry teamB = config.teamB;

                config.teamA = teamB;
                config.teamB = teamA;
                spawnManagerA.SetSpawnFile(config.eventName, config.teamA.spawnfile);
                spawnManagerB.SetSpawnFile(config.eventName, config.teamB.spawnfile);
            }

            private void ResetMatch()
            {
                foreach (EventPlayer eventPlayer in eventPlayers)
                {
                    if (eventPlayer.Player.IsSpectating())
                        eventPlayer.FinishSpectating();

                    StripInventory(eventPlayer.Player);
                    eventPlayer.ResetStats();
                }

                if (eventPlayers.Count >= config.playersToStart)
                {
                    BroadcastToPlayers("playersreached", new string[] { ins.configData.GameTimers.MatchPrestart[config.eventType].ToString() }, true);
                    Prestart();
                }
                else
                {
                    InvokeHandler.Invoke(this, WaitMessage, 15f);

                    if (!InvokeHandler.IsInvoking(this, BroadcastWaiting))
                        BroadcastWaiting();
                }
            }

            public void EjectAllPlayers()
            {
                if (eventPlayers.Count > 0)
                {
                    for (int i = eventPlayers.Count - 1; i >= 0; i--)
                        LeaveEvent(eventPlayers[i]);
                    eventPlayers.Clear();
                }
            }

            private void RespawnAllPlayers()
            {                
                foreach(EventPlayer eventPlayer in eventPlayers)
                {
                    if (eventPlayer.Player.IsSpectating())
                        eventPlayer.FinishSpectating();

                    if (eventPlayer.isDead)                    
                        ins.RespawnPlayer(eventPlayer.Player);                                    
                }
            }

            private void CheckPlayers()
            {                
                if (eventPlayers.Count < config.minimumPlayers || eventPlayers.Count == 0)
                {
                    switch (status)
                    {
                        case EventStatus.Finished:
                        case EventStatus.Pending:
                            gameTimer.StopTimer();
                            status = EventStatus.Finished;
                            BroadcastToPlayers("notenoughtostart", null, true);
                            InvokeHandler.Invoke(this, WaitMessage, 15f);

                            if (!InvokeHandler.IsInvoking(this, BroadcastWaiting))
                                BroadcastWaiting();
                            break;
                        case EventStatus.Started:
                            gameTimer.StopTimer();
                            BroadcastToPlayers("notenoughtocontinue", null, true);
                            EndMatch();
                            break;                        
                    }                    
                }                
            }

            private bool HasValidPlayers()
            {
                for (int i = eventPlayers.Count - 1; i >= 0; i--)
                {
                    EventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer == null || eventPlayer.Player == null || !eventPlayer.Player.IsConnected)
                    {
                        eventPlayers.Remove(eventPlayer);
                        Destroy(eventPlayer);
                    }
                }

                if (eventPlayers.Count == 0 || eventPlayers.Count < config.playersToStart)
                {
                    status = EventStatus.Finished;
                    gameTimer.StopTimer();
                    BroadcastToPlayers("notenoughtostart", null, true);
                    InvokeHandler.Invoke(this, WaitMessage, 15f);

                    if (!InvokeHandler.IsInvoking(this, BroadcastWaiting))
                        BroadcastWaiting();
                    return false;
                }
                return true;
            }

            private void WaitMessage()
            {
                if (status == EventStatus.Finished)
                {
                    BroadcastToPlayers("notenoughtostart", null, true);
                    InvokeHandler.Invoke(this, WaitMessage, 15f);

                    if (!InvokeHandler.IsInvoking(this, BroadcastWaiting))
                        BroadcastWaiting();
                }
            }

            internal void BroadcastWaiting()
            {
                if (status == EventStatus.Started)
                    return;

                if (config.minimumPlayers > 1 && eventPlayers.Count > 0)
                {
                    BroadcastToAll("waitingBroadcast", new string[] { config.eventName, config.eventType, (config.minimumPlayers - eventPlayers.Count).ToString() });

                    InvokeHandler.Invoke(this, BroadcastWaiting, 45);
                }
            }

            public void DisableMatchingZones()
            {
                closedEvents.Clear();
                KeyValuePair<string, EventManager>[] events = ins.events.Where(x => (!string.IsNullOrEmpty(x.Value.config.zoneId) && x.Value.config.zoneId == config.zoneId && x.Value != this)).ToArray();
                if (events.Length > 0)
                {
                    for (int i = events.Length - 1; i >= 0; i--)
                    {
                        KeyValuePair<string, EventManager> eventGame = events[i];
                        closedEvents.Add(eventGame.Key, eventGame.Value.config);
                        eventGame.Value.EndMatch();
                        eventGame.Value.EjectAllPlayers();
                        Destroy(eventGame.Value);
                        ins.events.Remove(eventGame.Key);
                    }
                }
            }

            public void EnableMatchingZones()
            {
                if (isUnloading)
                    return;

                foreach(KeyValuePair<string, ArenaData.EventConfig> eventGame in closedEvents)                
                    ins.InitializeEvent(eventGame.Key, eventGame.Value);
                
                closedEvents.Clear();
            }
            #endregion

            #region Player Management
            public virtual bool CanJoinEvent()
            {
                if (pendingDisable)
                    return false;

                if (eventPlayers.Count >= config.maximumPlayers)
                    return false;
                return true;
            }

            public virtual void JoinEvent(EventPlayer eventPlayer, Team team)
            {
                eventPlayer.ResetStats();

                eventPlayer.Team = team;
                eventPlayers.Add(eventPlayer);
                BroadcastToPlayers("joinevent", new string[] { eventPlayer.Player.displayName });

                if (!string.IsNullOrEmpty(config.zoneId))
                {
                    ins.ZoneManager?.Call("AddPlayerToZoneWhitelist", config.zoneId, eventPlayer.Player);

                    if (ins.configData.EventSettings.DisableUsedZones && eventPlayers.Count == 1)                    
                        DisableMatchingZones();                    
                }

                if (ins.configData.EventSettings.BroadcastJoiners)
                {
                    BroadcastToAll("joineventglobal", new string[] { eventPlayer.Player.displayName, config.eventName });
                }

                switch (status)
                {
                    case EventStatus.Pending:
                        if (ins.configData.EventSettings.SendToArenaOnJoin)
                            SpawnPlayer(eventPlayer, false, true);
                        return;
                    case EventStatus.Finished:
                        if (eventPlayers.Count == config.minimumPlayers)
                        {
                            BroadcastToPlayers("playersreached", new string[] { ins.configData.GameTimers.MatchPrestart[config.eventType].ToString() }, true);
                            Prestart();
                        }
                        else
                        {
                            if (ins.configData.EventSettings.SendToArenaOnJoin)
                                SpawnPlayer(eventPlayer, false, true);
                        }
                        return;
                    case EventStatus.Started:
                        SpawnPlayer(eventPlayer, true, true);
                        return;
                }
            }
            
            public virtual void LeaveEvent(EventPlayer eventPlayer)
            {
                if (eventPlayers.Contains(eventPlayer))
                {
                    if (eventPlayer.Player != null)
                    {
                        if (eventPlayer.Player.IsSpectating())
                            eventPlayer.FinishSpectating();

                        if (eventPlayer.Player.IsSleeping())
                            eventPlayer.Player.Die();
                        else
                        {
                            if (eventPlayer.hasEnteredEvent)
                                StripInventory(eventPlayer.Player);

                            if (!ins.configData.ServerSettings.EventOnly)
                                eventPlayer.RestorePlayer();                              
                            else ins.SendToWaiting(eventPlayer.Player);
                        }
                    }
                    if (!string.IsNullOrEmpty(config.zoneId))
                        ins.ZoneManager?.Call("RemovePlayerFromZoneWhitelist", config.zoneId, eventPlayer.Player);

                    eventPlayers.Remove(eventPlayer);

                    DestroyImmediate(eventPlayer);

                    if (status != EventStatus.Finished)
                        CheckPlayers();

                    if (ins.configData.EventSettings.DisableUsedZones && eventPlayers.Count == 0)
                        EnableMatchingZones();
                }                
            }

            public void SpawnPlayer(EventPlayer eventPlayer, bool giveKit = true, bool sleep = false)
            {
                BasePlayer player = eventPlayer.Player;
                if (player == null) return;
                
                if (!ins.configData.EventSettings.SendToArenaOnJoin)
                    eventPlayer.SetPlayer();

                if (!eventPlayer.hasEnteredEvent)
                {
                    if (Events.IsTeamEvent(config.eventType) && ins.configData.EventSettings.AddToTeams)
                    {
                        switch (eventPlayer.Team)
                        {
                            case Team.A:
                                playerTeamA.AddPlayer(eventPlayer.Player);
                                break;
                            case Team.B:
                                playerTeamB.AddPlayer(eventPlayer.Player);
                                break;
                        }
                    }

                    eventPlayer.hasEnteredEvent = true;
                }

                StripInventory(player);                

                ins.ResetMetabolism(player);

                Vector3 spawnPoint = eventPlayer.Team == Team.A ? spawnManagerA.GetSpawnPoint() : spawnManagerB.GetSpawnPoint();
                ins.MovePosition(player, spawnPoint, sleep);

                if (config.useClassSelector && eventPlayer.currentKit == -1)
                {
                    eventPlayer.RemoveFromNetwork();
                    UI.ShowSpawnScreen(eventPlayer.Player);
                    return;
                }

                OnPlayerSpawn(eventPlayer);                

                if (scoreContainer != null)
                {
                    UI.DestroyUI(eventPlayer.Player, ArenaUI.UIPanel.Scores);
                    UI.AddUI(eventPlayer.Player, ArenaUI.UIPanel.Scores, scoreContainer);
                }   
                
                if (giveKit)
                {
                    ins.NextTick(() =>
                    {
                        if (OverridePlayerKit(eventPlayer) is bool)
                            return;

                        if (config.eventKits.Count > 1 && config.useClassSelector)
                            ins.GiveKit(player, config.eventKits[eventPlayer.currentKit]);
                        else ins.GiveKit(player, currentKit);

                        GiveTeamGear(eventPlayer);

                        if (ins.configData.EventSettings.LockInventory)
                            LockInventory(eventPlayer.Player);
                    });
                }

                UI.ShowHelpText(player);
            }

            public virtual void OnPlayerSpawn(EventPlayer eventPlayer)
            {
            }

            public virtual void GiveTeamGear(EventPlayer eventPlayer)
            {
            }

            public virtual object OverridePlayerKit(EventPlayer eventPlayer)
            {
                return null;
            }

            public virtual EventPlayer[] GetSpectateTargets(Team team)
            {
                return eventPlayers.Where(x => x.Team == team && !x.Player.IsSpectating()).ToArray();
            }

            public int CountAlive()
            {
                int alive = 0;
                foreach (EventPlayer eventPlayer in eventPlayers)
                    if (!eventPlayer.isDead && !eventPlayer.Player.IsSpectating())
                        alive++;
                return alive;
            }

            public int CountStillPlaying()
            {
                int alive = 0;
                foreach (EventPlayer eventPlayer in eventPlayers)
                    if (!eventPlayer.isEliminated)
                        alive++;
                return alive;
            }

            public int GetTeamCount(Team team)
            {
                IEnumerable<EventPlayer> players = eventPlayers.Where(x => x.Team == team);
                if (players == null) return 0;
                else return players.Count();
            }

            public int GetTeamAlive(Team team)
            {
                IEnumerable<EventPlayer> players = eventPlayers.Where(x => x.Team == team && !x.isDead && !x.Player.IsSpectating());
                if (players == null) return 0;
                else return players.Count();
            }

            public virtual int GetTeamScore(Team team)
            {
                int kills = 0;
                IEnumerable<EventPlayer> teamPlayers = eventPlayers.Where(x => x.Team == team);
                foreach (EventPlayer eventPlayer in teamPlayers)
                    kills += eventPlayer.kills;
                return kills;
            }

            public void BalanceTeams()
            {
                int aCount = GetTeamCount(Team.A);
                int bCount = GetTeamCount(Team.B);

                int difference = aCount > bCount + 1 ? aCount - bCount : bCount > aCount + 1 ? bCount - aCount : 0;

                Team moveFrom = aCount > bCount + 1 ? Team.A : bCount > aCount + 1 ? Team.B : Team.None;

                if (difference > 1 && moveFrom != Team.None)
                {
                    BroadcastToPlayers("teamsunbalanced", null, true);
                    EventPlayer[] teamPlayers = eventPlayers.Where(x => x.Team == moveFrom).ToArray();

                    for (int i = 0; i < (int)Math.Floor((float)difference / 2); i++)
                    {
                        EventPlayer eventPlayer = teamPlayers.GetRandom();
                        eventPlayer.Team = moveFrom == Team.A ? Team.B : Team.A;
                        BroadcastToPlayer(eventPlayer, string.Format(msg("teamswitched", eventPlayer.Player.userID), eventPlayer.Team));
                    }                    
                }                
            }
            #endregion

            #region Death and Damage Management
            public virtual bool CanPlayerDealDamage(EventPlayer attacker, BaseEntity entity, HitInfo info)
            {
                return false;
            }

            public virtual void OnPlayerTakeDamage(EventPlayer eventPlayer, HitInfo info)
            {
                BasePlayer player = eventPlayer.Player;
                EventPlayer attacker = ToEventPlayer(info.InitiatorPlayer);
                
                if (godEnabled || eventPlayer.isDead || (attacker == null && info.InitiatorPlayer != null && !IsEventNPC(info.InitiatorPlayer.userID)))
                {
                    ins.NullifyDamage(info);
                    return;
                }
                
                if (info.isHeadshot)
                {
                    if (attacker != null)
                        Statistics.OnHeadshot(attacker.Player.userID, config);
                }

                float damageModifier = GetDamageModifier(attacker);
                if (damageModifier != 0)
                    info.damageTypes.ScaleAll(damageModifier);

                eventPlayer.OnTakeDamage(attacker?.Player.userID ?? 0U);
            }

            public void OnPlayerDeath(EventPlayer eventPlayer, HitInfo info)
            {               
                eventPlayer.isDead = true;
                BasePlayer player = eventPlayer.Player;   
                
                if (dropBackpack && CanDropBackpack())
                {                    
                    DroppedItemContainer itemContainer = ItemContainer.Drop("assets/prefabs/misc/item drop/item_drop_backpack.prefab", player.transform.position, Quaternion.identity, new ItemContainer[] { player.inventory.containerBelt, player.inventory.containerMain });
                    if (itemContainer != null)
                    {
                        itemContainer.playerName = player.displayName;
                        itemContainer.playerSteamID = player.userID;
                    }
                }
                else
                {
                    if (dropAmmo && CanDropAmmo())
                    {
                        Item item = player.GetActiveItem();
                        if (item != null)
                        {
                            BaseProjectile weapon = item.GetHeldEntity() as BaseProjectile;
                            if (weapon != null && weapon.primaryMagazine.contents > 0)
                            {
                                Item ammo = ItemManager.Create(weapon.primaryMagazine.ammoType, weapon.primaryMagazine.contents);
                                BaseEntity entity = ammo.Drop(player.transform.position, Vector3.up);
                                ammo.Remove(30f);
                                weapon.primaryMagazine.contents = 0;

                                ins.RotateDroppedItem(entity);
                            }
                        }
                    }
                    if (dropWeapon && CanDropWeapons())
                    {
                        Item item = player.GetActiveItem();
                        if (item != null)
                        {
                            BaseEntity entity = item.Drop(player.transform.position, Vector3.up);
                            item.Remove(30f);
                            ins.RotateDroppedItem(entity);
                        }
                    }
                }
               
                Statistics.OnEventPlayerDeath(eventPlayer, info, config);

                //EventPlayer attacker = ins.GetUser(info.InitiatorPlayer);
                OnPlayerDeath(eventPlayer, info?.InitiatorPlayer ?? null, info);

                if (info != null)
                    ins.NullifyDamage(info);
            }

            public virtual bool CanShowDeathScreen()
            {
                return true;
            }
           
            public virtual void OnPlayerDeath(EventPlayer victim, BasePlayer attacker = null, HitInfo info = null)
            {
                if (victim == null || victim.Player == null)
                    return;

                victim.isDead = true;

                victim.Player.Invoke(()=> StripInventory(victim.Player), 0.25f);
                //victim.player.inventory.Strip();
                victim.Player.RemoveFromTriggers();

                victim.RemoveFromNetwork();

                if (victim.Player.isMounted)
                {
                    BaseMountable baseMountable = victim.Player.GetMounted();
                    if (baseMountable != null)
                    {
                        baseMountable.DismountPlayer(victim.Player);
                        victim.Player.EnsureDismounted();
                    }
                }

                string attackerName = attacker != null ? attacker.displayName : string.Empty;

                if (CanShowDeathScreen())
                {
                    victim.StartRespawnTimer(ins.configData.GameTimers.RespawnTimer[config.eventType]);
                    UI.ShowDeathScreen(victim.Player, string.IsNullOrEmpty(attackerName) ? msg("aredead", victim.Player.userID) : string.Format(msg("killedby", victim.Player.userID), attackerName));
                }

                if (showKillFeed)
                    DisplayKillToChat(victim, attackerName);
            }

            private void DisplayKillToChat(EventPlayer victim, string attackerName)
            {
                if (Events.IsSurvivalEvent(config.eventType))
                {
                    int remaining = CountAlive();

                    if (remaining > 1)
                    {
                        if (string.IsNullOrEmpty(attackerName))
                        {
                            if (victim.isOOB)
                            {
                                BroadcastToPlayers("survrunaway", new string[] { victim.Player.displayName, remaining.ToString() });
                            }
                            else BroadcastToPlayers("survsuicide", new string[] { victim.Player.displayName, remaining.ToString() });
                        }
                        else
                        {
                            BroadcastToPlayers("survkilledplayer", new string[] { attackerName, victim.Player.displayName, remaining.ToString() });
                        }
                    }
                }               
                else
                {
                    if (string.IsNullOrEmpty(attackerName))
                    {
                        if (victim.isOOB)
                        {
                            BroadcastToPlayers("runaway", new string[] { victim.Player.displayName });
                        }
                        else BroadcastToPlayers("suicide", new string[] { victim.Player.displayName });
                    }
                    else
                    {
                        if (config.eventType != "GunGame")
                            BroadcastToPlayers("killedplayer", new string[] { attackerName, victim.Player.displayName });
                    }
                }
            }

            public virtual void OnNpcDeath(BaseCombatEntity entity, HitInfo info)
            {
                
            }

            public virtual void OnNpcCorpseSpawned(LootableCorpse corpse)
            {
            }

            public virtual bool IsEventNPC(ulong corpseId)
            {
                return false;
            }

            public virtual bool CanDropWeapons()
            {
                return true;
            }

            public virtual bool CanDropAmmo()
            {
                return true;
            }

            public virtual bool CanDropBackpack()
            {
                return true;
            }

            public virtual bool CanLootContainers()
            {
                return true;
            }

            public virtual bool CanPickupWeapons()
            {
                return true;
            }

            public virtual float GetDamageModifier(EventPlayer attacker)
            {
                if (attacker != null)
                    return GetDamageModifier(attacker.Player.userID);
                return 0f;
            }

            public virtual float GetDamageModifier(ulong attackerId)
            {
                return 0f;
            }
            #endregion

            #region Entity Management
            public void OnEntityDeployed(BaseCombatEntity entity)
            {
                deployedObjects.Add(entity);
            }

            private void CleanupEntities()
            {
                for (int i = deployedObjects.Count - 1; i >= 0; i--)
                {
                    BaseCombatEntity entity = deployedObjects[i];
                    if (entity != null && !entity.IsDestroyed)
                        entity.DieInstantly();
                }

                deployedObjects.Clear();
            }
            #endregion

            #region Menu Management
            public virtual Dictionary<string, string> GetAdditionalInformation()
            {
                return new Dictionary<string, string>();
            }
            #endregion

            #region Scoreboards             
            public void DisplayFinalScoreboard() => UI.ShowEventResults(this);

            public void UpdateScoreboard()
            {
                scoreContainer = UI.ShowGameScores(this);                
            }  
            
            public void DestroyScoreboard(BasePlayer player) => UI.DestroyUI(player, ArenaUI.UIPanel.Scores);
            
            public virtual List<ScoreEntry> GetGameScores(Team team = Team.A)
            {
                List<ScoreEntry> gameScores = new List<ScoreEntry>();                
                int i = 1;
                foreach (EventPlayer eventPlayer in eventPlayers.Where(x => x.Team == team).OrderByDescending(x => x.kills))
                {
                    gameScores.Add(new ScoreEntry(eventPlayer, i, eventPlayer.kills, eventPlayer.deaths));
                    i++;
                }
                return gameScores;
            }

            public virtual string GetScoreString() => config.killLimit > 0 ? $"({msg("Kill Limit")}: {config.killLimit})" : "";

            public virtual string GetAdditionalScoreString() => string.Empty;

            public virtual string[] GetScoreType() => new string[] { "K", "D" };

            public virtual string GetScoreName(bool first) => first ? msg("kills") : msg("deaths");

            #endregion
            
            #region Event Messaging
            public virtual void BroadcastToPlayers(string key, string[] args = null, bool isPopup = false)
            {
                foreach (EventPlayer eventPlayer in eventPlayers)  
                    if (eventPlayer?.Player != null)              
                        BroadcastToPlayer(eventPlayer, args != null ? string.Format(msg(key, eventPlayer.Player.userID), args) : msg(key, eventPlayer.Player.userID), isPopup);                
            }

            public virtual void BroadcastToTeam(Team team, string key, string[] args = null, bool isPopup = false)
            {
                foreach (EventPlayer eventPlayer in eventPlayers)
                    if (eventPlayer?.Player != null && eventPlayer.Team == team)
                        BroadcastToPlayer(eventPlayer, args != null ? string.Format(msg(key, eventPlayer.Player.userID), args) : msg(key, eventPlayer.Player.userID), isPopup);
            }

            public void ChatToPlayers(BasePlayer player, string message)
            {
                foreach (EventPlayer eventPlayer in eventPlayers)
                    if (eventPlayer?.Player != null)
                        eventPlayer.Player.SendConsoleCommand("chat.add", new object[] { 0, player.UserIDString, $"<color={(player.IsAdmin ? "#aaff55" : player.IsDeveloper ? "#fa5" : "#55AAFF")}>{player.displayName}</color>: {message}" });
            }

            public void BroadcastToPlayer(EventPlayer eventPlayer, string message, bool isPopup = false)
            {
                if (eventPlayer?.Player != null)
                {
                    if (isPopup)
                        eventPlayer.popupManager.PopupMessage(message);
                    else eventPlayer.Player.SendConsoleCommand("chat.add", new object[] { 0, "76561198403299915", message });
                }
            }   
            
            public void DestroyAllPopups()
            {
                foreach (EventPlayer eventPlayer in eventPlayers)
                    if (eventPlayer?.Player != null)
                        eventPlayer.popupManager.DestroyAllPopups();                
            }

            public void BroadcastWinners(string key, string[] args = null)
            {
                if (ins.configData.EventSettings.BroadcastWinners)
                    ins.PrintToChat(args != null ? string.Format(msg(key), args) : msg(key));
            }

            public void BroadcastToAll(string key, string[] args = null)
            {
                ins.PrintToChat(args != null ? string.Format(msg(key), args) : msg(key));
            }
            #endregion
        }

        public class ArenaTeleport : MonoBehaviour
        {
            public SphereEntity Entity { get; private set; }

            private EventManager manager;

            private Vector3 position;

            private const string SPHERE_ENTITY = "assets/prefabs/visualization/sphere.prefab";

            private const float REFRESH_RATE = 2f;

            private bool isTriggerReady = false;

            private void Awake()
            {
                Entity = GetComponent<SphereEntity>();
                position = Entity.transform.position;
            }

            private void OnDestroy()
            {
                InvokeHandler.CancelInvoke(this, InformationTick);
                Entity.Kill();
            }

            private void OnTriggerEnter(Collider col)
            {
                if (!isTriggerReady)
                    return;

                BasePlayer player = col.gameObject?.ToBaseEntity()?.ToPlayer();
                if (player == null)
                    return;

                if (player.isMounted)
                    return;

                if (!string.IsNullOrEmpty(manager.config.permission) && !ins.permission.UserHasPermission(player.UserIDString, manager.config.permission))
                {
                    player.ChatMessage(msg("teleportNoPerm", player.userID));
                    return;
                }

                EventPlayer eventPlayer = ToEventPlayer(player);
                if (eventPlayer != null)
                    return;

                if (manager.eventPlayers.Count >= manager.config.maximumPlayers)
                {                    
                    player.ChatMessage(msg("atcapacity", player.userID));
                    return;
                }

                if (!manager.CanJoinEvent())
                {
                    player.ChatMessage(msg("eventStarted", player.userID));
                    return;
                }
                
                eventPlayer = player.gameObject.AddComponent<EventPlayer>();
                eventPlayer.currentEvent = manager.eventName;

                if (ins.configData.EventSettings.SendToArenaOnJoin)
                    eventPlayer.SetPlayer();

                if (ArenaEvents.ins.IsTeamEvent(manager.config.eventType))
                {
                    int teamACount = manager.GetTeamCount(Arena.Team.A);
                    int teamBCount = manager.GetTeamCount(Arena.Team.B);
                    if (teamACount > teamBCount)
                    {
                        manager.JoinEvent(eventPlayer, Team.B);
                        return;
                    }
                     
                }
                manager.JoinEvent(eventPlayer, Team.A);
            }

            public void Initialize(EventManager manager)
            {
                this.manager = manager;

                InvokeHandler.InvokeRepeating(this, InformationTick, UnityEngine.Random.Range(0.1f, 2f), REFRESH_RATE);
            }

            private void InformationTick()
            {
                isTriggerReady = true;
                      
                List<BasePlayer> list = Facepunch.Pool.GetList<BasePlayer>();
                Vis.Entities(position, 10f, list);
                if (list.Count > 0)
                {
                    string informationStr = string.Format(INFORMATION_STR,
                    manager.config.eventName,
                    manager.config.eventType,
                    manager.eventPlayers.Count,
                    manager.config.maximumPlayers,
                    manager.status,
                    !Arena.Events.IgnoreKillLimit(manager.config.eventType) ? string.Format(SCORE_LIMIT, manager.config.killLimit) : string.Format(TIME_LIMIT, manager.config.timeLimit == 0 ? "Unlimited" : manager.config.timeLimit.ToString()),
                    manager.config.useClassSelector ? "Class Selector" : "Event Specific");

                    for (int i = 0; i < list.Count; i++)
                    {
                        BasePlayer player = list[i];
                        if (player == null || player.IsDead() || player.isMounted)
                            continue;

                        if (player.IsAdmin)
                            player.SendConsoleCommand("ddraw.text", REFRESH_RATE, Color.white, position, informationStr);
                        else
                        {
                            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                            player.SendNetworkUpdateImmediate();
                            player.SendConsoleCommand("ddraw.text", REFRESH_RATE, Color.white, position, informationStr);
                            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                            player.SendNetworkUpdateImmediate();
                        }
                    }
                }
                Facepunch.Pool.FreeList(ref list);
            }

            public static ArenaTeleport Create(Vector3 position, float radius)
            {
                SphereEntity sphereEntity = GameManager.server.CreateEntity(SPHERE_ENTITY, position, Quaternion.identity) as SphereEntity;
                sphereEntity.currentRadius = sphereEntity.lerpRadius = radius * 2f;
                
                sphereEntity.enableSaving = false;
                sphereEntity.Spawn();

                sphereEntity.gameObject.layer = (int)Rust.Layer.Reserved2;

                SphereCollider sphereCollider = sphereEntity.gameObject.AddComponent<SphereCollider>();
                sphereCollider.radius = radius;
                sphereCollider.isTrigger = true;

                return sphereEntity.gameObject.AddComponent<ArenaTeleport>();
            }

            private const string INFORMATION_STR = "<size=25>{0}</size><size=16>\nGame: {1}\nPlayers: {2} / {3}\nStatus: {4}\n{5}\nKit: {6}</size>";
            private const string TIME_LIMIT = "Time Limit: {0}";
            private const string SCORE_LIMIT = "Score Limit: {0}";
        }
        #endregion  
               
        #region Components
        public class EventPlayer : MonoBehaviour
        {
            public BasePlayer Player { get; private set; }
            public Team Team;
            public PopupManager popupManager;
               
            public int kills;
            public int killStreak;
            public int streakNumber;
            public int deaths;

            public string currentEvent;
            public int currentKit;

            private int spectateIndex;
            private EventPlayer spectateTarget;

            public bool isOOB;
            public bool isDead;
            public bool isEliminated;
            public bool hasEnteredEvent;

            public int specialInt1;
            public int specialInt2;
            public float specialFloat1;
            public float specialFloat2;
            public bool specialBool1;
            public bool specialBool2;

            public bool autoSpawn = false;
            public int respawnTime = 0;

            private double lastAttacked;
            private List<ulong> damageContributors = new List<ulong>();

            public float invincibleUntil = 0f;
            public bool IsInvincible
            {
                get
                {
                    return Time.time < invincibleUntil;
                }
            }

            public bool CanRespawn => respawnTime <= 0;

            private void Awake()
            {
                Player = GetComponent<BasePlayer>();
                popupManager = new PopupManager(Player);
                currentKit = -1;

                UI.ShowHelpText(Player);

                if (ins.configData.EventSettings.Health.Enabled)
                {
                    nextRestoreTime = Time.time + ins.configData.EventSettings.Health.RestoreAfter;
                    InvokeHandler.InvokeRepeating(this, HealthRestoreTick, 1f, 1f);
                }                
            }
                       
            public void SetPlayer()
            {
                if (hasEnteredEvent)
                    return;

                ResetStats();

                SavePlayer();

                if (ins.configData.EventSettings.AddToTeams && Player.currentTeam != 0UL)
                {
                    EventManager eventManager = ins.FindManagerByName(currentEvent);
                    if (eventManager == null)
                        return;

                    if (Events.IsTeamEvent(eventManager.config.eventType))
                    {
                        RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(Player.currentTeam);
                        if (playerTeam != null)
                        {
                            playerTeam.RemovePlayer(Player.userID);
                            eventManager.CheckValidTeams();
                        }
                    }
                }

                ins.LustyMap?.Call("DisableMaps", Player);
                Interface.Oxide.CallHook("EnableBypass", Player.userID);

                if (Player.isMounted)
                    Player.GetMounted()?.AttemptDismount(Player);

                if (ins.NoEscape)
                    ins.permission.GrantUserPermission(Player.UserIDString, "noescape.disable", ins.NoEscape);
            }

            public void ResetStats()
            {
                kills = 0;
                killStreak = 0;
                streakNumber = 0;                

                deaths = 0;
                isDead = false;
                invincibleUntil = 0f;
                isEliminated = false;
                isOOB = false;

                specialBool1 = false;
                specialBool2 = false;
                specialFloat1 = 0;
                specialFloat2 = 0;
                specialInt1 = 0;
                specialInt2 = 0;

                lastAttacked = 0;
                damageContributors.Clear();
            }

            public void SavePlayer()
            {
                if (!ins.configData.ServerSettings.EventOnly)
                    ins.restoreData.AddData(Player);
            }
            
            public void RestorePlayer() => ins.restoreData.RestorePlayer(Player);

            private void OnDestroy()
            {
                if (Player == null)
                    return;

                if (Player.isMounted)                
                    Player.GetMounted()?.AttemptDismount(Player);

                if (ins.configData.EventSettings.AddToTeams && Player.currentTeam != 0UL)
                {
                    EventManager eventManager = ins.FindManagerByName(currentEvent);
                    if (eventManager == null)
                        return;

                    if (Events.IsTeamEvent(eventManager.config.eventType))
                    {
                        RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(Player.currentTeam);
                        if (playerTeam != null)
                        {
                            if (playerTeam == eventManager.playerTeamA || playerTeam == eventManager.playerTeamB)
                            {
                                playerTeam.RemovePlayer(Player.userID);
                                eventManager.CheckValidTeams();
                            }
                        }
                    }
                }

                UnlockInventory(Player);
                
                Interface.Oxide.CallHook("DisableBypass", Player.userID);
                ins.LustyMap?.Call("EnableMaps", Player);

                if (ins.permission.UserHasPermission(Player.UserIDString, "noescape.disable"))
                    ins.permission.RevokeUserPermission(Player.UserIDString, "noescape.disable");

                Player.State.unHostileTimestamp = TimeEx.currentTimestamp;
                Player.DirtyPlayerState();
                Player.ClientRPCPlayer<float>(null, Player, "SetHostileLength", 0f);

                try
                {
                    UI.DestroyUI(Player, ArenaUI.UIPanel.Help);
                    UI.DestroyAllUI(Player);
                } catch { }
            }

            #region Networking
            public void RemoveFromNetwork()
            {               
                if (Net.sv.write.Start())
                {
                    Net.sv.write.PacketID(Message.Type.EntityDestroy);
                    Net.sv.write.EntityID(Player.net.ID);
                    Net.sv.write.UInt8((byte)BaseNetworkable.DestroyMode.None);
                    Net.sv.write.Send(new SendInfo(Player.net.group.subscribers.Where(x => x.userid != Player.userID).ToList()));
                }
            }

            public void AddToNetwork() => Player.SendFullSnapshot();
            #endregion

            #region Spectating 
            public void BeginSpectating()
            {
                if (Player.IsSpectating())
                    return;

                StripInventory(Player);

                Player.StartSpectating();
                UpdateSpectateTarget();
            }

            public void FinishSpectating()
            {
                if (!Player.IsSpectating())
                    return;

                Player.SetParent(null, false, false);
                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, false);
                Player.gameObject.SetLayerRecursive(17);

                ins.FindManagerByName(currentEvent).SpawnPlayer(this, false, false);
            }

            public void SetSpectateTarget(EventPlayer eventPlayer)
            {
                Player.ChatMessage($"Spectating: {eventPlayer.Player.displayName}");

                Player.SendEntitySnapshot(eventPlayer.Player);
                Player.gameObject.Identity();
                Player.SetParent(eventPlayer.Player, false, false);
            }

            public void UpdateSpectateTarget(int index = 0)
            {
                EventPlayer[] eventPlayers = ins.FindManagerByName(currentEvent).GetSpectateTargets(Team);

                if (eventPlayers.Length == 0)
                {
                    UI.ShowSpectateScreen(Player, msg("spectatetext", Player.userID));
                    return;
                }
                else
                {
                    int newIndex = spectateIndex += index;

                    if (newIndex > eventPlayers.Length - 1)
                        newIndex = 0;
                    else if (newIndex < 0)
                        newIndex = eventPlayers.Length - 1;

                    if (eventPlayers[newIndex] == spectateTarget)
                        return;

                    spectateIndex = newIndex;
                    spectateTarget = eventPlayers[spectateIndex];
                    SetSpectateTarget(spectateTarget);
                }
            }
            #endregion

            #region Scoring
            public void AddKill()
            {
                kills++;
                killStreak++;
                if (ins.configData.EventSettings.Killstreaks.Contains(killStreak))
                {
                    streakNumber++;
                    popupManager.PopupMessage(msg($"ks_{streakNumber}", Player.userID));
                    ins.FindManagerByName(currentEvent).BroadcastToPlayers($"ks_{streakNumber}_p", new string[] { Player.displayName });
                }
            }

            public void AddDeath()
            {
                deaths++;
                killStreak = 0;
                streakNumber = 0;

                damageContributors.Clear();
            }
            #endregion

            #region Damage Contributors
            public void OnTakeDamage(ulong attackerId)
            {
                nextRestoreTime = Time.time + ins.configData.EventSettings.Health.RestoreAfter;

                float time = Time.realtimeSinceStartup;
                if (time > lastAttacked)
                {
                    lastAttacked = time + 3f;
                    damageContributors.Clear();
                }

                if (attackerId != 0U && attackerId != Player.userID)
                {
                    if (damageContributors.Contains(attackerId))
                        damageContributors.Remove(attackerId);
                    damageContributors.Add(attackerId);
                }
            }   
            
            public List<ulong> GetDamageContributors() => damageContributors;
            #endregion

            #region Health Restore
            internal float nextRestoreTime;

            private void HealthRestoreTick()
            {
                if (Player == null || isDead)
                    return;

                if (Time.time > nextRestoreTime && Player.health < Player.MaxHealth())
                {
                    Player.health = Mathf.Clamp(Player._health + ins.configData.EventSettings.Health.Amount, 0f, Player._maxHealth);
                    Player.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }
            }

            internal void StartRespawnTimer(int v)
            {
                respawnTime = v;
                InvokeHandler.InvokeRepeating(this, RespawnTimerTick, 1f, 1f);
            }

            private void RespawnTimerTick()
            {
                respawnTime = Mathf.Clamp(respawnTime - 1, 0, int.MaxValue);

                UI.AddRespawnButton(this);

                if (respawnTime <= 0)
                {
                    InvokeHandler.CancelInvoke(this, RespawnTimerTick);

                    if (autoSpawn)
                        ins.RespawnPlayer(Player);
                }
            }
            #endregion
        }

        public class GameTimer : MonoBehaviour
        {
            private EventManager manager;
            private string message;
            private int time;
            private bool showTimer;

            private Action callback;

            public void Register(EventManager manager, Action callback, bool showTimer)
            {
                this.manager = manager;
                this.callback = callback;
                this.showTimer = showTimer;
            }

            public void StartTimer(int time, string message = "")
            {
                this.time = time;
                this.message = message;
                InvokeHandler.InvokeRepeating(this, TimerTick, 1f, 1f);
            }

            public void StopTimer()
            {
                InvokeHandler.CancelInvoke(this, TimerTick);

                if (showTimer)
                {
                    foreach (EventPlayer eventPlayer in manager.eventPlayers)
                        UI?.DestroyUI(eventPlayer.Player, ArenaUI.UIPanel.Clock);
                }
            }

            private void OnDestroy()
            {
                StopTimer();
            }

            private void TimerTick()
            {
                time--;
                if (time == 0)
                {
                    StopTimer();
                    callback.Invoke();
                }
                else
                {
                    if (showTimer)
                        UpdateUITimer();
                }
            }
            private void UpdateUITimer()
            {
                CuiElementContainer container = UI.UpdateTimer(time, message);

                foreach (EventPlayer eventPlayer in manager.eventPlayers)
                {
                    UI.DestroyUI(eventPlayer.Player, ArenaUI.UIPanel.Clock);
                    UI.AddUI(eventPlayer.Player, ArenaUI.UIPanel.Clock, container);
                }                
            }            
        }

        public class PopupManager
        {
            private BasePlayer player;
            private List<string> popupPanels = new List<string>();
            private List<MessageData> popupQueue = new List<MessageData>();
            private int lastId = 0;

            public PopupManager(BasePlayer player)
            {
                this.player = player;
            }

            public void PopupMessage(string message)
            {
                lastId++;
                popupQueue.Add(new MessageData(message, 6, $"arena.{ArenaUI.UIPanel.Popup} {lastId}", this));
                UpdateMessages();
            }

            private CuiElementContainer CreateMessageEntry(int number, MessageData data)
            {
                float[] pos = GetFeedPosition(number);
                CuiElementContainer container = ArenaUI.UI.Popup(data.elementID, data.message, 17, new ArenaUI.UI4(pos[0], pos[1], pos[2], pos[3]), TextAnchor.MiddleCenter, "Hud");
                return container;
            }

            private float[] GetFeedPosition(int number)
            {
                Vector2 initialPos = new Vector2(0.25f, 0.875f);
                Vector2 dimensions = new Vector2(0.5f, 0.04f);
                float yPos = initialPos.y - ((dimensions.y + 0.005f) * number);
                return new float[] { initialPos.x, yPos, initialPos.x + dimensions.x, yPos + dimensions.y };
            }

            private void UpdateMessages(bool destroyed = false)
            {
                if (destroyed)
                {
                    if (popupQueue.Count > 0)
                    {
                        for (int i = 0; i < popupQueue.Count; i++)
                        {
                            if (i >= 3)
                                return;

                            MessageData feed = popupQueue[i];
                            if (!feed.started)                                                           
                                feed.Begin(feed.elementID);
                            
                            AddMessage(CreateMessageEntry(i, popupQueue[i]), feed.elementID);
                        }
                    }
                }
                else
                {
                    if (popupQueue.Count > 0 && popupQueue.Count < 3)
                    {
                        MessageData feed = popupQueue[popupQueue.Count - 1];
                        AddMessage(CreateMessageEntry(popupQueue.Count - 1, feed), feed.elementID);
                        feed.Begin(feed.elementID);
                    }
                }
            }

            private void AddMessage(CuiElementContainer element, string panel)
            {
                if (player != null)
                {
                    popupPanels.Add(panel);
                    UI.AddUI(player, panel, element);
                }
            } 

            private void DestroyUpdate()
            {                
                for (int i = 0; i < popupQueue.Count; i++)
                    UI.DestroyUI(player, popupQueue[i].elementID);

                UpdateMessages(true);
            }

            private void DestroyPopup(MessageData element)
            {
                if (!string.IsNullOrEmpty(element.elementID))
                    UI.DestroyUI(player, element.elementID);

                popupQueue.Remove(element);
                DestroyUpdate();
            }

            public void DestroyAllPopups()
            {
                foreach (string message in popupPanels)
                    UI.DestroyUI(player, message);
                popupPanels.Clear();
            }

            public class MessageData
            {
                private PopupManager messager;
                private int timecount;
                public string elementID;
                public string message;
                public bool started;

                public MessageData(string message, int timecount, string elementID, PopupManager messager)
                {
                    this.timecount = timecount;
                    this.elementID = elementID;
                    this.message = message;
                    this.messager = messager;
                    started = false;
                }

                public void Begin(string elementID)
                {
                    this.elementID = elementID;
                    started = true;
                    StartDestroy();
                }

                private void StartDestroy()
                {
                    if (timecount > 0)
                    {
                        timecount--;
                        ins.timer.Once(1, () => StartDestroy());
                    }
                    else if (timecount == 0)
                        messager.DestroyPopup(this);
                }
            }
        }        
             
        public class SpawnManager
        {
            private string eventName;
            private bool reserveSpawn;

            private List<Vector3> defaultSpawns;
            private List<Vector3> availableSpawns;

            public void SetSpawnFile(string eventName, string spawnFile)
            {
                this.eventName = eventName;

                defaultSpawns = ins.Spawns.Call("LoadSpawnFile", spawnFile) as List<Vector3>;
                availableSpawns = new List<Vector3>(defaultSpawns);                
            }

            public void SetReserved()
            {
                reserveSpawn = true;
                availableSpawns.RemoveAt(0);
            }

            public Vector3 GetSpawnPoint()
            {
                Vector3 point = availableSpawns.GetRandom();
                availableSpawns.Remove(point);
                if (availableSpawns.Count == 0)
                {                    
                    availableSpawns = new List<Vector3>(defaultSpawns);
                    if (reserveSpawn)
                        availableSpawns.RemoveAt(0);
                }
                return point;
            }

            public Vector3 GetSpawnPoint(int number) => defaultSpawns[number];          
        }
        #endregion

        #region Information Classes
        public class ScoreEntry
        {
            public int position;
            public int value1;
            public int value2;
            public string displayName;

            public ScoreEntry() { }
            public ScoreEntry(EventPlayer eventPlayer, int position, int value1, int value2)
            {
                this.position = position;
                this.displayName = UI.StripTags(eventPlayer.Player.displayName);                
                this.value1 = value1;
                this.value2 = value2;
            }
            public ScoreEntry(EventPlayer eventPlayer, int position, string prefix, int value1, int value2)
            {
                this.position = position;
                this.displayName = $"{prefix} {UI.StripTags(eventPlayer.Player.displayName)}";
                this.value1 = value1;
                this.value2 = value2;
            }
        }

        public class AdditionalParamData
        {
            public string field;
            public string type;
            public bool isRequired;
            public bool requireValue;
            public bool useInput;
            public bool useSelector;
            public bool useToggle;
            public string selectorHook;
            public string selectorDesc;
        }

        public class DeadPlayer
        {
            public ulong playerId;
            public string playerName;
            public int value1;
            public int value2;
            public Team team;

            public DeadPlayer(EventPlayer eventPlayer, int value1, int value2)
            {
                playerId = eventPlayer.Player.userID;
                playerName = UI.StripTags(eventPlayer.Player.displayName);
                this.value1 = value1;
                this.value2 = value2;
                team = eventPlayer.Team;
            }
        }

        public class EventWeapon
        {
            public string shortname, ammoType;
            public int amount, ammoAmount;
            public ulong skinId;
            public string[] attachments;
        }
        #endregion

        #region Event Initialization
        private void InitializePluginReferences()
        {
            Events = plugins.PluginManager.GetPlugin("ArenaEvents") as ArenaEvents;
            Statistics = plugins.PluginManager.GetPlugin("ArenaStatistics") as ArenaStatistics;
            UI = plugins.PluginManager.GetPlugin("ArenaUI") as ArenaUI;
            Loot = plugins.PluginManager.GetPlugin("ArenaLootSpawns") as ArenaLootSpawns;

            Events.RegisterEvents(this);
                        
            CheckLoadedDependencies();
        }

        private void CheckLoadedDependencies(int attempts = 1)
        {           
            bool checkAgain = false;

            if (!Spawns)
            {
                PrintError("Spawns Database not found.");
                checkAgain = true;
            }
            if (!ZoneManager)
            {
                PrintError("Zone Manager not found.");
                checkAgain = true;
            }
            if (!Kits)
            {
                PrintError("Kits not found.");
                checkAgain = true;
            }
            if (!ImageLibrary)
            {
                PrintError("Image Library not found.");
                checkAgain = true;
            }
                        
            if (checkAgain)
            {
                if (attempts == 1)
                {
                    PrintWarning("Missing required dependencies. Checking again in 10 seconds");
                    timer.In(10, () => CheckLoadedDependencies(2));
                    return;
                }
                else
                {
                    PrintWarning("Required dependencies are still missing, unable to continue. Check to make sure they have been installed.");
                    return;
                }
            }

            if (arenaData.events.Count > 0)
            {
                PrintWarning("Initializing Events in 5 seconds!");
                timer.In(5, InitializeAllEvents);
            }

            isInitialized = true;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);
        }

        private void InitializeAllEvents()
        {
            foreach (KeyValuePair<string, ArenaData.EventConfig> game in arenaData.events)
            {
                if (game.Value.isDisabled)
                    continue;
                if (game.Value.eventName != game.Key)
                    game.Value.eventName = game.Key;
                InitializeEvent(game.Key, game.Value);
            }
        }

        public bool InitializeEvent(string name, ArenaData.EventConfig config)
        {
            if (events.ContainsKey(name))
                return false;

            if (!eventTypes.Contains(config.eventType))
            {
                PrintError($"Initialization Error: Invalid game type for event: \"{name}\", type: {config.eventType}");
                return false;
            }
            object success = ValidateEventConfig(config);
            if (success is string)
            {
                PrintError((string)success);
                return false;
            }    

            if (!string.IsNullOrEmpty(config.eventIcon))
                UI.AddImage(config.eventIcon, config.eventIcon);

            if (!string.IsNullOrEmpty(config.permission) && !permission.PermissionExists(config.permission))
                permission.RegisterPermission(config.permission, this);
                        
            GameObject eventManager = new GameObject();

            success = Events.SetEventMode(config.eventType, eventManager);
            if (success is bool && (bool)success)
            {
                events.Add(name, eventManager.GetComponent<EventManager>());
                eventManager.GetComponent<EventManager>().InitializeEvent(name, config);
            }

            return true;
        }
        
        public EventManager FindManagerByName(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                EventManager eventManager;
                if (events.TryGetValue(name, out eventManager))
                    return eventManager;
            }
            return null;
        }
        #endregion

        #region Player Management
        public static EventPlayer ToEventPlayer(BasePlayer player)
        {
            if (player == null)
                return null;

            EventPlayer eventPlayer = player.GetComponent<EventPlayer>();

            if (eventPlayer == null)
                return null;

            return eventPlayer;
        }

        private void SendToWaiting(BasePlayer player)
        {
            if (player == null)
                return;

            if (player.GetComponent<EventPlayer>())
                UnityEngine.Object.DestroyImmediate(player.GetComponent<EventPlayer>());

            CancelPendingTeleports(player);

            SpawnPlayer(player, configData.LobbySettings.LobbySpawnfile, true);

            if (configData.ServerSettings.EventOnly && !string.IsNullOrEmpty(configData.LobbySettings.LobbyKit))
                GiveKit(player, configData.LobbySettings.LobbyKit);

        }

        public void SpawnPlayer(BasePlayer player, string spawnFile, bool sleep = false)
        {
            ResetMetabolism(player);
            object spawnPoint = Spawns?.Call("GetRandomSpawn", spawnFile);
            if (spawnPoint is Vector3)
            {
                MovePosition(player, (Vector3)spawnPoint, sleep);
            }
        }

        public void ResetMetabolism(BasePlayer player)
        {            
            player.metabolism.Reset();
            player.health = player.MaxHealth();
        }

        public void GiveKit(BasePlayer player, string kitname) => Kits?.Call("GiveKit", player, kitname);

        public bool CanJoinEvent(BasePlayer player)
        {
            if (ins.configData.EventSettings.RequireNaked)
            {
                if (player.inventory.AllItems().Length != 0)
                {
                    SendReply(player, msg("notnaked", player.userID));
                    return false;
                }
            }
            return true;
        }

        public static void LockInventory(BasePlayer player)
        {
            if (!player.inventory.containerWear.HasFlag(ItemContainer.Flag.IsLocked))
            {
                player.inventory.containerWear.SetFlag(ItemContainer.Flag.IsLocked, true);
                player.inventory.SendSnapshot();
            }
            if (ins.configData.EventSettings.LockInventory)
            {
                if (!player.inventory.containerMain.HasFlag(ItemContainer.Flag.IsLocked))
                {
                    player.inventory.containerMain.SetFlag(ItemContainer.Flag.IsLocked, true);
                    player.inventory.SendSnapshot();
                }
                if (!player.inventory.containerBelt.HasFlag(ItemContainer.Flag.IsLocked))
                {
                    player.inventory.containerBelt.SetFlag(ItemContainer.Flag.IsLocked, true);
                    player.inventory.SendSnapshot();
                }
            }
        }

        public static void UnlockInventory(BasePlayer player)
        {
            if (player == null) return;
            if (player.inventory.containerWear.HasFlag(ItemContainer.Flag.IsLocked))
            {
                player.inventory.containerWear.SetFlag(ItemContainer.Flag.IsLocked, false);
                player.inventory.SendSnapshot();
            }
            if (player.inventory.containerMain.HasFlag(ItemContainer.Flag.IsLocked))
            {
                player.inventory.containerMain.SetFlag(ItemContainer.Flag.IsLocked, false);
                player.inventory.SendSnapshot();
            }
            if (player.inventory.containerBelt.HasFlag(ItemContainer.Flag.IsLocked))
            {
                player.inventory.containerBelt.SetFlag(ItemContainer.Flag.IsLocked, false);
                player.inventory.SendSnapshot();
            }
        }

        public static void StripInventory(BasePlayer player)
        {
            if (player == null)
                return;

            Item[] allItems = player.inventory.AllItems();

            for (int i = allItems.Length - 1; i >= 0; i--)
            {
                Item item = allItems[i];
                item.RemoveFromContainer();
                item.Remove();
            }
        }
        #endregion

        #region Teleportation
        private void MovePosition(BasePlayer player, Vector3 destination, bool sleep)
        {
            if (player.isMounted)
                player.GetMounted().DismountPlayer(player, true);

            if (player.GetParentEntity() != null)
                player.SetParent(null);

            if (sleep)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);                
                player.MovePosition(destination);
                player.UpdateNetworkGroup();
                player.StartSleeping();
                player.SendNetworkUpdateImmediate(false);
                player.ClearEntityQueue(null);
                player.ClientRPCPlayer(null, player, "StartLoading");                   
                player.SendFullSnapshot();
            }
            else
            {
                player.MovePosition(destination);
                player.ClientRPCPlayer(null, player, "ForcePositionTo", destination);
                player.SendNetworkUpdateImmediate();
                player.ClearEntityQueue(null);                
            }
        }      
        #endregion

        #region Death and Respawn Management 
        public void NullifyDamage(HitInfo info)
        {
            info.damageTypes = new DamageTypeList();
            info.HitEntity = null;
            info.HitMaterial = 0;
            info.PointStart = Vector3.zero;
        }

        private void ResetPlayer(BasePlayer player)
        {
            EventPlayer eventPlayer = ToEventPlayer(player);
            if (eventPlayer == null) return;

            if (eventPlayer.Player.IsSpectating())
                eventPlayer.FinishSpectating();

            EventManager manager = FindManagerByName(eventPlayer.currentEvent);

            if (Events.IsTeamEvent(manager.config.eventType) && ins.configData.EventSettings.AddToTeams)
            {
                RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);

                if (playerTeam != null)
                {
                    if ((eventPlayer.Team == Team.A && playerTeam != manager.playerTeamA) || (eventPlayer.Team == Team.B && playerTeam != manager.playerTeamB))
                    {
                        playerTeam.RemovePlayer(player.userID);
                        manager.CheckValidTeams();
                        switch (eventPlayer.Team)
                        {
                            case Team.A:
                                manager.playerTeamA.AddPlayer(player);
                                break;
                            case Team.B:
                                manager.playerTeamB.AddPlayer(player);
                                break;
                        }
                    }
                }
            }

            player.SetPlayerFlag(BasePlayer.PlayerFlags.Wounded, false);
            player.metabolism.calories.value = player.metabolism.calories.max;
            player.metabolism.hydration.value = player.metabolism.hydration.max;
            player.metabolism.bleeding.value = 0;
            player.metabolism.radiation_level.value = 0;
            player.metabolism.radiation_poison.value = 0;
            player.metabolism.SendChangesToClient();
            
            player.SendNetworkUpdateImmediate();
            try { player.ClearEntityQueue(null); } catch { }

            eventPlayer.isDead = false;
            eventPlayer.invincibleUntil = Time.time + configData.EventSettings.InvincibilityTime;
            eventPlayer.AddToNetwork();

            manager.SpawnPlayer(eventPlayer);            
        }  
               
        public void RespawnPlayer(BasePlayer player)
        {
            UI.DestroyUI(player, ArenaUI.UIPanel.Death);
            UI.DestroyUI(player, ArenaUI.UIPanel.Respawn);
            UI.DestroyUI(player, ArenaUI.UIPanel.Class);
            ResetPlayer(player);
        }

        private void RotateDroppedItem(BaseEntity entity)
        {
            if (RotatingPickups && configData.EventSettings.UseRotator)
            {
                NextTick(() =>
                {
                    if (entity != null && entity is WorldItem)
                        RotatingPickups.Call("AddItemRotator", new object[] { entity as WorldItem, true });
                });
            }
        }

        private bool IsPlayerDead(BasePlayer player)
        {
            EventPlayer eventPlayer = ToEventPlayer(player);
            if (eventPlayer != null)
                return eventPlayer.isDead;
            return false;
        }
        #endregion       

        #region Zone Management
        private void OnExitZone(string zoneId, BasePlayer player)
        {
            if (player == null)
                return;

            //ArenaEvents.NPCSurvival.NPCController npcController = player.GetComponent<ArenaEvents.NPCSurvival.NPCController>();
            //if (npcController != null)
            //{
            //    npcController.OnNPCLeaveZone(zoneId);
            //    return;
            //}

            EventPlayer eventPlayer = ToEventPlayer(player);

            if (eventPlayer == null || eventPlayer.isDead)
                return;

            EventManager manager = FindManagerByName(eventPlayer.currentEvent);

            if (manager != null && zoneId == manager.config.zoneId)
            {
                eventPlayer.isOOB = true;
                if (!killTimers.ContainsKey(player.userID))
                {                    
                    int time = 12;
                    killTimers.Add(player.userID, timer.Repeat(1, time, () =>
                    {
                        if (eventPlayer != null && player == null)
                        {
                            FindManagerByName(eventPlayer.currentEvent).LeaveEvent(eventPlayer);
                            return;
                        }

                        if (eventPlayer == null || player == null)
                            return;

                        if (eventPlayer.isDead || eventPlayer.Player.IsSpectating() || eventPlayer.isEliminated)
                            return;

                        if (eventPlayer.isOOB)
                        {
                            time--;

                            if (time == 10)
                                manager.BroadcastToPlayer(eventPlayer, msg("oob1", player.userID));

                            else if (time < 10)
                                manager.BroadcastToPlayer(eventPlayer, string.Format(msg("oob2", player.userID), time));

                            if (time == 0)
                            {
                                Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", (player.transform.position));
                                if (manager.status == EventStatus.Started)
                                    manager.OnPlayerDeath(eventPlayer, null);
                                else manager.SpawnPlayer(eventPlayer, false);                                
                            }
                        }
                    }));
                }
            }
        }

        private void OnEnterZone(string zoneId, BasePlayer player)
        {
            //ArenaEvents.NPCSurvival.NPCController npcMonitor = player.GetComponent<ArenaEvents.NPCSurvival.NPCController>();
            //if (npcMonitor != null)
            //{
            //    npcMonitor.OnNPCEnterZone(zoneId);
            //    return;
            //}

            EventPlayer eventPlayer = ToEventPlayer(player);

            if (eventPlayer == null || eventPlayer.isDead)
                return;

            EventManager manager = FindManagerByName(eventPlayer.currentEvent);
            if (manager != null && zoneId == manager.config.zoneId)
            {
                eventPlayer.isOOB = false;
                if (killTimers.ContainsKey(player.userID))
                {
                    killTimers[player.userID].Destroy();
                    killTimers.Remove(player.userID);
                }
            }
        }
        #endregion

        #region File Validation
        public object ValidateEventConfig(ArenaData.EventConfig config)
        {
            object success;
            if (!Events.IgnoreKitSelection(config.eventType) && config.eventKits.Count == 0)
                return "You must set atleast 1 kit";
            if (config.minimumPlayers == 0)
                return "You must set the minimum players";
            if (config.maximumPlayers == 0)
                return "You must set the maximum players";
            if (Events.RequiresTimeOrKillLimit(config.eventType) && (config.timeLimit == 0 && config.killLimit == 0))
                return "You must set a match timer or kill limit";
            foreach (string kit in config.eventKits)
            {
                success = ValidateKit(kit);
                if (success is string)                
                    return $"Invalid kit: {kit}";                
            }
            success = ValidateSpawnFile(config.teamA.spawnfile);
            if (success is string)
                return $"Invalid spawn file: {config.teamA.spawnfile}";

            if (Events.IsTeamEvent(config.eventType))
            {
                success = ValidateSpawnFile(config.teamB.spawnfile);
                if (success is string)
                    return $"Invalid second spawn file: {config.teamB.spawnfile}";
            }
            
            if (additionalParameters.ContainsKey(config.eventType))
            {
                if (config.additionalParameters == null || config.additionalParameters.Count == 0)
                    return "Event specific parameters not found in this event config!";

                foreach (AdditionalParamData parameter in additionalParameters[config.eventType])
                {
                    if (parameter.isRequired && (!config.additionalParameters.ContainsKey(parameter.field) || config.additionalParameters[parameter.field] == null))
                        return $"You must set a value for the parameter {parameter.field}";
                }
            }

            success = ValidateZoneID(config.zoneId);
            if (success is string)
                return $"Invalid zone ID: {config.zoneId}";

            return null;
        }

        public object ValidateSpawnFile(string name)
        {
            object success = Spawns?.Call("GetSpawnsCount", name);
            if (success is string)
                return (string)success;
            else return null;
        }

        public object ValidateZoneID(string name)
        {
            object success = ZoneManager?.Call("CheckZoneID", name);
            if (name is string && !string.IsNullOrEmpty((string)name))
                return null;
            else return $"Zone \"{name}\" does not exist!";
        }

        public object ValidateKit(string name)
        {
            object success = Kits?.Call("isKit", name);
            if ((success is bool))
                if (!(bool)success)
                    return $"Kit \"{name}\" does not exist!";
            return null;
        }

        public void AddNewEvent(ArenaData.EventConfig config)
        {
            arenaData.events.Add(config.eventName, config);
            SaveData();
            InitializeEvent(config.eventName, config);
        }

        public void RemoveEvent(string eventName)
        {
            arenaData.events.Remove(eventName);
            SaveData();
        }

        public void UpdateEvent(string eventName, ArenaData.EventConfig config)
        {
            arenaData.events[eventName] = config;
            SaveData();
            InitializeEvent(eventName, config);
        }
        #endregion

        #region NPC Integration
        private void OnUseNPC(BasePlayer npc, BasePlayer player)
        {
            if (player == null || npc == null) return;

            if (configData.LobbySettings.NPCIDs.Contains(npc.UserIDString))
            {
                OpenMenu(player);
            }
        }
        #endregion

        #region Helpers
        public enum RewardType { Kill, Headshot, Win }

        public void IssueReward(ulong playerId, RewardType rewardType, ArenaData.EventConfig config)
        {
            int amount = rewardType == RewardType.Kill && configData.RewardSettings.Kills ? (config.killRewardOverride == -1 ? configData.RewardSettings.KillAmount : config.killRewardOverride) : 
                rewardType == RewardType.Win && configData.RewardSettings.Wins ? (config.winRewardOverride == -1 ? configData.RewardSettings.WinAmount : config.winRewardOverride) : 
                rewardType == RewardType.Headshot && configData.RewardSettings.Headshots ? (config.headshotRewardOverride == -1 ? configData.RewardSettings.HeadshotAmount : config.headshotRewardOverride) : 0;

            if (amount == 0)
                return;

            switch (configData.RewardSettings.Type.ToLower())
            {
                case "serverrewards":
                    ServerRewards?.Call("AddPoints", playerId.ToString(), amount);
                    return;
                case "economics":
                    Economics?.Call("Deposit", playerId.ToString(), (double)amount);
                    return;
                default:
                    break;
            }
        }

        public object IsEventPlayer(BasePlayer player) => ToEventPlayer(player) != null ? (object)true : null;

        private object isEventPlayer(BasePlayer player) => ToEventPlayer(player) != null ? (object)true : null;

        #endregion

        #region Lobby TP
        [ChatCommand("lobby")]
        private void cmdLobbyTP(BasePlayer player, string command, string[] args)
        {
            if (!configData.LobbySettings.TP.Enabled || string.IsNullOrEmpty(configData.LobbySettings.LobbySpawnfile))
                return;

            if (ToEventPlayer(player) != null)
            {
                player.ChatMessage(msg("notp.inevent", player.userID));
                return;
            }

            if (pendingTP.ContainsKey(player.userID))
            {
                player.ChatMessage(msg("notp.pending", player.userID));
                return;
            }

            double time;
            if (cooldownTP.TryGetValue(player.userID, out time))
            {
                if (time > Time.realtimeSinceStartup)
                {
                    player.ChatMessage(string.Format(msg("notp.cooldown", player.userID), Mathf.RoundToInt((float)time - Time.realtimeSinceStartup)));
                    return;
                }
            }

            string str = MeetsLobbyTPRequirements(player);
            if (!string.IsNullOrEmpty(str))
            {
                player.ChatMessage(str);
                return;
            }

            if (configData.LobbySettings.TP.Timer == 0)
            {
                SendToWaiting(player);
                return;
            }
            else
            {
                Action action = new Action(() => TeleportToLobby(player));
                player.Invoke(action, configData.LobbySettings.TP.Timer);
                pendingTP[player.userID] = action;
                player.ChatMessage(string.Format(msg("tp.pending", player.userID), configData.LobbySettings.TP.Timer));
            }
        }

        [ChatCommand("lobbyc")]
        private void cmdLobbyCancel(BasePlayer player, string command, string[] args)
        {
            if (!configData.LobbySettings.TP.Enabled || string.IsNullOrEmpty(configData.LobbySettings.LobbySpawnfile))
                return;

            if (!HasPendingTeleports(player))
            {
                player.ChatMessage(msg("tp.notpending", player.userID));
                return;
            }

            CancelPendingTeleports(player);
            player.ChatMessage(msg("tp.cancelled", player.userID));
        }

        private void TeleportToLobby(BasePlayer player)
        {
            pendingTP.Remove(player.userID);

            string str = MeetsLobbyTPRequirements(player);
            if (!string.IsNullOrEmpty(str))
            {
                player.ChatMessage(str);
                return;
            }

            SendToWaiting(player);            

            if (configData.LobbySettings.TP.Cooldown > 0)
                cooldownTP[player.userID] = Time.realtimeSinceStartup + configData.LobbySettings.TP.Cooldown;
        }

        private bool HasPendingTeleports(BasePlayer player) => pendingTP.ContainsKey(player.userID);

        private void CancelPendingTeleports(BasePlayer player)
        {
            if (player == null)
                return;

            if (HasPendingTeleports(player))
            {
                player.CancelInvoke(pendingTP[player.userID]);
                pendingTP.Remove(player.userID);
            }
        }

        private string MeetsLobbyTPRequirements(BasePlayer player)
        {
            if (!string.IsNullOrEmpty(configData.LobbySettings.LobbyZoneID) && (bool)ZoneManager.Call("isPlayerInZone", configData.LobbySettings.LobbyZoneID, player))            
                return msg("alreadyinlobby", player.userID);            

            if (!configData.LobbySettings.TP.AllowTeleportFromBuildBlock && !player.CanBuild())
                return msg("notp.buildblocked", player.userID);

            if (!configData.LobbySettings.TP.AllowTeleportFromCargoShip && player.GetParentEntity() is CargoShip)
                return msg("notp.cargoship", player.userID);

            if (!configData.LobbySettings.TP.AllowTeleportFromHotAirBalloon && player.GetParentEntity() is HotAirBalloon)
                return msg("notp.hotairballoon", player.userID);

            if (!configData.LobbySettings.TP.AllowTeleportFromMounted && player.isMounted)
                return msg("notp.mounted", player.userID);

            if (!configData.LobbySettings.TP.AllowTeleportFromOilRig && IsNearOilRig(player))
                return msg("notp.oilrig", player.userID);

            if (!configData.LobbySettings.TP.AllowTeleportWhilstBleeding && player.metabolism.bleeding.value > 0)
                return msg("notp.bleeding", player.userID);

            if (IsRaidBlocked(player))
                return msg("notp.raidblocked", player.userID);

            if (IsCombatBlocked(player))
                return msg("notp.combatblocked", player.userID);

            string str = Interface.Oxide.CallHook("CanTeleport", player) as string;
            if (str != null)
                return str;

            return string.Empty;
        }

        private bool IsNearOilRig(BasePlayer player)
        {
            for (int i = 0; i < TerrainMeta.Path.Monuments.Count; i++)
            {
                MonumentInfo monumentInfo = TerrainMeta.Path.Monuments[i];

                if (monumentInfo.gameObject.name.Contains("oilrig"))
                {
                    if (Vector3Ex.Distance2D(player.transform.position, monumentInfo.transform.position) <= 100f)
                        return true;
                }
            }

            return false;
        }

        private bool IsRaidBlocked(BasePlayer player)
        {
            if (NoEscape)
            {
                if (configData.LobbySettings.TP.AllowTeleportWhilstRaidBlocked)
                {
                    bool success = NoEscape.Call<bool>("IsRaidBlocked", player);
                    if (success)
                        return true;
                }
            }
            return false;
        }

        private bool IsCombatBlocked(BasePlayer player)
        {
            if (NoEscape)
            {
                if (configData.LobbySettings.TP.AllowTeleportWhilstCombatBlocked)
                {
                    bool success = NoEscape.Call<bool>("IsCombatBlocked", player);
                    if (success)
                        return true;
                }
            }
            return false;
        }
        #endregion

        #region Commands
        private void cmdEventMenu(BasePlayer player, string command, string[] args) => OpenMenu(player);

        private void OpenMenu(BasePlayer player)
        {
            if (!isInitialized)
                return;

            EventPlayer eventPlayer = ToEventPlayer(player);
            if (eventPlayer == null)
            {
                if (!permission.UserHasPermission(player.UserIDString, "arena.admin") && configData.LobbySettings.LobbyEnabled)
                {
                    if (!string.IsNullOrEmpty(configData.LobbySettings.LobbyZoneID) && !(bool)ZoneManager.Call("isPlayerInZone", configData.LobbySettings.LobbyZoneID, player))
                    {
                        SendReply(player, msg("notinlobby"));

                        if (configData.LobbySettings.TP.Enabled && !string.IsNullOrEmpty(configData.LobbySettings.LobbySpawnfile))
                            SendReply(player, msg("lobbytp"));

                        return;
                    }
                }
                if (events.Count > 0)
                    UI.OpenMainMenu(player, events.First().Value.config.eventType);
                else SendReply(player, msg("noevents"));
            }
            else
            {
                if (permission.UserHasPermission(player.UserIDString, "arena.blacklisted"))
                {
                    SendReply(player, msg("blacklisted"));
                    return;
                }
                UI.OpenPlayerMenu(player);
            }
        }

        [ChatCommand("leave")]
        private void cmdLeaveEvent(BasePlayer player, string command, string[] args)
        {
            if (!isInitialized)
                return;

            EventPlayer eventPlayer = ToEventPlayer(player);
            if (eventPlayer == null)
                return;

            EventManager manager = FindManagerByName(eventPlayer.currentEvent);
            if (manager != null)
                manager.LeaveEvent(eventPlayer);
        }

        [ChatCommand("arenateleporter")]
        private void cmdArenaTeleporter(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "arena.admin"))
                return;

            if (args.Length == 0)
            {
                SendReply(player, "<color=#ce422b>/arenateleporter add \"event name\"</color> - Create a arena teleporter for the specified event on your position");
                SendReply(player, "<color=#ce422b>/arenateleporter remove \"event name\"</color> - Remove the arena teleporter for the specified event");
                return;
            }

            switch (args[0].ToLower())
            {
                case "add":
                    {
                        if (args.Length < 2)
                        {
                            player.ChatMessage("Invalid syntax! <color=#ce422b>/arenateleporter add \"event name\"</color>");
                            return;
                        }

                        string eventName = args[1];
                        if (!arenaData.events.ContainsKey(eventName))
                        {
                            player.ChatMessage($"Unable to find an event with the name {eventName}");
                            return;
                        }

                        if (arenaData.events[eventName].arenaTeleporter != null)
                        {
                            player.ChatMessage($"This event already has a teleporter. You need to remove it before continuing");
                            return;
                        }

                        arenaData.events[eventName].arenaTeleporter = new ArenaData.EventConfig.ArenaTeleporter()
                        {
                            x = player.transform.position.x,
                            y = player.transform.position.y + 1.5f,
                            z = player.transform.position.z
                        };

                        SaveData();

                        if (!configData.LobbySettings.LobbyEnabled || string.IsNullOrEmpty(configData.LobbySettings.LobbySpawnfile))
                        {
                            player.ChatMessage($"You have successfully setup a teleporter for event {eventName}, however teleporters require the lobby to be setup and enabled");
                            return;
                        }

                        if (events.ContainsKey(eventName))
                        {
                            events[eventName].CreateArenaTeleporter();
                            player.ChatMessage($"You have successfully setup a teleporter for event {eventName}");
                            return;
                        }

                        player.ChatMessage($"You have successfully setup a teleporter for event {eventName}, however the event is not currently running so it hasn't been created");
                    }
                    return;
                case "remove":
                    {
                        if (args.Length < 2)
                        {
                            player.ChatMessage("Invalid syntax! <color=#ce422b>/arenateleporter remove \"event name\"</color>");
                            return;
                        }

                        string eventName = args[1];
                        if (!arenaData.events.ContainsKey(eventName))
                        {
                            player.ChatMessage($"Unable to find an event with the name {eventName}");
                            return;
                        }

                        if (arenaData.events[eventName].arenaTeleporter == null)
                        {
                            player.ChatMessage($"This event does not have a teleporter");
                            return;
                        }

                        arenaData.events[eventName].arenaTeleporter = null;

                        SaveData();

                        if (events.ContainsKey(eventName))                        
                            events[eventName].DestroyArenaTeleporter();
                        
                        player.ChatMessage($"You have removed the teleporter for event {eventName}");
                    }
                    return;
                default:
                    player.ChatMessage("Invalid syntax");
                    break;
            }

        }

        [ChatCommand("startevent")]
        private void cmdStartEvent(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "arena.admin"))
                return;

            if (args.Length == 0)
            {
                SendReply(player, "<color=#ce422b>/startevent \"event name\"</color> - Force start a event");
                return;
            }

            if (!events.ContainsKey(args[0]))
            {
                SendReply(player, "Invalid event name entered");
                return;
            }

            EventManager manager = events[args[0]];
            if (manager.status != EventStatus.Pending)
            {
                SendReply(player, "This event has already started, or is still finishing");
                return;
            }

            if (manager.eventPlayers.Count == 0)
            {
                SendReply(player, "This event has no players");
                return;
            }

            manager.StartMatch();
            SendReply(player, $"Event <color=#ce422b>{args[0]}</color> has been force started!");
        }

        [ConsoleCommand("arena.enable")]
        private void ccmdEnableEvent(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
                return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                SendReply(arg, "arena.enable <eventname> - Enable a previously disabled event");
                return;
            }

            string eventName = arg.Args[0];

            if (!arenaData.events.ContainsKey(eventName))
            {
                SendReply(arg, "Invalid event name entered");
                return;
            }

            if (events.ContainsKey(eventName))
            {
                SendReply(arg, "This event is already running");
                return;
            }

            if (!arenaData.events[eventName].isDisabled)
            {
                SendReply(arg, "This event is not disabled");
                return;
            }

            arenaData.events[eventName].isDisabled = false;
            SaveData();

            InitializeEvent(eventName, arenaData.events[eventName]);

            SendReply(arg, $"{eventName} has been enabled");
        }

        [ConsoleCommand("arena.disable")]
        private void ccmdDisableEvent(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
                return;

            if (arg.Args == null || arg.Args.Length == 0)
            {
                SendReply(arg, "arena.disable <eventname> - Disable a event");
                return;
            }

            string eventName = arg.Args[0];

            if (!arenaData.events.ContainsKey(eventName))
            {
                SendReply(arg, "Invalid event name entered");
                return;
            }
            
            if (arenaData.events[eventName].isDisabled)
            {
                SendReply(arg, "This event is already disabled");
                return;
            }

            arenaData.events[eventName].isDisabled = true;
            SaveData();

            EventManager eventManager = FindManagerByName(eventName);
            if (eventManager != null)
            {
                eventManager.EndMatch();
                eventManager.EjectAllPlayers();
                events.Remove(eventName);
                UnityEngine.Object.Destroy(eventManager, 4f);
            }

            SendReply(arg, $"{eventName} has been disabled");
        }

        private void TemporarilyDisableEvent(string eventName, int time)
        {
            EventManager eventManager = FindManagerByName(eventName);
            if (eventManager != null)
            {
                eventManager.EndMatch();
                eventManager.EjectAllPlayers();
                events.Remove(eventName);
                UnityEngine.Object.Destroy(eventManager, 4f);
            }

            timer.In(time, () =>
            {
                if (!arenaData.events.ContainsKey(eventName))
                    return;

                if (InitializeEvent(eventName, arenaData.events[eventName]))
                {
                    foreach (BasePlayer player in BasePlayer.activePlayerList)
                        player.ChatMessage(string.Format(msg("eventEnabled", player.userID), eventName));
                }
            });
        }

        [ConsoleCommand("arena.startup")]
        private void ccmdStartUp(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
                return;

            if (isInitialized || events.Count > 0)
            {
                SendReply(arg, "Events are already running");
                return;
            }

            CheckLoadedDependencies();            
        }

        [ConsoleCommand("arena.shutdown")]
        private void ccmdShutdown(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null)
                return;

            isInitialized = false;

            for (int i = 0; i < events.Count; i++)
            {
                EventManager eventManager = events.ElementAt(i).Value;
                eventManager.EndMatch();
                eventManager.EjectAllPlayers();
                UnityEngine.Object.Destroy(eventManager, 4f);
            }

            SendReply(arg, "All events have been shutdown, cleaning up");
            timer.In(5, () =>
            {
                events.Clear();
                SendReply(arg, "Event shutdown complete");
            });
        }
        #endregion

        #region Config        
        public ConfigData configData;

        public class ConfigData
        {
            [JsonProperty(PropertyName = "Game Timers")]
            public Timers GameTimers { get; set; }

            [JsonProperty(PropertyName = "Server Settings")]
            public ServerOptions ServerSettings { get; set; }

            [JsonProperty(PropertyName = "Event Settings")]
            public EventOptions EventSettings { get; set; }

            [JsonProperty(PropertyName = "Lobby Settings")]
            public LobbyInfo LobbySettings { get; set; }

            [JsonProperty(PropertyName = "Reward Settings")]
            public RewardOptions RewardSettings { get; set; }

            public class Timers
            {
                [JsonProperty(PropertyName = "Match pre-start timer per event type (seconds)")]
                public Dictionary<string, int> MatchPrestart { get; set; }     
                
                [JsonProperty(PropertyName = "Player respawn timer per event type (seconds)")]
                public Dictionary<string, int> RespawnTimer { get; set; }

                [JsonProperty(PropertyName = "Display game timers")]
                public bool ShowGameTimer { get; set; }

                [JsonProperty(PropertyName = "Amount of time between rounds (when Auto-start a new game when event ends is enabled) (seconds)")]
                public int TimeBetweenRounds { get; set; }
            }
            
            public class LobbyInfo
            {
                [JsonProperty(PropertyName = "Force event access from a physical lobby")]
                public bool LobbyEnabled { get; set; }

                [JsonProperty(PropertyName = "Event lobby spawnfile")]
                public string LobbySpawnfile { get; set; }

                [JsonProperty(PropertyName = "Event lobby zone ID")]
                public string LobbyZoneID { get; set; }

                [JsonProperty(PropertyName = "Event lobby kit (only applies if event only server)")]
                public string LobbyKit { get; set; }

                [JsonProperty(PropertyName = "Lobby NPC ID's")]
                public List<string> NPCIDs { get; set; }

                [JsonProperty(PropertyName = "Lobby Teleportation")]
                public LobbyTP TP { get; set; }

                public class LobbyTP
                {
                    [JsonProperty(PropertyName = "Allow teleportation to the lobby (Requires a lobby spawn file)")]
                    public bool Enabled { get; set; }

                    [JsonProperty(PropertyName = "Allow teleportation if Raid Blocked (NoEscape)")]
                    public bool AllowTeleportWhilstRaidBlocked { get; set; }

                    [JsonProperty(PropertyName = "Allow teleportation if Combat Blocked (NoEscape)")]
                    public bool AllowTeleportWhilstCombatBlocked { get; set; }

                    [JsonProperty(PropertyName = "Allow teleportation if bleeding")]
                    public bool AllowTeleportWhilstBleeding { get; set; }

                    [JsonProperty(PropertyName = "Allow teleportation if building blocked")]
                    public bool AllowTeleportFromBuildBlock { get; set; }

                    [JsonProperty(PropertyName = "Allow teleportation if on the CargoShip")]
                    public bool AllowTeleportFromCargoShip { get; set; }

                    [JsonProperty(PropertyName = "Allow teleportation if in a HotAirBalloon")]
                    public bool AllowTeleportFromHotAirBalloon { get; set; }

                    [JsonProperty(PropertyName = "Allow teleportation if on the OilRig")]
                    public bool AllowTeleportFromOilRig { get; set; }

                    [JsonProperty(PropertyName = "Allow teleportation if mounted")]
                    public bool AllowTeleportFromMounted { get; set; }

                    [JsonProperty(PropertyName = "Teleportation countdown timer")]
                    public int Timer { get; set; }

                    [JsonProperty(PropertyName = "Teleportation cooldown timer")]
                    public int Cooldown { get; set; }
                }
            }
            public class ServerOptions
            {
                [JsonProperty(PropertyName = "This is an event only server?")]
                public bool EventOnly { get; set; }   
                
                [JsonProperty(PropertyName = "Disable Airdrops (Server wide)")]
                public bool NoAirdrops { get; set; }

                [JsonProperty(PropertyName = "Disable Helicopters (Server wide)")]
                public bool NoHelicopters { get; set; }

                [JsonProperty(PropertyName = "Disable Animals (Server wide)")]
                public bool NoAnimals { get; set; }

                [JsonProperty(PropertyName = "Use inbuilt chat manager")]
                public bool UseChat { get; set; }

                [JsonProperty(PropertyName = "Chat command to open the menu")]
                public string ChatCommand { get; set; }

                [JsonProperty(PropertyName = "Open event menu when a player connects (Requires event only server)")]
                public bool MenuOnJoin { get; set; }
            }
            public class EventOptions
            {
                [JsonProperty(PropertyName = "Auto-start a new game when event ends")]
                public bool Continual { get; set; }

                [JsonProperty(PropertyName = "Kick players from the event when they die (Survival events only)")]
                public bool DeathKick { get; set; }

                [JsonProperty(PropertyName = "Drop weapon on death")]
                public bool DropWeapon { get; set; }

                [JsonProperty(PropertyName = "Drop ammunition on death")]
                public bool DropAmmo { get; set; }

                [JsonProperty(PropertyName = "Drop backpack on death")]
                public bool DropBackpack { get; set; }

                [JsonProperty(PropertyName = "Lock player inventory during events")]
                public bool LockInventory { get; set; }

                [JsonProperty(PropertyName = "Disable events when zone is in use by another event")]
                public bool DisableUsedZones { get; set; }

                [JsonProperty(PropertyName = "Add rotator to dropped items (Requires RotatingPickups)")]
                public bool UseRotator { get; set; }

                [JsonProperty(PropertyName = "Require player to be naked to enter events")]
                public bool RequireNaked { get; set; }

                [JsonProperty(PropertyName = "Number of kills to activate killstreaks")]
                public int[] Killstreaks { get; set; }

                [JsonProperty(PropertyName = "Display kill feed in chat")]
                public bool UseKillFeed { get; set; }

                [JsonProperty(PropertyName = "Print event winners in chat")]
                public bool BroadcastWinners { get; set; }

                [JsonProperty(PropertyName = "Broadcast to chat when a player joins an event")]
                public bool BroadcastJoiners { get; set; }

                [JsonProperty(PropertyName = "Broadcast to chat when players are waiting in a event for more players")]
                public bool BroadcastWaiting { get; set; }

                [JsonProperty(PropertyName = "Blacklisted commands for event players")]
                public string[] CommandBlacklist { get; set; }

                [JsonProperty(PropertyName = "Send players to the arena and wait for the event to start when they join")]
                public bool SendToArenaOnJoin { get; set; }

                [JsonProperty(PropertyName = "Create and add players to Rusts team system for team based events")]
                public bool AddToTeams { get; set; }

                [JsonProperty(PropertyName = "Invincibility time after respawn (seconds)")]
                public float InvincibilityTime { get; set; }

                [JsonProperty(PropertyName = "Disable events after a match has been played (event name, seconds to disable)")]
                public Dictionary<string, int> DisableAfterPlayed { get; set; }

                [JsonProperty(PropertyName = "Automatic Health Restore Options")]
                public HealthRestore Health { get; set; }

                public class HealthRestore
                {
                    [JsonProperty(PropertyName = "Enable automatic health restoration")]
                    public bool Enabled { get; set; }

                    [JsonProperty(PropertyName = "Start restoring if no damage taken for x seconds")]
                    public float RestoreAfter { get; set; }

                    [JsonProperty(PropertyName = "Amount of health to restore every second")]
                    public float Amount { get; set; }
                }
            }
            public class RewardOptions
            {
                [JsonProperty(PropertyName = "Issue rewards for kills")]
                public bool Kills { get; set; }

                [JsonProperty(PropertyName = "Amount rewarded for kills")]
                public int KillAmount { get; set; }

                [JsonProperty(PropertyName = "Issue rewards for wins")]
                public bool Wins { get; set; }

                [JsonProperty(PropertyName = "Amount rewarded for wins")]
                public int WinAmount { get; set; }

                [JsonProperty(PropertyName = "Issue rewards for headshots")]
                public bool Headshots { get; set; }

                [JsonProperty(PropertyName = "Amount rewarded for headshots")]
                public int HeadshotAmount { get; set; }

                [JsonProperty(PropertyName = "Reward type (ServerRewards, Economics)")]
                public string Type { get; set; }
            }
            public VersionNumber Version { get; set; }
        }
               
        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(configData, true);
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                EventSettings = new ConfigData.EventOptions
                {
                    AddToTeams = false,
                    BroadcastWinners = false,
                    BroadcastJoiners = false,
                    BroadcastWaiting = false,
                    Continual = true,
                    CommandBlacklist = new string[] { "s", "tp" },
                    DeathKick = false,
                    DisableAfterPlayed = new Dictionary<string, int>(),
                    DropAmmo = true,
                    DropWeapon = false,
                    DropBackpack = false,
                    DisableUsedZones = true,
                    InvincibilityTime = 3f,
                    Killstreaks = new int[] { 5, 10, 15, 20, 25, 30 },
                    LockInventory = false,
                    RequireNaked = false,
                    SendToArenaOnJoin = true,
                    UseRotator = true,
                    UseKillFeed = true   ,
                    Health = new ConfigData.EventOptions.HealthRestore
                    {
                        Enabled = false,
                        RestoreAfter = 5f,
                        Amount = 2.5f
                    }
                },
                ServerSettings = new ConfigData.ServerOptions
                {
                    EventOnly = true,
                    NoAirdrops = true,
                    NoAnimals = true,
                    NoHelicopters = true,
                    UseChat = true,
                    ChatCommand = "menu",
                    MenuOnJoin = false
                },
                GameTimers = new ConfigData.Timers
                {
                    MatchPrestart = new Dictionary<string, int>
                    {
                        ["Free For All"] = 30,
                        ["Team Deathmatch"] = 30,
                        ["Survival"] = 30,
                        ["Team Survival"] = 30,
                        ["GunGame"] = 30,
                        ["One in the Chamber"] = 30,
                        ["Capture the Flag"] = 30,
                        ["NPC Survival"] = 30,
                        //["Slasher"] = 30,
                        ["Infected"] = 30
                    },
                    RespawnTimer = new Dictionary<string, int>
                    {
                        ["Free For All"] = 5,
                        ["Team Deathmatch"] = 5,
                        ["Survival"] = 5,
                        ["Team Survival"] = 5,
                        ["GunGame"] = 5,
                        ["One in the Chamber"] = 5,
                        ["Capture the Flag"] = 5,
                        ["NPC Survival"] = 5,
                        //["Slasher"] = 5,
                        ["Infected"] = 5
                    },
                    ShowGameTimer = true,
                    TimeBetweenRounds = 30
                },
                LobbySettings = new ConfigData.LobbyInfo
                {
                    LobbyEnabled = true,
                    LobbySpawnfile = "lobbyspawns",
                    LobbyZoneID = "",
                    NPCIDs = new List<string>(),
                    TP = new ConfigData.LobbyInfo.LobbyTP
                    {
                        AllowTeleportFromBuildBlock = false,
                        AllowTeleportFromCargoShip = false,
                        AllowTeleportFromHotAirBalloon = false,
                        AllowTeleportFromMounted = false,
                        AllowTeleportFromOilRig = false,
                        AllowTeleportWhilstBleeding = false,
                        AllowTeleportWhilstCombatBlocked = false,
                        AllowTeleportWhilstRaidBlocked = false,
                        Enabled = false,
                        Timer = 15,
                        Cooldown = 600
                    }
                },
                RewardSettings = new ConfigData.RewardOptions
                {
                    Kills = false,
                    KillAmount = 1,
                    Wins = false,
                    WinAmount = 5,
                    Headshots = false,
                    HeadshotAmount = 2,
                    Type = "ServerRewards"
                },
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();
            if (configData.Version < new VersionNumber(0, 1, 90))
            {
                configData.EventSettings.DisableUsedZones = baseConfig.EventSettings.DisableUsedZones;
            }

            if (configData.Version<new VersionNumber(0, 1, 100))
            {
                configData.RewardSettings.Headshots = baseConfig.RewardSettings.Headshots;
                configData.RewardSettings.HeadshotAmount = baseConfig.RewardSettings.HeadshotAmount;
            }

            if (configData.Version < new VersionNumber(0, 1, 101))
            {
                configData.EventSettings.DropBackpack = baseConfig.EventSettings.DropBackpack;
                configData.EventSettings.SendToArenaOnJoin = baseConfig.EventSettings.SendToArenaOnJoin;
            }

            if (configData.Version < new VersionNumber(0, 1, 108))
            {
                configData.GameTimers.MatchPrestart = baseConfig.GameTimers.MatchPrestart;
            }

            if (configData.Version< new VersionNumber(0, 1, 115))
            {
                configData.GameTimers.MatchPrestart.Add("Slasher", 30);
                configData.GameTimers.RespawnTimer.Add("Slasher", 5);
            }

            if (configData.Version < new VersionNumber(0, 1, 116))
            {
                configData.EventSettings.BroadcastJoiners = baseConfig.EventSettings.BroadcastJoiners;
            }

            if (configData.Version < new VersionNumber(0, 1, 126))
                configData.LobbySettings.TP = baseConfig.LobbySettings.TP;

            if (configData.Version < new VersionNumber(0, 1, 127))
                configData.LobbySettings.LobbyKit = string.Empty;

            if (configData.Version < new VersionNumber(0, 1, 128))
            {
                configData.GameTimers.MatchPrestart.Add("Infected", 30);
                configData.GameTimers.RespawnTimer.Add("Infected", 5);
            }

            if (configData.Version < new VersionNumber(0, 1, 134))
                configData.GameTimers.TimeBetweenRounds = 30;

            if (configData.Version < new VersionNumber(0, 1, 137))
                configData.EventSettings.InvincibilityTime = 3f;

            if (configData.Version < new VersionNumber(0, 1, 142))
                configData.EventSettings.DisableAfterPlayed = new Dictionary<string, int>();

            if (configData.Version < new VersionNumber(0, 1, 144))
                configData.EventSettings.Health = baseConfig.EventSettings.Health;

            configData.Version = Version;
            PrintWarning("Config update completed!");
        }
        #endregion

        #region Data Management
        private void SaveData() => eventData.WriteObject(arenaData);

        private void SaveRestoreData() => restorationData.WriteObject(restoreData);

        private void LoadData()
        {
            try
            {
                arenaData = eventData.ReadObject<ArenaData>();
            }
            catch
            {
                arenaData = new ArenaData();
            }
            try
            {
                restoreData = restorationData.ReadObject<RestoreData>();
            }
            catch
            {
                restoreData = new RestoreData();
            }
        }        
        
        public class ArenaData
        {
            public Dictionary<string, EventConfig> events = new Dictionary<string, EventConfig>();

            public class EventConfig
            {                
                public string eventType = string.Empty, eventName = string.Empty, zoneId = string.Empty, eventIcon = string.Empty, description = string.Empty, permission = string.Empty;
                public int timeLimit = 0, killLimit = 0, minimumPlayers = 0, maximumPlayers = 0, playersToStart = 1, killRewardOverride = -1, headshotRewardOverride = -1, winRewardOverride = -1;
                public bool useClassSelector = false, ffEnabled = false, isDisabled = false, switchTeamOnRepeat = false;
                public List<string> eventKits = new List<string>();
                public TeamEntry teamA = new TeamEntry(), teamB = new TeamEntry();
                public Dictionary<string, object> additionalParameters = new Dictionary<string, object>();
                public ArenaTeleporter arenaTeleporter;

                public class TeamEntry
                {
                    public string kit = string.Empty, name = string.Empty, color = string.Empty, spawnfile = string.Empty;

                    public string GetFormattedName()
                    {
                        if (string.IsNullOrEmpty(name))
                            return null;
                        if (string.IsNullOrEmpty(color))
                            return name;
                        return $"<color={color}>{name}</color>";
                    }
                }  
                
                public class ArenaTeleporter
                {
                    public float x, y, z;

                    [JsonIgnore]
                    public Vector3 Position { get { return new Vector3(x, y, z); } }
                }
            }            
        } 
        
        public class RestoreData
        {
            public Hash<ulong, PlayerData> restoreData = new Hash<ulong, PlayerData>();

            public void AddData(BasePlayer player)
            {
                restoreData[player.userID] = new PlayerData(player);
            }

            public void RemoveData(ulong playerId)
            {
                if (HasRestoreData(playerId))
                    restoreData.Remove(playerId);
            }

            public bool HasRestoreData(ulong playerId) => restoreData.ContainsKey(playerId);

            public void RestorePlayer(BasePlayer player)
            {
                if (isUnloading)
                {
                    player.Die();
                    player.ChatMessage("<color=#ce422b>The plugin has been unloaded!</color> We are unable to restore you at this time, when the plugin has been reloaded you will be restored to your previous state");
                    return;
                }

                PlayerData playerData;
                if (restoreData.TryGetValue(player.userID, out playerData))
                {
                    StripInventory(player);

                    if (player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
                    {
                        ins.timer.Once(1, () => RestorePlayer(player));
                        return;
                    }

                    ins.NextTick(() =>
                    {
                        if (player == null || playerData == null)
                            return;

                        if (playerData.teamId != 0U)
                        {
                            RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(playerData.teamId);
                            if (playerTeam != null)
                            {
                                if (playerTeam.members.Count >= RelationshipManager.maxTeamSize)
                                {
                                    player.ChatMessage("Unable to restore you back in to your old team as the team is now full");
                                }
                                else playerTeam.AddPlayer(player);
                            }
                            else player.ChatMessage("Unable to restore you back in to your old team as it no longer exists");
                        }

                        playerData.SetStats(player);
                        ins.MovePosition(player, playerData.GetPosition(), true);
                        RestoreAllItems(player, playerData);
                    });
                }
            }

            private void RestoreAllItems(BasePlayer player, PlayerData playerData)
            {
                if (player == null || !player.IsConnected)
                    return;
                  
                if (RestoreItems(player, playerData.containerBelt, "belt") && RestoreItems(player, playerData.containerWear, "wear") && RestoreItems(player, playerData.containerMain, "main"))
                    RemoveData(player.userID);
            }

            private bool RestoreItems(BasePlayer player, ItemData[] itemData, string type)
            {
                ItemContainer container = type == "belt" ? player.inventory.containerBelt : type == "wear" ? player.inventory.containerWear : player.inventory.containerMain;

                for (int i = 0; i < itemData.Length; i++)
                {
                    Item item = CreateItem(itemData[i]);
                    item.position = itemData[i].position;
                    item.SetParent(container);
                }
                return true;
            }

            private Item CreateItem(ItemData itemData)
            {
                Item item = ItemManager.CreateByItemID(itemData.itemid, itemData.amount, itemData.skin);
                item.condition = itemData.condition;
                item.maxCondition = itemData.maxCondition;

                if (itemData.frequency > 0)
                {
                    ItemModRFListener rfListener = item.info.GetComponentInChildren<ItemModRFListener>();
                    if (rfListener != null)
                    {
                        PagerEntity pagerEntity = BaseNetworkable.serverEntities.Find(item.instanceData.subEntity) as PagerEntity;
                        if (pagerEntity != null)
                        {
                            pagerEntity.ChangeFrequency(itemData.frequency);
                            item.MarkDirty();
                        }
                    }
                }

                if (itemData.instanceData?.IsValid() ?? false)
                    itemData.instanceData.Restore(item);

                BaseProjectile weapon = item.GetHeldEntity() as BaseProjectile;
                if (weapon != null)
                {
                    if (!string.IsNullOrEmpty(itemData.ammotype))
                        weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(itemData.ammotype);
                    weapon.primaryMagazine.contents = itemData.ammo;
                }

                FlameThrower flameThrower = item.GetHeldEntity() as FlameThrower;
                if (flameThrower != null)
                    flameThrower.ammo = itemData.ammo;


                if (itemData.contents != null)
                {
                    foreach (ItemData contentData in itemData.contents)
                    {
                        Item newContent = ItemManager.CreateByItemID(contentData.itemid, contentData.amount);
                        if (newContent != null)
                        {
                            newContent.condition = contentData.condition;
                            newContent.MoveToContainer(item.contents);
                        }
                    }
                }
                return item;
            }

            public class PlayerData
            {
                public float[] stats;
                public float[] position;
                public ItemData[] containerMain;
                public ItemData[] containerWear;
                public ItemData[] containerBelt;
                public ulong teamId;

                public PlayerData() { }

                public PlayerData(BasePlayer player)
                {
                    stats = GetStats(player);
                    position = GetPosition(player.transform.position);
                    containerBelt = GetItems(player.inventory.containerBelt).ToArray();
                    containerMain = GetItems(player.inventory.containerMain).ToArray();
                    containerWear = GetItems(player.inventory.containerWear).ToArray();
                    teamId = player.currentTeam;
                }

                private IEnumerable<ItemData> GetItems(ItemContainer container)
                {
                    return container.itemList.Select(item => new ItemData
                    {
                        itemid = item.info.itemid,
                        amount = item.amount,
                        ammo = item.GetHeldEntity() is BaseProjectile ? (item.GetHeldEntity() as BaseProjectile).primaryMagazine.contents : item.GetHeldEntity() is FlameThrower ? (item.GetHeldEntity() as FlameThrower).ammo : 0,
                        ammotype = (item.GetHeldEntity() as BaseProjectile)?.primaryMagazine.ammoType.shortname ?? null,
                        position = item.position,
                        skin = item.skin,
                        condition = item.condition,
                        maxCondition = item.maxCondition,
                        frequency = ItemModAssociatedEntity<PagerEntity>.GetAssociatedEntity(item)?.GetFrequency() ?? -1,
                        instanceData = new ItemData.InstanceData(item),
                        contents = item.contents?.itemList.Select(item1 => new ItemData
                        {
                            itemid = item1.info.itemid,
                            amount = item1.amount,
                            condition = item1.condition
                        }).ToArray()
                    });
                }

                private float[] GetStats(BasePlayer player) => new float[] { player.health, player.metabolism.hydration.value, player.metabolism.calories.value };

                public void SetStats(BasePlayer player)
                {
                    player.health = stats[0];
                    player.metabolism.hydration.value = stats[1];
                    player.metabolism.calories.value = stats[2];
                    player.metabolism.SendChangesToClient();
                }               

                private float[] GetPosition(Vector3 position) => new float[] { position.x, position.y, position.z };

                public Vector3 GetPosition() => new Vector3(position[0], position[1], position[2]);
            }

            public class ItemData
            {
                public int itemid;
                public ulong skin;
                public int amount;
                public float condition;
                public float maxCondition;
                public int ammo;
                public string ammotype;
                public int position;
                public int frequency;
                public InstanceData instanceData;
                public ItemData[] contents;

                public class InstanceData
                {
                    public int dataInt;
                    public int blueprintTarget;
                    public int blueprintAmount;

                    public InstanceData() { }
                    public InstanceData(Item item)
                    {
                        if (item.instanceData == null)
                            return;

                        dataInt = item.instanceData.dataInt;
                        blueprintAmount = item.instanceData.blueprintAmount;
                        blueprintTarget = item.instanceData.blueprintTarget;
                    }

                    public void Restore(Item item)
                    {
                        if (item.instanceData == null)
                            item.instanceData = new ProtoBuf.Item.InstanceData();

                        item.instanceData.ShouldPool = false;

                        item.instanceData.blueprintAmount = blueprintAmount;
                        item.instanceData.blueprintTarget = blueprintTarget;
                        item.instanceData.dataInt = dataInt;

                        item.MarkDirty();
                    }

                    public bool IsValid()
                    {
                        return dataInt != 0 || blueprintAmount != 0 || blueprintTarget != 0;
                    }
                }
            }
        }        
        #endregion

        #region Localization
        public static string msg(string key, ulong playerId = 0U) => ins.lang.GetMessage(key, ins, playerId != 0U ? playerId.ToString() : null);

        Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["ks_1"] = "<color=#ce422b>Killing Spree</color>",
            ["ks_1_p"] = "<color=#ce422b>{0}</color> is on a killing spree!",
            ["ks_2"] = "<color=#ce422b>Killing Frenzy</color>",
            ["ks_2_p"] = "<color=#ce422b>{0}</color> is on a killing frenzy!",
            ["ks_3"] = "<color=#ce422b>Running Riot</color>",
            ["ks_3_p"] = "<color=#ce422b>{0}</color> is running riot!",
            ["ks_4"] = "<color=#ce422b>Rampage</color>",
            ["ks_4_p"] = "<color=#ce422b>{0}</color> is on a rampage!",
            ["ks_5"] = "<color=#ce422b>Untouchable</color>",
            ["ks_5_p"] = "<color=#ce422b>{0}</color> is untouchable!",
            ["ks_6"] = "<color=#ce422b>Invincible</color>",
            ["ks_6_p"] = "<color=#ce422b>{0}</color> is invincible",
            ["roundstarts"] = "<color=#ce422b>Round starts in:</color>",
            ["notenoughtostart"] = "There is not enough players to start the round. Waiting for more players",
            ["notenoughtocontinue"] = "There is not enough players to continue this round. Ending game!",
            ["aredead"] = "You are dead",
            ["killedby"] = "You were killed by <color=#ce422b>{0}</color>",
            ["Kill Limit"] = "Kill Limit",
            ["playersreached"] = "Minimum players has been reached. The event will start in <color=#ce422b>{0} seconds</color>!",
            ["leftevent"] = "<color=#ce422b>{0}</color> has left the event",
            ["joinevent"] = "<color=#ce422b>{0}</color> has joined the event",
            ["joineventglobal"] = "<color=#ce422b>{0}</color> has joined event <color=#ce422b>{1}</color>",
            ["killedplayer"] = "<color=#ce422b>{0}</color> killed <color=#ce422b>{1}</color>",
            ["runaway"] = "<color=#ce422b>{0}</color> tried to run away....",
            ["suicide"] = "<color=#ce422b>{0}</color> killed themselves....",
            ["teamsunbalanced"] = "<color=#ce422b>Teams are unbalanced!</color> Auto assigning players to balance teams",
            ["teamswitched"] = "You have been moved to team {0}!",
            ["survleftevent"] = "<color=#ce422b>{0}</color> has left the event (<color=#ce422b>{1}</color> players remain!)",
            ["survkilledplayer"] = "<color=#ce422b>{0}</color> killed <color=#ce422b>{1}</color>. (<color=#ce422b>{2}</color> players remain!)",
            ["survrunaway"] = "<color=#ce422b>{0}</color> tried to run away.... (<color=#ce422b>{1}</color> players remain!)",
            ["survsuicide"] = "<color=#ce422b>{0}</color> killed themselves....  (<color=#ce422b>{1}</color> players remain!)",
            ["youarewinner"] = "You have <color=#ce422b>won</color> the match!",
            ["Survivors"] = "Survivors",
            ["oob1"] = "You have <color=#ce422b>10</color> seconds to return to the arena",
            ["oob2"] = "<color=#ce422b>{0}</color> seconds",
            ["teamwin"] = "<color=#ce422b>{0}</color> has won the match!",
            ["Rank Limit"] = "Rank Limit",
            ["Capture Limit"] = "Capture Limit",
            ["killedplayergg"] = "<color=#ce422b>({1}) {0}</color> killed <color=#ce422b>({3}) {2}</color>",
            ["downgradeEnabled"] = "Downgrade other players by killing them with a <color=#ce422b>{0}</color>!",
            ["noevents"] = "No events have been set up yet",
            ["blacklistcmd"] = "You can not run that command whilst playing in an event",
            ["globalwin"] = "<color=#ce422b>{0}</color> has won the <color=#ce422b>{1}</color> event!",
            ["globalteamwin"] = "<color=#ce422b>Team {0}</color> (<color=#ce422b>{1}</color>) has won the <color=#ce422b>{2}</color> event!",
            ["spectatetext"] = "You have run out of lives!\nYou are currently in spectate mode however there are no valid spectate targets.\n\nYou can leave the event now, or wait until the round is over",
            ["youareout"] = "You have run out of lives, entering <color=#ce422b>specator mode</color>",
            ["flagcaptured"] = "<color=#ce422b>{0}</color> has captured <color=#ce422b>Team {1}</color>'s flag!",
            ["flagpickup"] = "<color=#ce422b>{0}</color> has picked up <color=#ce422b>Team {1}</color>'s flag!",
            ["flagreturned"] = "<color=#ce422b>{0}</color> has returned <color=#ce422b>Team {1}</color>'s flag to base!",
            ["kills"] = "Kills",
            ["deaths"] = "Deaths",
            ["wins"] = "Wins",
            ["captures"] = "Captures",
            ["deadplayer"] = "(Dead)",
            ["notnaked"] = "You must be naked to join an event",
            ["nextround"] = "Round <color=#ce422b>{0}</color> starts in <color=#ce422b>10 seconds!</color>",
            ["npcsremain"] = "NPCs Remaining: <color=#ce422b>{0}</color>",
            ["roundsremain"] = "Round <color=#ce422b>{0} / {1}</color>",
            ["Team A"] = "Team A",
            ["Team B"] = "Team B",
            ["nolooting"] = "<color=#ce422b>Looting is disabled until the event starts!</color>",
            ["nolooting_event"] = "<color=#ce422b>Looting is disabled in this event!</color>",
            ["atcapacity"] = "<color=#ce422b>This event is already at capacity!</color>",
            ["eventStarted"] = "<color=#ce422b>You can not join this event once it has started</color>",
            ["flag"] = "Flag",
            ["friendlyFire"] = "<color=#ce422b>Friendly Fire!</color>",
            ["waitingForPlayers"] = "<color=#ce422b>Waiting for the event to start before sending you to the arena</color>",
            ["hunterWinners"] = "The <color=#ce422b>hunted</color> have won this round!",
            ["slasherWinners"] = "The <color=#ce422b>slasher</color> has won this round!",
            ["hideSlasher"] = "Hide from the <color=#ce422b>Slasher</color>!",
            ["killSlasher"] = "Kill The <color=#ce422b>Slasher</color>!",
            ["slasherRoundChange"] = "The <color=#ce422b>hunter</color> has become the <color=#ce422b>hunted</color>...",
            ["matchDraw"] = "Draw!",
            ["slasherHelp"] = "<color=#ce422b>You are the slasher!</color> Kill the other players before they timer expires and they get a weapon",
            ["huntedHelp"] = "<color=#ce422b>You are prey!</color> Hide from the Slasher! If you survive long enough you will get a weapon",
            ["notinlobby"] = "You must be in the event lobby area to access the event menu",
            ["lobbytp"] = "Type <color=#ce422b>/lobby</color> to teleport to the lobby",
            ["alreadyinlobby"] = "You are already in the event lobby area",
            ["notp.pending"] = "You already have a pending lobby TP request",
            ["notp.cooldown"] = "You must wait another {0} seconds to teleport to the lobby",
            ["notp.buildblocked"] = "You can not TP whilst building blocked",
            ["notp.cargoship"] = "You can not TP whilst on the cargo ship",
            ["notp.hotairballoon"] = "You can not TP whilst in a hot air balloon",
            ["notp.mounted"] = "You can not TP whilst mounted",
            ["notp.oilrig"] = "You can not TP whilst on the oil rig",
            ["notp.bleeding"] = "You can not TP whilst bleeding",
            ["notp.raidblocked"] = "You can not TP whilst raid blocked",
            ["notp.combatblocked"] = "You can not TP whilst combat blocked",
            ["notp.inevent"] = "You can not use this command while you are in a event",
            ["tp.notpending"] = "You do not have a pending teleport",
            ["tp.cancelled"] = "You have cancelled your pending TP to the lobby",
            ["tp.pending"] = "Teleporting to the lobby in {0} seconds\nYou can cancel this request by typing <color=#ce422b>/lobbyc</color>",
            ["infectedKill"] = "{0} infected a survivor!",
            ["chooseInfected"] = "The infected will be chosen in :",
            ["chosenInfected"] = "You are <color=#ce422b>Infected</color>! Hunt the survivors and spread the infection",
            ["infectedStart"] = "Hide from the <color=#ce422b>Infected</color>!",
            ["survivorsWin"] = "The survivors have won the event",
            ["infectedWin"] = "The infected have won the event",
            ["Infected"] = "Infected",
            ["score"] = "Score",
            ["waitingBroadcast"] = "The event <color=#ce422b>{0}</color> (<color=#ce422b>{1}</color>) is waiting for <color=#ce422b>{2}</color> more player(s) to join.",
            ["blacklisted"] = "You have been <color=#ce422b>blacklisted</color> from playing in events",
            ["eventEnabled"] = "The event <color=#ce422b>{0}</color> is open to play!",
            ["teleportNoPerm"] = "This event is for <color=#ce422b>donators</color> only!"
        };
        #endregion
    }
}
