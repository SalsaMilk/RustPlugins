using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ConVar;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RusticBot", "Salsa", "1.0.0")]
    [Description("Chat bot for Rustic Rejects minicopter training")]
    class RusticBot : RustPlugin
    {
        #region Fields
        Configuration config;

        private const string IPAPI = "http://ip-api.com/json/{ip}?fields=country,countryCode,status";
        #endregion

        #region Config
        private class Configuration
        {
            [JsonProperty(PropertyName = "Chat prefix")]
            public string Prefix = "<color=#787FFF>Rustic Regects</color>: ";

            [JsonProperty(PropertyName = "Show chat prefix")]
            public bool ShowPrefix = true;

            [JsonProperty(PropertyName = "Bot icon (SteamID)")]
            public ulong ChatSteamId = 0;

            [JsonProperty(PropertyName = "Time between message and answer")]
            public float ResponseTime = 1.0f;

            [JsonProperty(PropertyName = "Welcome message enabled")]
            public bool WelcomeMessageEnabled = true;

            [JsonProperty(PropertyName = "Leaving message enabled")]
            public bool LeavingMessageEnabled = true;

            [JsonProperty(PropertyName = "Welcome message")]
            public string WelcomeMessage = "<size=20><color=#EEAAAA>{name}</color><size=14> has joined!</size></size>";

            [JsonProperty(PropertyName = "Leaving message")]
            public string LeavingMessage = "<size=14>Goodbye <size=20><color=#EEAAAA>{name}</color></size></size>";

            [JsonProperty(PropertyName = "Time between auto messages")]
            public float AutoMessageTime = 300.0f;

            [JsonProperty(PropertyName = "Auto messages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AutoMessages = new List<string>() { "I'm alive", "How's everyone doing" };

            [JsonProperty(PropertyName = "Append auto responses")]
            public bool AppendAutoResponses = true;

            [JsonProperty(PropertyName = "Auto responses", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<AutoResponse> AutoResponses = new List<AutoResponse>() { new AutoResponse() };

            [JsonProperty(PropertyName = "Naughty word punishments", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<NaughtyWord> NaughtyList = new List<NaughtyWord>() { new NaughtyWord() };
        }

        class AutoResponse
        {
            [JsonProperty(PropertyName = "Response")]
            public string Response = "Yes, the bot works";

            [JsonProperty(PropertyName = "Keywords", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Keywords = new List<string>() { "does", "this", "bot", "work" };

            [JsonProperty(PropertyName = "Number of keywords required")]
            public int KeywordsRequired = 2;
        }

        class NaughtyWord
        {
            [JsonProperty(PropertyName = "Word")]
            public string word = "frick";

            [JsonProperty(PropertyName = "Response")]
            public string response = "That's not nice";

            [JsonProperty(PropertyName = "Naughtiness level")]
            public int level = 1;
        }
        #endregion

        #region Hooks
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

            int privateMessageIndex = 0;
            timer.Every(config.AutoMessageTime, () =>
            {
                BroadcastMessage(config.AutoMessages[privateMessageIndex++ % config.AutoMessages.Count]);
            });
        }

        private void OnPlayerChat(BasePlayer player, string message, Chat.ChatChannel channel)
        {
            Punish(player, HandleBlacklist(message.ToLower())); // Check for naughty words
            HandleQuestion(message.ToLower());
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!config.WelcomeMessageEnabled) return;
            string message = config.WelcomeMessage;
            webrequest.Enqueue(IPAPI.Replace("{ip}", player.Connection.ipaddress),
            string.Empty, (status, result) =>
            {
                try
                {
                    if (status == 200)
                    {
                        var info = JsonConvert.DeserializeObject<Dictionary<string, object>>(result);
                        if ((string)info["status"] == "success")
                            message = message.Replace("{country}", (string)info["country"]);
                    }
                }
                catch (Exception e)
                {
                    Puts(e.ToString());
                }
                finally
                {
                    BroadcastMessage(message.Replace("{name}", player.displayName));
                }
            }, this);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (!config.LeavingMessageEnabled) return;
            BroadcastMessage(config.LeavingMessage.Replace("{name}", player.displayName));
        }
        #endregion

        #region Methods
        private void HandleQuestion(string message)
        {
            string response = "";
            foreach (AutoResponse AR in config.AutoResponses)
            {
                int keywordCount = 0;
                foreach (string word in AR.Keywords)
                {
                    if (message.Contains(word)) keywordCount++;
                }
                if (keywordCount >= AR.KeywordsRequired)
                    response = config.AppendAutoResponses ? (response + AR.Response + "\n") : AR.Response;
            }
            response = response.Trim('\n');
            if (response != "")
                timer.Once(config.ResponseTime, () => { BroadcastMessage(response); });
        }

        private int HandleBlacklist(string message) 
        {   // returns true if player says naughty word
            string response = "";
            int punishment = 0;
            foreach (NaughtyWord NW in config.NaughtyList)
            {
                if (message.Replace(" ", "") // Check for 1337 5P34K
                           .Replace('1', 'i').Replace('9', 'g') 
                           .Replace('4', 'a').Replace('3', 'e')
                           .Replace('0', 'o').Replace('7', 't')
                           .Replace('8', 'b').Replace('5', 's')
                           .Contains(NW.word.Replace(" ", "")))
                    if (NW.level > punishment)
                    {
                        punishment = NW.level;
                        response = NW.response;
                    }
            }
            timer.Once(config.ResponseTime, () => { if (response != "") BroadcastMessage(response); });
            return punishment;
        }

        private void Punish(BasePlayer player, int level)
        {
            switch (level) 
            {
                case 1: // Harrassment / vulgarity
                    // mute / warning
                    break;

                case 2: // Offensive slurs reguarding sexuality/mental state
                    // mute / kick / temp ban
                    break;

                case 3: // Reccurring racism
                    // perm ban with chance of appeal
                    break;

                case 4: // DDOS threats / threatening the integrety of the server
                    // perm ban with NO chance of appeal
                    break;

                default: return;
            }
        }

        private void BroadcastMessage(string message)
        {
            foreach (BasePlayer p in BasePlayer.activePlayerList)
                p.SendConsoleCommand("chat.add", 2, config.ChatSteamId,
                    config.ShowPrefix ? config.Prefix + message : message);
        }
        #endregion
    }
}
