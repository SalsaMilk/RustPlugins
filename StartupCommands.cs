using System.Collections.Generic;
using Oxide.Game.Rust.Libraries;
using Newtonsoft.Json;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;

namespace Oxide.Plugins
{
    [Info("Startup Commands", "Salsa", "1.0.0")]
    [Description("Execute commands on server startup")]
    class StartupCommands : RustPlugin
    {
        private Configuration config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> commands = new List<string>() { "" };

            [JsonProperty(PropertyName = "delayedCommands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> delayedCommands = new List<string>() { "" };

            public Configuration() { }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
            }
            catch
            {
                Puts("Your configuration file contains an error. Using default configuration values.");
                config = new Configuration();
            }
        }

        private void Init()
        {
            LoadConfig();
            Config.WriteObject(config);

            foreach (string command in config.commands)
                rust.RunServerCommand(command);

            timer.Once(300f, () =>
            {
                foreach (string command in config.delayedCommands)
                    rust.RunServerCommand(command);
            });
        }
    }
}