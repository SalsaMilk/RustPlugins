using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("SuperLogs", "Salsa", "1.0.0")]
    [Description("The best log system with a UI for admins")]
    class SuperLogs : RustPlugin
    {
        #region Fields

        private const string PERM_USE = "superlogs.use";

        private const string uiMain = "LOGS_MenuMain";
        private const string uiElement = "LOGS_MenuElement";

        #endregion

        #region Config

        private Configuration configData;
        private class Configuration
        {
            [JsonProperty(PropertyName = "Background", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public UIColor bg { get; set; }

            [JsonProperty(PropertyName = "Exit button", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public UIColor button_exit { get; set; }


            public class UIColor
            {
                public string Color { get; set; }
                public float Alpha { get; set; }
            }
        }

        #endregion

        #region OXide Hooks

        [ChatCommand("logs")]
        private void cmdLogs(BasePlayer player, string command, string[] args)
        {
            if (!HasPermission(player.userID, PERM_USE))
            {
                SendChatMessage(player, "You don't have permission to use this command");
                return;
            }
            OpenLogsMenu(player);
        }

        [ConsoleCommand("superlogs.request")]
        private void cmdRequest(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            switch (arg.Args[0])
            {
                case "exit":
                    ClearUI(player);
                    break;
                default: 
                    break;
            }
        }
        #endregion


        private void OpenLogsMenu(BasePlayer player)
        {
            ClearUI(player);

        }

        private void Init()
        {
            permission.RegisterPermission(PERM_USE, this);
            configData = DefaultConfig();
        }

        private Configuration DefaultConfig() =>
                new Configuration
                {
                    bg = new Configuration.UIColor { Color = "#0d0000", Alpha = 0.8f },
                    button_exit = new Configuration.UIColor { Color = "#ff0000", Alpha = 1f }
                };

        #region Helpers

        private bool HasPermission(ulong ID, string perm) => permission.UserHasPermission(ID.ToString(), perm);

        private void ClearUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, uiMain);
        }

        void SendChatMessage(BasePlayer player, string msg) => SendReply(player, "<color=#00FF8D>Logs</color>: " + msg);

        #endregion
    }
}