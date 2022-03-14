/*
 * Originally created by Blunt.
 * Config support, optimization, and other modifications by Salsa
 */

using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;
using System;

namespace Oxide.Plugins
{
    [Info("Mini Jams", "Blunt", "1.5.0")]
    [Description("Spawns an boombox and lights your minicopter.")]
    class MiniJams : RustPlugin
    {
        #region Fields

        private Dictionary<ulong, Preference> preferences; // player preferences
        const string _boombox = "assets/prefabs/voiceaudio/boombox/boombox.static.prefab";
        const string _lights = "assets/prefabs/misc/xmas/christmas_lights/xmas.lightstring.deployed.prefab";

        string Prefix = "Rustic Rejects";
        const string PermUse = "minijams.use";

        class Preference
        {
            public bool boombox = true;
            public bool lights = true;
        }

        #endregion

        #region Hooks

        void OnEntitySpawned(MiniCopter mini)
        {
            if (mini.ShortPrefabName == "minicopter.entity" && permission.UserHasPermission(mini.OwnerID.ToString(), PermUse))
            {
                if (!preferences.ContainsKey(mini.OwnerID))
                    preferences.Add(mini.OwnerID, new Preference());

                // Boombox
                if (preferences[mini.OwnerID].boombox)
                    MakeEnt(_boombox, mini, new Vector3(0.0f, 0.35f, 1.88f), Quaternion.Euler(315.0f, 180.0f, 0.0f));

                // Lights
                if (preferences[mini.OwnerID].lights)
                {
                    MakeEnt(_lights, mini, new Vector3(0.45f, 0.3f, 0.35f), Quaternion.Euler(0.0f, 65.0f, 0.0f));
                    MakeEnt(_lights, mini, new Vector3(-0.45f, 0.3f, 0.35f), Quaternion.Euler(0.0f, 295.0f, 0.0f));
                    MakeEnt(_lights, mini, new Vector3(0.0f, 0.3f, -0.85f), Quaternion.Euler(0.0f, 0.0f, 0.0f));
                }
            }
        }

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            preferences = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, Preference>>(Name);
            if (preferences == null)
            {
                preferences = new Dictionary<ulong, Preference>();
                Interface.Oxide.DataFileSystem.WriteObject(Name, preferences);
            }
        }

        #endregion

        void MakeEnt(string ent, BaseVehicle vehicle, Vector3 position, Quaternion rotation)
        {
            BaseEntity entity = GameManager.server.CreateEntity(ent, vehicle.transform.position);
            if (entity == null) { Puts("Error creating entity"); return; }
            entity.SetParent(vehicle);
            entity.transform.localPosition = position;
            entity.transform.localRotation = rotation;
            entity.Spawn();
        }

        [ChatCommand("boombox")]
        private void cmdJams(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse))
            {
                SendChatMessage(player, Prefix, "You do not have permission to use this command");
                return;
            }
            if (!preferences.ContainsKey(player.userID))
                preferences.Add(player.userID, new Preference());
            preferences[player.userID].boombox = !preferences[player.userID].boombox;
            SendChatMessage(player, Prefix, "Boombox toggled " + (preferences[player.userID].boombox ? "on" : "off"));
            Interface.Oxide.DataFileSystem.WriteObject(Name, preferences);
            return;
        }

        [ChatCommand("lights")]
        private void cmdLights(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse))
            {
                SendChatMessage(player, Prefix, "You do not have permission to use this command");
                return;
            }
            if (!preferences.ContainsKey(player.userID))
                preferences.Add(player.userID, new Preference());
            preferences[player.userID].lights = !preferences[player.userID].lights;
            SendChatMessage(player, Prefix, "Lights toggled " + (preferences[player.userID].lights ? "on" : "off"));
            Interface.Oxide.DataFileSystem.WriteObject(Name, preferences);
            return;
        }

        void SendChatMessage(BasePlayer player, string prefix, string msg = null) => SendReply(player, msg == null ? prefix : "<color=#00FF8D>" + prefix + "</color>: " + msg);
    }
}