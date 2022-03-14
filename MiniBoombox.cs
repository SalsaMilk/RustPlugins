using System.Collections.Generic;
using System.Collections;
using Oxide.Game.Rust.Libraries;
using System.Linq;
using Oxide.Core;
using System;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MiniBoombox", "Salsa", "1.0.0")]
    [Description("Adds boombox to mini")]
    class MiniBoombox : RustPlugin
    {
        private const string prefab = "assets/prefabs/voiceaudio/boombox/boombox.static.prefab";

        void OnEntitySpawned(MiniCopter mini)
        {
            var boombox = GameManager.server.CreateEntity(prefab, mini.transform.position + new Vector3(0, 10, 0), new Quaternion(0, 0, 0, 0)) as DeployableBoomBox;
            boombox.SetParent(mini);
            boombox.Spawn();
        }

        [ChatCommand("test1")]
        private void cmdTest(BasePlayer player, string command, string[] args)
        {
            var boombox = GameManager.server.CreateEntity(prefab, player.transform.position, new Quaternion(0, 0, 0, 0)) as DeployableBoomBox;
            boombox.SetParent(player);
            boombox.Spawn();
        }
    }
}
