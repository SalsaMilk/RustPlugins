
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;

/*
 * Edited by Salsa
 */


namespace Oxide.Plugins
{
    [Info("RustPlaneChatCommand", "Karuza", "01.01.00")]
    public class RustPlaneChatCommand : RustPlugin
    {
        [PluginReference]
        Plugin ZoneManager;

        private static Configuration configuration;

        private static int generalColl = LayerMask.GetMask("Construction", "Deployable", "Default", "Prevent Building", "Deployed", "Resource", "Terrain", "Water", "World", "Tree");
        private static float mdist = 9999f;
        private static Dictionary<uint, VehicleSpawner> SpawnedVehiclesByPlayer = new Dictionary<uint, VehicleSpawner>();
        private static Permission permissionHelper;

        public struct VehicleSpawner
        {
            public float LastChatCommandTime { get; set; }
            public BasePlayer Player { get; set; }
            public Dictionary<uint, BaseEntity> ActivePlanes { get; set; }
        }

        private void RegisterPermissions()
        { 
            if(configuration.Configurations == null)
                return;

            foreach(var config in configuration.Configurations)
            {
                if (!string.IsNullOrEmpty(config.RequiredPermission))
                {
                    permission.RegisterPermission(config.RequiredPermission, this);
                }
            }

            permissionHelper = permission;
        }

        private void RegisterCommands()
        {
            cmd.AddChatCommand(configuration.SpawnCommand, this, SpawnCommand);
            cmd.AddChatCommand(configuration.RecallCommand, this, RecallCommand);
        }

        void OnServerInitialized()
        {
            LoadConfig();
            RegisterCommands();
            RegisterPermissions();
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            if(configuration.ClearCooldownOnDisconnect)
                SpawnedVehiclesByPlayer.Remove(player.net.ID);
        }

        void OnEntityKill(BaseNetworkable entity)
        {
            if (entity == null || entity.net.ID <= 0)
                return;

            var spawner = SpawnedVehiclesByPlayer.FirstOrDefault(svbp => svbp.Value.ActivePlanes.ContainsKey(entity.net.ID));
            if (spawner.Key <= 0)
                return;

            spawner.Value.ActivePlanes.Remove(entity.net.ID);
        }

        private void RecallCommand(BasePlayer player, string command, string[] args)
        {
            if (!SpawnedVehiclesByPlayer.ContainsKey(player.net.ID))
            {
                player.ChatMessage("No planes to recall");
                return;
            }

            var playerSpawner = SpawnedVehiclesByPlayer[player.net.ID];
            if (playerSpawner.ActivePlanes.Count <= 0)
            {
                player.ChatMessage("No planes to recall");
                return;
            }

            player.ChatMessage("Beginning Recall");
            foreach (var plane in playerSpawner.ActivePlanes.ToList())
            {
                var currentBaseNet = plane.Value.GetComponent<BaseNetworkable>();
                if (!currentBaseNet.IsDestroyed)
                    currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);
                UnityEngine.Object.Destroy(plane.Value);
                playerSpawner.ActivePlanes.Remove(plane.Key);
            }
            player.ChatMessage("Destroyed Plane");
        }

        private void SpawnCommand(BasePlayer player, string command, string[] args)
        {
            if (configuration.AdminOnly && !player.IsAdmin)
            {
                player.ChatMessage("You must be an admin to use this command");
                return;
            }

            if (configuration.DisallowSafeZoneSpawn && player.InSafeZone())
            {
                player.ChatMessage($"You are unable to spawn an animal in the safe zone");
                return;
            }

            PermissionConfiguration permConfig = new PermissionConfiguration();
            bool found = false;
            if (configuration.Configurations != null && configuration.Configurations.Any())
            {
                foreach(var config in configuration.Configurations)
                {
                    if(string.IsNullOrEmpty(config.RequiredPermission) || permissionHelper.UserHasPermission(player.UserIDString, config.RequiredPermission))
                    {
                        permConfig = config;
                        found = true;
                        break;
                    }
                }
            }
            else
            {
                found = true;
            }

            if (!found)
            {
                player.ChatMessage("You do not have permission to use this command");
                return;
            }

            if (configuration.UseZoneManager)
            {
                bool isZoneBlocked = (bool)(ZoneManager?.Call("PlayerHasFlag", player, "NoVehicleSpawn") ?? false);
                if (isZoneBlocked)
                {
                    player.ChatMessage("You are not allowed to spawn a plane here.");
                    return;
                }
            }

            if (!args.Any())
                return;

            VehicleSpawner playerSpawner;
            if (!SpawnedVehiclesByPlayer.ContainsKey(player.net.ID))
            {
                playerSpawner = new VehicleSpawner()
                {
                    Player = player,
                    ActivePlanes = new Dictionary<uint, BaseEntity>()
                };
            }
            else
            {
                playerSpawner = SpawnedVehiclesByPlayer[player.net.ID];
            }

            var currentTime = UnityEngine.Time.realtimeSinceStartup;
            if (playerSpawner.LastChatCommandTime + permConfig.ChatCommandCooldown > currentTime)
            {
                player.ChatMessage($"This command is still on cooldown. Please try again in {(int)(playerSpawner.LastChatCommandTime + permConfig.ChatCommandCooldown - currentTime)} seconds");
                return;
            }

            playerSpawner.LastChatCommandTime = currentTime;
            if (permConfig.MaxPlanes > 0 && playerSpawner.ActivePlanes.Count >= permConfig.MaxPlanes)
            {
                player.ChatMessage($"You already have the max allowed planes: {permConfig.MaxPlanes} ");
                SpawnedVehiclesByPlayer[player.net.ID] = playerSpawner;
                return;
            }

            if(configuration.RequireBuildingPermission && !player.CanBuild())
            {
                player.ChatMessage($"You are not allowed to spawn a plane here");
                SpawnedVehiclesByPlayer[player.net.ID] = playerSpawner;
                return;
            }

            string planeType = args[0];
            Quaternion currentRot;
            if (!TryGetPlayerView(player, out currentRot))
                return;

            object closestEnt;
            Vector3 closestHitpoint;
            if (!TryGetClosestRayPoint(player.transform.position, currentRot, out closestEnt, out closestHitpoint))
                return;

            Vector3 newPos = closestHitpoint + (Vector3.up * 2f);
            Quaternion newRot = currentRot;
            newRot.x = 0f;
            newRot.z = 0f;
            newRot = newRot * new Quaternion(0f, 0.7071068f, 0f, 30f);

            var plane = Interface.CallHook("OnSpawnPlane", planeType, newPos, player.transform.up) as BaseEntity;
            if (plane == null)
            {
                player.ChatMessage("Unable to spawn a plane with that command.");
                return;
            }

            playerSpawner.ActivePlanes.Add(plane.net.ID, plane);
            SpawnedVehiclesByPlayer[player.net.ID] = playerSpawner;
            if(configuration.RevokePermissionOnSpawn)
            {
                permission.RevokeUserPermission(player.UserIDString, $"{permConfig.RequiredPermission}");
            }
        }

        private static bool TryGetPlayerView(BasePlayer player, out Quaternion viewAngle)
        {
            viewAngle = Quaternion.identity;

            if (player.serverInput.current == null)
                return false;

            viewAngle = Quaternion.Euler(player.serverInput.current.aimAngles);

            return true;
        }


        private static bool TryGetClosestRayPoint(Vector3 sourcePos, Quaternion sourceDir, out object closestEnt, out Vector3 closestHitpoint)
        {
            float closestdist = 999999f;

            Vector3 sourceEye = sourcePos + new Vector3(0f, 1.5f, 0f);
            Ray ray = new Ray(sourceEye, sourceDir * Vector3.forward);

            closestHitpoint = sourcePos;
            closestEnt = false;

            foreach (var hit in Physics.RaycastAll(ray, configuration.SpawnDistance, generalColl))
            {
                if (hit.collider.GetComponentInParent<TriggerBase>() == null)
                {
                    if (hit.distance < closestdist)
                    {
                        closestdist = hit.distance;
                        closestEnt = hit.GetCollider();
                        closestHitpoint = hit.point;
                    }
                }
            }

            if (closestEnt is bool)
                return false;
            return true;
        }

        #region Configuration
        public class Configuration
        {
            public string SpawnCommand { get; set; } = "Spawn";
            public string RecallCommand { get; set; } = "Recall";
            public bool AdminOnly { get; set; }
            public bool RequireBuildingPermission { get; set; }
            public bool RevokePermissionOnSpawn { get; set; }
            public bool UseZoneManager { get; set; }
            public bool ClearCooldownOnDisconnect { get; set; }
            public bool DisallowSafeZoneSpawn { get; set; }
            public float SpawnDistance { get; set; } = 9999f;
            public List<PermissionConfiguration> Configurations { get; set; }
        }

        public struct PermissionConfiguration
        {
            public float ChatCommandCooldown { get; set; }
            public int MaxPlanes { get; set; }
            public string RequiredPermission { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                configuration = Config.ReadObject<Configuration>();
                if (configuration == null)
                    throw new Exception();
            }
            catch
            {
                Config.WriteObject(configuration, false, $"{Interface.Oxide.ConfigDirectory}/{Name}.jsonError");
                PrintError("The configuration file contains an error and has been replaced with a default config.");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            configuration = new Configuration()
            {
                AdminOnly = false,
                RevokePermissionOnSpawn = false,
                RequireBuildingPermission = true,
                ClearCooldownOnDisconnect = true,
                UseZoneManager = false,
                Configurations = new List<PermissionConfiguration>()
                {
                    new PermissionConfiguration()
                    {
                        ChatCommandCooldown = 30,
                        MaxPlanes = 1,
                        RequiredPermission = string.Empty
                    }
                }
            };
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(configuration);
        }

        #endregion
    }
}
