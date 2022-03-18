/*
  ██████  ▄▄▄       ██▓      ██████  ▄▄▄
▒██    ▒ ▒████▄    ▓██▒    ▒██    ▒ ▒████▄
░ ▓██▄   ▒██  ▀█▄  ▒██░    ░ ▓██▄   ▒██  ▀█▄
  ▒   ██▒░██▄▄▄▄██ ▒██░      ▒   ██▒░██▄▄▄▄██
▒██████▒▒ ▓█   ▓██▒░██████▒▒██████▒▒ ▓█   ▓██▒
▒ ▒▓▒ ▒ ░ ▒▒   ▓▒█░░ ▒░▓  ░▒ ▒▓▒ ▒ ░ ▒▒   ▓▒█░
░ ░▒  ░ ░  ▒   ▒▒ ░░ ░ ▒  ░░ ░▒  ░ ░  ▒   ▒▒ ░
░  ░  ░    ░   ▒     ░ ░   ░  ░  ░    ░   ▒   
 Contact Salsa#7717 on Discord for programming/business inquiries
*/

using System.Collections.Generic;
using System.Collections;
using Oxide.Game.Rust.Libraries;
using Newtonsoft.Json;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Plugins;
using System;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Rustic Claims", "Salsa", "0.0.0")]
    [Description("Claiming system for the Rustic Rejects Build server")]
    class RusticClaims : RustPlugin
    {
        [PluginReference] Plugin ZoneDomes;
        [PluginReference] Plugin ZoneManager;

        private const string SphereEnt = "assets/prefabs/visualization/sphere.prefab";

        //              Steam ID, Claim Position
        private Dictionary<ulong, Claim> Claims = new Dictionary<ulong, Claim>();

        private class Claim
        {
            public string ID; // functions as zoneID

            public Vector3 pos;

            public Claim(string ID, Vector3 pos)
            {
                this.ID = ID;

                this.pos = pos;
            }
        }

        private bool ClaimTaken(Vector3 pos)
        {
            foreach (var claim in Claims)
                if (claim.Value.pos == pos) return true;
            return false;
        }

        [ChatCommand("claim")]
        private void cmdClaim(BasePlayer player, string command, string[] args)
        {
            if (Claims.ContainsKey(player.userID))
                return;

            // Chose random location on map and ensure it doesn't fuck up
            // claim grid is 19x19 with each square being 292
            Vector3 pos;
            do
                pos = new Vector3(Core.Random.Range(-9, 9) * 292, 30, Core.Random.Range(-9, 9) * 292);
            while (ClaimTaken(pos));

            Claims.Add(player.userID, new Claim(player.UserIDString, pos));

            if (!(bool)ZoneManager?.Call("CreateOrUpdateZone", player.UserIDString, new string[] { }, pos))
            {
                Puts($"Error creating zone for {player.UserIDString}");
                return;
            }

            if (!(bool)ZoneDomes?.Call("AddNewDome", player, player.UserIDString))
            {
                Puts($"Error creating dome for {player.UserIDString}");
                return;
            }

            player.Teleport(pos);
        }

        [ChatCommand("unclaim")]
        private void cmdUnclaim(BasePlayer player, string command, string[] args)
        {

        }
    }
}