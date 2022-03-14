using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FauxAdmin", "Colon Blow", "1.0.20")]
    [Description("Players can use admin noclip")]

    class FauxAdmin : RustPlugin
    {
        // changed OnPlayerInit to OnPlayerConnected
        // Static cleanup
        // Code cleanup
        // Unsubscribing of hooks not being used
        // Allowed npcs to be killed using entkill
        private static readonly Dictionary<ulong, FauxControl> controls = new Dictionary<ulong, FauxControl>();

        #region Loadup

        void Init()
        {
            Unsubscribe(nameof(OnStructureDemolish));
            Unsubscribe(nameof(OnStructureRotate));
            Unsubscribe(nameof(OnStructureUpgrade));
            lang.RegisterMessages(Messages, this);
            permission.RegisterPermission("fauxadmin.allowed", this);
            permission.RegisterPermission("fauxadmin.bypass", this);
            permission.RegisterPermission("fauxadmin.blocked", this);
            LoadVariables();
        }

        void OnServerInitialized()
        {
            if (DisableFauxAdminDemolish) Subscribe(nameof(OnStructureDemolish));
            if (DisableFauxAdminRotate) Subscribe(nameof(OnStructureRotate));
            if (DisableFauxAdminUpgrade) Subscribe(nameof(OnStructureUpgrade));
        }

        bool isAllowed(BasePlayer player, string perm) => player != null && permission.UserHasPermission(player.UserIDString, perm);

        #endregion

        #region Configuration

        bool DisableFauxAdminDemolish = true;
        bool DisableFauxAdminRotate = true;
        bool DisableFauxAdminUpgrade = true;
        bool DisableNoclipOnNoBuild = true;
        bool EntKillOwnOnly = true;
        bool UseAdminFlag = false;

        private void LoadVariables()
        {
            LoadConfigVariables();
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating a new config file");
            Config.Clear();
            LoadVariables();
        }

        private void LoadConfigVariables()
        {
            CheckCfg("Disable FauxAdmin Ability to Demolish others building parts? ", ref DisableFauxAdminDemolish);
            CheckCfg("Disable FauxAdmin Ability to Rotate others building parts ? ", ref DisableFauxAdminRotate);
            CheckCfg("Disable FauxAdmin Ability to upgrade others building parts ? ", ref DisableFauxAdminUpgrade);
            CheckCfg("Disable FauxAdmins Noclip when not authorized on local tool cupboard ? ", ref DisableNoclipOnNoBuild);
            CheckCfg("Only allow FauxAdmins to use entkill in there own stuff ? ", ref EntKillOwnOnly);
            CheckCfg("Give Players Admin flag and not Developer flag ? ", ref UseAdminFlag);
        }

        private void CheckCfg<T>(string Key, ref T var)
        {
            if (Config[Key] is T)
                var = (T)Config[Key];
            else
                Config[Key] = var;
        }

        private void CheckCfgFloat(string Key, ref float var)
        {
            if (Config[Key] != null)
                var = Convert.ToSingle(Config[Key]);
            else
                Config[Key] = var;
        }

        #endregion

        #region Localization

        string msg(string key, string playerId = null) => lang.GetMessage(key, this, playerId);

        Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["restricted"] = "You are not allowed to noclip here.",
            ["notallowed"] = "You are not authorized to use that command !!"
        };

        #endregion

        #region EntKill

        private BaseEntity baseEntity;
        private RaycastHit RayHit;
        private static int layermask = LayerMask.GetMask("Construction", "Deployed", "Default", "AI", "Player (Server)");

        [ConsoleCommand("entkill")]
        void cmdConsoleEntKill(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!isAllowed(player, "fauxadmin.allowed"))
            {
                SendReply(player, msg("notallowed", player.UserIDString));
                return;
            }
            EntKillProcess(player);
        }

        private void EntKillProcess(BasePlayer player)
        {
            if (Physics.Raycast(player.eyes.HeadRay(), out RayHit, 10f, layermask))
            {
                baseEntity = RayHit.GetEntity();
                if (baseEntity == null) return;
                if (baseEntity is BasePlayer && !baseEntity.IsNpc) return;
                if (EntKillOwnOnly && !baseEntity.IsNpc && player.userID != baseEntity.OwnerID) return;
                baseEntity.Kill(BaseNetworkable.DestroyMode.Gib);
            }
        }

        #endregion

        #region EntWho

        [ConsoleCommand("entwho")]
        void cmdConsoleEntWho(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!isAllowed(player, "fauxadmin.allowed"))
            {
                SendReply(player, msg("notallowed", player.UserIDString));
                return;
            }
            EntWhoProcess(player);
        }

        private void EntWhoProcess(BasePlayer player)
        {
            if (Physics.Raycast(player.eyes.HeadRay(), out RayHit, 10f, layermask))
            {
                baseEntity = RayHit.GetEntity();
                if (baseEntity == null || baseEntity.IsNpc || baseEntity is BasePlayer) return;
                SendReply(player, "Owner ID: " + baseEntity.OwnerID.ToString());
            }
        }

        #endregion

        #region Noclip

        [ChatCommand("noclip")]
        void cmdChatnoclip(BasePlayer player, string command, string[] args)
        {
            if (player.net?.connection?.authLevel > 0 || isAllowed(player, "fauxadmin.allowed"))
            {
                rust.RunClientCommand(player, "noclip");
            }
            else SendReply(player, msg("notallowed", player.UserIDString));
        }

        [ChatCommand("fly")]
        void cmdChatFly(BasePlayer player, string command, string[] args)
        {
            cmdChatnoclip(player, command, args);
        }

        #endregion

        #region Player Control

        class FauxControl : FacepunchBehaviour
        {
            BasePlayer player;
            FauxAdmin instance;
            ulong playerId;
            bool DisableNoclipOnNoBuild;

            static Dictionary<ulong, RestrictedData> _restricted = new Dictionary<ulong, RestrictedData>();

            class RestrictedData
            {
                public BasePlayer player;
            }

            public void Setup(FauxAdmin instance, BasePlayer player, bool DisableNoclipOnNoBuild)
            {
                this.DisableNoclipOnNoBuild = DisableNoclipOnNoBuild;
                this.instance = instance;
                this.player = player;
                playerId = player.userID;
                player.PauseFlyHackDetection(99999f);
                player.PauseSpeedHackDetection(99999f);
                player.PauseVehicleNoClipDetection(99999f);
                InvokeRepeating(Repeater, 1f, 1f);
            }

            private void Repeater()
            {
                if (!player || !player.IsConnected)
                {
                    CancelInvoke();
                    OnDestroy();
                }
            }

            private void DeactivateNoClip(Vector3 pos)
            {
                if (player == null || _restricted.ContainsKey(player.userID)) return;
                instance.timer.Repeat(0.1f, 10, () => instance.ForcePlayerPosition(player, pos));

                _restricted.Add(player.userID, new RestrictedData
                {
                    player = player
                });
                instance.SendReply(player, instance.msg("restricted", player.UserIDString));
                instance.rust.RunClientCommand(player, "noclip");
                instance.timer.Once(1, () => _restricted.Remove(player.userID));
            }

            private void FixedUpdate()
            {
                if (player == null || !DisableNoclipOnNoBuild) return;
                if (player.IsFlying && player.IsBuildingBlocked())
                {
                    DeactivateNoClip(player.transform.position);
                }
            }

            private void OnDestroy()
            {
                if (player != null)
                {
                    player.PauseFlyHackDetection(5f);
                    player.PauseSpeedHackDetection(5f);
                    player.PauseVehicleNoClipDetection(5f);
                }

                controls.Remove(playerId);
                Destroy(this);
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (isAllowed(player, "fauxadmin.blocked"))
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsDeveloper, false);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                return;
            }
            if (player.net?.connection?.authLevel > 0) return;
            if (!isAllowed(player, "fauxadmin.allowed"))
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsDeveloper, false);
            }
            else
            {
                if (UseAdminFlag) player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                else player.SetPlayerFlag(BasePlayer.PlayerFlags.IsDeveloper, true);
                if (isAllowed(player, "fauxadmin.bypass")) return;
                var component = player.gameObject.AddComponent<FauxControl>();
                controls.Add(player.userID, component);
                component.Setup(this, player, DisableNoclipOnNoBuild);
            }
        }

        #endregion

        #region Structure Hooks

        private object OnStructureDemolish(BuildingBlock block, BasePlayer player)
        {
            if (isAllowed(player, "fauxadmin.allowed") && block.OwnerID.IsSteamId() && block.OwnerID != player.userID)
            {
                return true;
            }
            return null;
        }

        private object OnStructureRotate(BuildingBlock block, BasePlayer player)
        {
            if (isAllowed(player, "fauxadmin.allowed") && block.OwnerID.IsSteamId() && block.OwnerID != player.userID)
            {
                return true;
            }
            return null;
        }

        private object OnStructureUpgrade(BuildingBlock block, BasePlayer player)
        {
            if (isAllowed(player, "fauxadmin.allowed") && block.OwnerID.IsSteamId() && block.OwnerID != player.userID)
            {
                return true;
            }
            return null;
        }

        #endregion

        #region Hooks

        private void Unload()
        {
            DestroyAll<FauxControl>();
            controls.Clear();
        }

        private static void DestroyAll<T>()
        {
            var objects = GameObject.FindObjectsOfType(typeof(T));
            if (objects != null)
                foreach (var gameObj in objects)
                    UnityEngine.Object.Destroy(gameObj);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            FauxControl component;
            if (controls.TryGetValue(player.userID, out component))
            {
                UnityEngine.Object.Destroy(component);
                controls.Remove(player.userID);
            }
        }

        #endregion
    }
}