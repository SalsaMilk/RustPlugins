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
    [Info("test", "Salsa", "0")]
    class Test : RustPlugin
    {
        BasePlayer p = null;

        private void Init()
        {
        }

        private void Unload()
        {
            Physics.gravity = new Vector3(0, -9.8f, 0);
        }

        private void OnTick()
        {
            //if (p != null) p.SetVelocity(new Vector3(0, 100, 0));
            //if (p != null) p.ApplyInheritedVelocity(new Vector3(0, 0, 0));
        }
        
        [ChatCommand("test")]
        private void cmdTest(BasePlayer player, string command, string[] args)
        {
            p = player;
            if (p != null) ;
            //var x = float.Parse(args[0]);
        }
        [ChatCommand("test2")]
        private void cmdTest2(BasePlayer player, string command, string[] args)
        {
            //player.ApplyInheritedVelocity(new Vector3(0, 0, 0));
            p = null;

        }
    }
}
