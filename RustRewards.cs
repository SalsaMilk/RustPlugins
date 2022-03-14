using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
	[Info("RustRewards", "Steenamaroo", "3.0.4")] 
	[Description("Rewards players for activities using Economic, ServerRewards or Scrap")]

	// To do

	// Fix teleport distance abuse.
	// Add a way to set default notification type for new players.
	// Add API for GetMultiplier and NotifyReward
	// Track user supply drops and give a different reward.PPlank 

	// DONE
	// Fix OnEntityDeath(BaseEntity entity... NRE. Probably weaponprefab. 
	// Fix OnPlayerDeath - Suicide.
	// Check if heli/apc are giving rewards.
	// Fixed happy hour always on issue.
	// Added notification preview when changing type/position in UI.
	// Fixed zone add/remove not saving on restart.
	// Fixed zone multiplier 0 being ignored.

	public class RustRewards : RustPlugin  
	{
		bool loaded = false;
		[PluginReference] Plugin Clans, Economics, Friends, GUIAnnouncements, NoEscape, ServerRewards, ZoneManager, RaidableBases, ImageLibrary;

		Dictionary<uint, Dictionary<ulong, int>> VehicleAttackers = new Dictionary<uint, Dictionary<ulong, int>>();

		private bool EventTerritory(BaseEntity entity)
		{
			return entity.OwnerID == 0 && Convert.ToBoolean(RaidableBases?.Call("EventTerritory", entity.transform.position)); 
		}

		public static RustRewards rr;
		private const string AdminUIPermission = "rustrewards.adminui";
		private const string HarvestPermission = "rustrewards.harvest";
		private const string KillPermission = "rustrewards.kill";
		private const string OpenPermission = "rustrewards.open";
		private const string PickupPermission = "rustrewards.pickup";
		private const string ActivityPermission = "rustrewards.activity";
		private const string WelcomePermission = "rustrewards.welcome";

		public enum RewardType { Kill, Harvest, Open, Pickup, Activity, Welcome }
		public enum Currency { Scrap, ServerRewards, Economics };
		public Currency currency = Currency.Scrap;

		public bool HappyHour()
		{
			if (conf.Settings.General.HappyHour_BeginHour > conf.Settings.General.HappyHour_EndHour)
				return TOD_Sky.Instance.Cycle.Hour > conf.Settings.General.HappyHour_BeginHour || TOD_Sky.Instance.Cycle.Hour < conf.Settings.General.HappyHour_EndHour;
			else
				return TOD_Sky.Instance.Cycle.Hour > conf.Settings.General.HappyHour_BeginHour && TOD_Sky.Instance.Cycle.Hour < conf.Settings.General.HappyHour_EndHour;
		}

		#region ConfigPrep
		public Dictionary<string, double> WeaponsList = new Dictionary<string, double>();

		public Kill Kills = new Kill();
		public class Kill
		{
			public Dictionary<string, double> NPCs = new Dictionary<string, double>();
			public Dictionary<string, double> Animals = new Dictionary<string, double>() { { "simpleshark", 0.0 } };
			public Dictionary<string, double> Vehicles = new Dictionary<string, double>() { { "patrolhelicopter", 0.0 }, { "bradleyapc", 0.0 }, { "ch47.entity", 0.0 }, { "ch47scientists.entity", 0.0 } };
			public Dictionary<string, double> MountedWeapons = new Dictionary<string, double>();
			public Dictionary<string, double> Players = new Dictionary<string, double>() { { "Players", 0.0 } };
		}

		public Harvest Harvests = new Harvest();
		public class Harvest
		{
			public Dictionary<string, double> Flesh = new Dictionary<string, double>() { };
			public Dictionary<string, double> Ore = new Dictionary<string, double>() { { "stone", 0.0 }, { "metal", 0.0 }, { "sulfur", 0.0 }, };
			public Dictionary<string, double> Tree = new Dictionary<string, double>() { { "Tree", 0.0 } };
		}
		public Dictionary<string, double> Open = new Dictionary<string, double>();
		public Dictionary<string, double> Pickup = new Dictionary<string, double>();
		#endregion

		const int ScrapId = -932201673;

		#region SetupAndTakedown
		void Init() 
		{
			permission.RegisterPermission(AdminUIPermission, this);
			permission.RegisterPermission(ActivityPermission, this);
			permission.RegisterPermission(HarvestPermission, this);
			permission.RegisterPermission(KillPermission, this);
			permission.RegisterPermission(OpenPermission, this);
			permission.RegisterPermission(PickupPermission, this);
			permission.RegisterPermission(WelcomePermission, this);

			lang.RegisterMessages(Messages, this);
		}

		void OnServerSave() 
		{
			SaveConf();
			SaveData();
		}

		void OnServerInitialized()  
		{
			rr = this;

			// Set up config information in advance.  
			List<string> Weapons = ItemManager.itemList.Where(x => x.category == ItemCategory.Weapon && !x.shortname.Contains("weapon.mod")).Select(x => x.shortname).Distinct().ToList();

			foreach (var entry in Weapons)
				WeaponsList.Add(entry, 1.0); 

			foreach (var entry in Resources.FindObjectsOfTypeAll<LootContainer>()) 
			{
				if (entry.ShortPrefabName.Contains("roadsign") || entry.ShortPrefabName.Contains("dm ") || entry.ShortPrefabName.Contains("test "))
					continue;
				string name = entry.ShortPrefabName;
				if (entry.PrefabName.Contains("underwater_labs"))
					name = "underwater_labs_" + entry.ShortPrefabName;
				if (Open.ContainsKey(name))
					continue;
				DictAdd(name); 
				Open.Add(name, 0.0); 
			}

			foreach (var entry in Resources.FindObjectsOfTypeAll<ItemModUnwrap>().Distinct().Select(x => x.name).Distinct().ToList())
			{
				DictAdd(entry);
				Open.Add(entry, 0.0);
			}

			foreach (var entry in Resources.FindObjectsOfTypeAll<ItemModMenuOption>().Distinct().Where(x => x.option.name.english == "Gut").Select(x => x.name).Distinct().ToList())
			{
				DictAdd(entry);
				Harvests.Flesh.Add(entry, 0.0); 
			}

			foreach (var entry in Resources.FindObjectsOfTypeAll<ResourceDispenser>().Distinct())
			{
				string name = entry.GetComponent<BaseEntity>().ShortPrefabName;
				if (name == "scientist_corpse" || name == "murderer_corpse")
					continue;
				if (entry.gatherType == ResourceDispenser.GatherType.Flesh && !Harvests.Flesh.ContainsKey(name))
				{
					DictAdd(entry.GetComponent<BaseEntity>().ShortPrefabName);
					Harvests.Flesh.Add(entry.GetComponent<BaseEntity>().ShortPrefabName, 0.0);
				}
			}

			foreach (var entry in Resources.FindObjectsOfTypeAll<CollectibleEntity>().Select(x => x.ShortPrefabName).Distinct().ToList())
			{
				DictAdd(entry);
				Pickup.Add(entry, 0.0);
			}

			foreach (var entry in Resources.FindObjectsOfTypeAll<BaseNpc>().Select(x => x.ShortPrefabName).Distinct().ToList())
			{
				DictAdd(entry);
				Kills.Animals.Add(entry, 0.0);
			}

			foreach (var entry in Resources.FindObjectsOfTypeAll<BaseEntity>().Where(x => (x is GunTrap || x is SamSite || x is AutoTurret || x is FlameTurret) && x.ShortPrefabName.Contains("deployed")).Select(x => x.ShortPrefabName).Distinct().ToList())
			{
				DictAdd(entry);
				Kills.MountedWeapons.Add(entry, 0.0);
			}

			foreach (var entry in new List<string>() { "BotSpawn", "ZombieHorde", "Scientist", "Murderer", "OilRig", "Excavator", "CompoundScientist", "BanditTown", "MountedScientist", "JunkPileScientist", "ScareCrow", "MilitaryTunnelScientist", "CargoShip", "HeavyScientist", "TunnelDweller", "Airfield", "Trainyard", "UnderwaterDweller" })
			{
				DictAdd(entry);
				Kills.NPCs.Add(entry, 0.0);
				Harvests.Flesh.Add(entry, 0.0);
			}

			List<string> Exclusions = new List<string>() { "CH47Helicopter", "CH47HelicopterAIController", "BaseArcadeMachine", "CardTable", "BaseCrane" };
			foreach (var entry in Resources.FindObjectsOfTypeAll<BaseVehicle>().Where(x => !Exclusions.Contains(x.GetType().ToString())).Select(x => x.GetType().ToString()).Distinct().ToList())
				Kills.Vehicles.Add(entry, 0.0);

			if (!LoadConfigVariables())
			{
				Puts("Config file issue detected. Please delete file, or check syntax and fix.");
				return;
			}

			foreach (var entry in conf.Group_Multipliers)
				if (!permission.GroupExists(entry.Key))
					permission.CreateGroup(entry.Key, entry.Key, 0);

			foreach (var ore in conf.RewardTypes.Harvest.Ore.Keys)
				DictAdd(ore);
			foreach (var veh in conf.RewardTypes.Kill.Vehicles.Keys)
				DictAdd(veh);

			DictAdd("Tree");
			DictAdd("simpleshark");

			foreach (var entry in conf.Permission_Multipliers.Where(x => !x.Key.Contains(".")))
				permission.RegisterPermission(Title + "." + entry.Key, this); //Register a permission for all perm multipliers. Dynamic - User can add/remove entries.
																			  //Check if permissions containing dots do already exist. If not, notify user + ignore. 

			CheckDependencies();

			cmd.AddChatCommand($"{conf.Settings.UI.MainCommandAlias}", this, "RustRewardsUI");

			if (ImageLibrary)
				ImageLibrary?.Call("AddImage", conf.Settings.UI.BackgroundImage, "RRUiImage", 0UL);

			foreach (var player in BasePlayer.activePlayerList)
				OnPlayerConnected(player);

			if (conf.Settings.Rewards.ActivityReward_Seconds > 0)
				ServerMgr.Instance.InvokeRepeating(this.CheckActivity, 1, 60);

			if (conf.Settings.Multipliers.HappyHour > 1)
				ServerMgr.Instance.InvokeRepeating(this.CheckHappyHour, 1, 15);
			SaveData();
			loaded = true;
		}
		 
		void DictAdd(string name)
		{
			if (!storedData.FriendlyNames.ContainsKey(name))
				storedData.FriendlyNames.Add(name, name);
		}

		void Unload()
		{
			foreach (BasePlayer player in BasePlayer.activePlayerList)
				DestroyMenu(player, true, false, false);

			if (conf?.Settings.Rewards.ActivityReward_Seconds > 0)
				ServerMgr.Instance.CancelInvoke(this.CheckActivity);

			if (conf?.Settings.Multipliers.HappyHour > 1)
				ServerMgr.Instance.CancelInvoke(this.CheckHappyHour);

			SaveConf();
			SaveData();
		}
		#endregion

		#region PluginMethods
		void CheckDependencies()
		{
			if (conf.Settings.RewardCurrency.UseScrap)
			{
				conf.Settings.RewardCurrency.UseEconomics = false;
				conf.Settings.RewardCurrency.UseServerRewards = false;
				Puts("Using Scrap");
			}
			else if (conf.Settings.RewardCurrency.UseEconomics && Economics)
			{
				conf.Settings.RewardCurrency.UseServerRewards = false;
				currency = Currency.Economics;
				Puts("Using Economics");
			}
			else if (conf.Settings.RewardCurrency.UseServerRewards && ServerRewards)
			{
				currency = Currency.ServerRewards;
				Puts("Using Server Rewards");
			}
			else
			{
				conf.Settings.RewardCurrency.UseScrap = true;
				Puts("Server Rewards and Economics not found - Using scrap.");
			}

			if (conf.Settings.Allies.UseFriendsPlugin && !Friends)
				Puts("Friends plugin wasn't loaded. Option has been disabled.");

			if (conf.Settings.Allies.UseClansPlugin && !Clans)
				Puts("Clans plugin wasn't loaded. Option has been disabled.");

			if (conf.Settings.Plugins.UseZoneManagerPlugin && !ZoneManager)
			{
				conf.Settings.Plugins.UseZoneManagerPlugin = false;
				Puts("Zone Manager plugin wasn't loaded. Option has been disabled.");
			}

			if (conf.Settings.Plugins.UseGUIAnnouncementsPlugin && !GUIAnnouncements)
				Puts("GUI Announcements plugin wasn't loaded. Option has been disabled.");

			if (conf.Settings.Plugins.UseNoEscape && !NoEscape)
				Puts("No Escape plugin wasn't loaded. Option has been disabled.");
		}

		Dictionary<ulong, int> RewardSeconds = new Dictionary<ulong, int>();

		void CheckActivity()
		{
			foreach (var player in BasePlayer.activePlayerList)
			{
				if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, ActivityPermission))
					return;

				if (player.secondsConnected - RewardSeconds[player.userID] < 0)
					continue;

				int Rewards = (int)Mathf.Floor((player.secondsConnected - RewardSeconds[player.userID]) / conf.Settings.Rewards.ActivityReward_Seconds);
				RewardSeconds[player.userID] += (conf.Settings.Rewards.ActivityReward_Seconds * Rewards);
				GiveReward(player, RewardType.Activity, conf.Settings.Rewards.ActivityRewardAmount * Rewards);
			}
		}

		bool first = true;
		bool HappyHourRef = false; 

		void CheckHappyHour()  
		{
			if (HappyHour() == true)
			{
				if (!HappyHourRef || first)
				{
					HappyHourRef = true;
					MessagePlayers("happyhourstart");
				}
			}
			else
			{
				if (HappyHourRef)
				{
					HappyHourRef = false;
					MessagePlayers("happyhourend");
				}
			}
			first = false;
		}

		string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

		bool HasPerm(string id, string perm) => permission.UserHasPermission(id, perm);

		string CleanIP(string ipaddress)
		{
			if (string.IsNullOrEmpty(ipaddress)) return " ";

			if (!ipaddress.Contains(":") || ipaddress.LastIndexOf(":") == 0) return ipaddress;
			return ipaddress.Substring(0, ipaddress.LastIndexOf(":"));
		}

		private void MessagePlayers(string key)
		{
			if (conf.Settings.Plugins.UseGUIAnnouncementsPlugin && GUIAnnouncements)
			{
				foreach (var player in BasePlayer.activePlayerList)
					GUIAnnouncements?.Call("CreateAnnouncement", String.Concat(Lang("Prefix", player.UserIDString), Lang(key, player.UserIDString)), conf.Settings.Announcements.GUI_Announcement_Banner_Colour, conf.Settings.Announcements.GUI_Announcement_Text_Colour, player);
			}
			else
				foreach (var player in BasePlayer.activePlayerList)
					SendReply(player, string.Format(conf.Settings.Announcements.ChatMessageFormat, Lang("Prefix", player.UserIDString), Lang(key, player.UserIDString)));
		}

		private void MessagePlayer(BasePlayer player, string msg, string prefix)
		{
			if (player?.net?.connection == null || String.IsNullOrWhiteSpace(msg))
				return;

			if (!String.IsNullOrWhiteSpace(prefix))
				msg = string.Format(conf.Settings.Announcements.ChatMessageFormat, prefix, msg);

			if (conf.Settings.Plugins.UseGUIAnnouncementsPlugin && GUIAnnouncements)
				GUIAnnouncements?.Call("CreateAnnouncement", msg, conf.Settings.Announcements.GUI_Announcement_Banner_Colour, conf.Settings.Announcements.GUI_Announcement_Text_Colour, player);
			else
				SendReply(player, msg);
		}

		private void NotifyReward(BasePlayer player, string msg, string prefix, bool GUI)
		{
			if (player?.net?.connection == null || String.IsNullOrWhiteSpace(msg))
				return;

			if (!String.IsNullOrWhiteSpace(prefix))
				msg = string.Format(conf.Settings.Announcements.ChatMessageFormat, prefix, msg);

			if (GUI)
				GUIAnnouncements?.Call("CreateAnnouncement", msg, conf.Settings.Announcements.GUI_Announcement_Banner_Colour, conf.Settings.Announcements.GUI_Announcement_Text_Colour, player);
			else
				SendReply(player, msg);
		}

		private void TakeScrap(BasePlayer player, int itemAmount)
		{
			if (player.inventory.Take(null, ScrapId, itemAmount) > 0)
				player.SendConsoleCommand("note.inv", ScrapId, itemAmount * -1);
		}

		private object GiveScrap(BasePlayer player, int amount = 1)
		{

			Item item = ItemManager.Create(ItemManager.FindItemDefinition(-932201673));

			if (item == null)
				return false;

			item.amount = amount;

			if (player == null)
			{
				item.Remove();
				return false;
			}
			if (!player.inventory.GiveItem(item, player.inventory.containerMain))
			{
				item.Remove();
				return false;
			}
			return true;
		}

		void PayPlayer(BasePlayer baseplayer, double amount)
		{
			var s = conf.Settings.RewardCurrency;
			if (currency != Currency.Economics)
				amount = Math.Round(amount, 0);
			if (amount == 0.0d)
				return;

			if (currency == Currency.Scrap)
			{
				if (amount < 0.0d)
				{
					//Puts("Rust Rewards does not currently support taking scrap from players");
					//TakeScrap(baseplayer, (int)(amount));
					//// Create a scrap debt in data? Pay it off as you collect?
					return;
				}
				else
					GiveScrap(baseplayer, (int)(amount));
			}
			else if (currency == Currency.ServerRewards)
			{
				if (amount < 0.0d)
					ServerRewards?.Call("TakePoints", baseplayer.UserIDString, -1 * (int)(amount));
				else
					ServerRewards?.Call("AddPoints", baseplayer.UserIDString, (int)(amount));
			}
			else
			{
				if (amount < 0.0d)
					Economics?.Call("Withdraw", baseplayer.UserIDString, -1 * amount);
				else
					Economics?.Call("Deposit", baseplayer.UserIDString, amount);
			} 
		}

		bool CheckPlayer(string playerId, double amount)
		{
			double balance = 0.0d;
			if (currency == Currency.ServerRewards)
				balance = (double)ServerRewards?.Call("CheckPoints", playerId);
			else if (currency == Currency.Economics)
				balance = (double)Economics?.Call("Balance", playerId);

			return !(amount.CompareTo(balance) < 0);
		}

		void GiveRustReward(BasePlayer player, int type, double amount, BaseEntity ent = null, string weapon = "", float distance = 0f, string name = null) => GiveReward(player, (RewardType)type, amount, ent, weapon, distance, name);
		void GiveReward(BasePlayer player, RewardType type, double amount, BaseEntity ent = null, string weapon = "", float distance = 0f, string name = null)
		{
			if (amount == 0) ////UNDO IT
				return;
			if (name == null)
				name = ent?.ShortPrefabName;

			double Multiplier = GetMultiplier(player, weapon, distance, ent);
			amount *= Multiplier;

			if (NoEscape && conf.Settings.Plugins.UseNoEscape)
			{
				var success = NoEscape?.Call("IsBlocked", player);

				if (success is bool && (bool)success)
				{
					MessagePlayer(player, Lang("NoEscapeBlocked", player.UserIDString), Lang("Prefix", player.UserIDString));
					return;
				}
			}

			if ((Math.Abs(amount) < 0.01d && currency == Currency.Economics) || (Math.Abs(amount) < 1.0d && currency == Currency.ServerRewards))
			{
				Puts("Net amount is too small: " + amount.ToString() + " for reason: " + type);
				return;
			}

			string formatted_amount = amount.ToString();
			if (currency == Currency.ServerRewards)
			{
				amount = Math.Round(amount, 0);
				formatted_amount = string.Format("{0:#;-#;0}", amount);
			}

			var victim = ent as BasePlayer;
			if (victim?.net?.connection != null)
			{
				if (conf.Settings.General.TakeMoneyfromVictim)
				{
					try
					{
						PayPlayer(victim, -1.0d * (amount));
						MessagePlayer(victim, Lang("VictimKilled", victim.UserIDString, victim.displayName), Lang("Prefix", victim.UserIDString));
						if (conf.Settings.General.LogToFile)
							LogToFile(Name, $"[{DateTime.Now}] " + victim.displayName + " ( " + victim.UserIDString + " / " + CleanIP(victim.net.connection.ipaddress) + " )" + " lost " + formatted_amount + " for " + type, this);
						if (conf.Settings.General.LogToConsole)
							Puts(victim.displayName + " ( " + victim.UserIDString + " / " + CleanIP(victim.net.connection.ipaddress) + " )" + " lost " + formatted_amount + " for " + type, this);
					}
					catch
					{
						MessagePlayer(player, Lang("VictimNoMoney", player.UserIDString, victim.displayName), Lang("Prefix", player.UserIDString));
						return;
					}
				}
			}
			PayPlayer(player, amount);

			if (!conf.Settings.General.Disable_All_Notifications)
			{
				var prefs = storedData.PlayerPrefs[player.userID]; 
				if (prefs.Type != 3 && prefs.ShowReward(type))
				{
					if (prefs.Type == 2)
					{
						if (amount > 0)
							RRRUI(player, type, $"+{amount}");
						else
							RRRUI(player, type, $"-{amount}");
					}
					else
						NotifyReward(player, Lang(type.ToString(), player.UserIDString, new object[] { amount, GetFriendly(name, ent), Math.Round(distance, 2) }), Lang("Prefix", player.UserIDString), prefs.Type == 1);
				}
			}
			if (conf.Settings.General.LogToFile)
				LogToFile(Name, $"[{DateTime.Now}] " + player.displayName + " ( " + player.userID + " / " + CleanIP(player.net.connection.ipaddress) + " )" + " got " + formatted_amount + " for " + type, this);
			if (conf.Settings.General.LogToConsole)
				Puts(player.displayName + " ( " + player.UserIDString + " / " + CleanIP(player.net.connection.ipaddress) + " )" + " got " + formatted_amount + " for " + type, this);
		}

		Dictionary<RewardType, string> Colours = new Dictionary<RewardType, string>()
		{
			{RewardType.Kill, "1 0 0 1"},
			{RewardType.Harvest, "0 1 0 1"},
			{RewardType.Open, "0 0 1 10"},
			{RewardType.Pickup, "1 1 0 1"},
			{RewardType.Activity, "0 1 1 1"},
			{RewardType.Welcome, "1 1 1 1"}
		};

		void RRRUI(BasePlayer player, RewardType type, string message)
		{
			CuiHelper.DestroyUi(player, "RRRUI");
			timer.Once(1.4f, () =>
			{
				if (player != null)
					CuiHelper.DestroyUi(player, "RRRUI");
			});

			var prefs = Positions[storedData.PlayerPrefs[player.userID].Position];

			var elements = new CuiElementContainer();
			var mainName = elements.Add(new CuiPanel { Image = { FadeIn = 0.7f, Color = $"0.1 0.1 0.1 0" }, RectTransform = { AnchorMin = prefs[0], AnchorMax = prefs[1] }, CursorEnabled = false }, "Overlay", "RRRUI");

			var addy = (string)ImageLibrary?.Call("GetImage", "RRUiImage");
			if (addy != null)
				elements.Add(new CuiElement { Parent = mainName, Components = { new CuiRawImageComponent { FadeIn = 0.7f, Png = addy, Sprite = Sprite }, new CuiRectTransformComponent { AnchorMin = "0.15 0.15", AnchorMax = "0.85 0.85" }, }, });

			elements.Add(new CuiLabel { Text = { FadeIn = 0.7f, Text = message, Color = Colours[type], FontSize = message.Length > 3 ? 28 : message.Length > 2 ? 34 : 38, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, }, mainName);
			CuiHelper.AddUi(player, elements);
		}

		string GetFriendly(string name, BaseEntity ent)
		{
			if (ent == null && name == null)
				return null;

			BasePlayer player = ent as BasePlayer;
			if (player != null)
			{
				if (player.userID.IsSteamId())
					return player.displayName;
				name = GetNPCType(player);
			}
			if (name == null || !storedData.FriendlyNames.ContainsKey(name))
			{
				Puts($"No friendly name for {ent?.GetType()} - Please notify author.");
				return "No Record";
			}
			return storedData.FriendlyNames[name];
		}

		double GetMultiplier(BasePlayer player, string weapon, float distance, BaseEntity ent = null)
		{
			double RBMulti = 1;
			if (ent != null && EventTerritory(ent))
				RBMulti = conf.Settings.Multipliers.RaidableBases;

			double PermMulti = 1;
			foreach (var entry in conf.Permission_Multipliers)
				if (HasPerm(player.UserIDString, Title + "." + entry.Key))
					PermMulti = Mathf.Max((float)entry.Value, (float)PermMulti);

			double GroupMulti = 1; 
			foreach (var entry in conf.Group_Multipliers)
				if (permission.UserHasGroup(player.UserIDString, entry.Key))
					GroupMulti = Mathf.Max((float)entry.Value, (float)GroupMulti);

			double DistanceMulti = conf.Settings.Multipliers.UseDynamicDistance ? 1.0f + (distance * conf.Settings.Multipliers.DynamicDistance) : Get_Distance_Multiplier(distance);
			double WeaponMulti = conf.Weapon_Multipliers.ContainsKey(weapon) ? conf.Weapon_Multipliers[weapon] : 1;
			double ZoneMulti = 0;
			bool GotZone = false;
			if (ZoneManager)
			{
				List<string> playerzones = ((string[])ZoneManager?.CallHook("GetPlayerZoneIDs", player)).ToList();
				foreach (var zone in playerzones)
					if (storedData.ZoneMultipliers.ContainsKey(zone))
					{
						ZoneMulti = Mathf.Max((float)storedData.ZoneMultipliers[zone], (float)ZoneMulti);
						GotZone = true;
					}
			}
			if (!GotZone)
				ZoneMulti = 1;
			double HappyMulti = HappyHourRef ? conf.Settings.Multipliers.HappyHour : 1; 
			//debug
			//Puts("Raidable Bases multiplier - " + RBMulti);
			//Puts("Perm multiplier - " + PermMulti);
			//Puts("Group multiplier - " + GroupMulti);
			//Puts("Distance multiplier - " + DistanceMulti);
			//Puts("Weapon multiplier - " + WeaponMulti);
			//Puts("Zone multiplier - " + ZoneMulti);

			return Math.Round(PermMulti * GroupMulti * DistanceMulti * WeaponMulti * ZoneMulti * HappyMulti * RBMulti, 2);
		}

		private ulong GetMajorityAttacker(uint id)
		{
			if (VehicleAttackers.ContainsKey(id))
				return VehicleAttackers[id].OrderByDescending(pair => pair.Value).First().Key;
			return 0U;
		}
		#endregion

		#region OxideHooks
		private void OnPlayerConnected(BasePlayer player)
		{
			if (conf == null)
				return;
			if (!RewardSeconds.ContainsKey(player.userID))
				RewardSeconds[player.userID] = player.secondsConnected;

			if (!LastKills.ContainsKey(player.userID))
				LastKills[player.userID] = new DateTime();

			if (storedData.PlayerPrefs.ContainsKey(player.userID))
				return;

			storedData.PlayerPrefs.Add(player.userID, new PlayerPrefs());

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, WelcomePermission))
				return;

			if (conf.Settings.Rewards.WelcomeMoneyAmount > 0)
				GiveReward(player, RewardType.Welcome, conf.Settings.Rewards.WelcomeMoneyAmount);
		}

		void OnDispenserGather(ResourceDispenser d, BaseEntity entity, Item item)
		{
			BasePlayer player = entity?.ToPlayer();
			var corpse = d.GetComponent<PlayerCorpse>();
			if (corpse != null)
			{
				var id = corpse.playerSteamID;
				NextTick(() =>
				{
					if (d == null && CorpseTypes.ContainsKey(id))
						GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[CorpseTypes[id]], null, "", 0, CorpseTypes[id]);
						//GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[CorpseTypes[id]], name:CorpseTypes[id]);
				});
			}

			var ent = d.GetComponent<BaseEntity>();
			if (ent != null)
			{
				var name = ent.ShortPrefabName;
				NextTick(() =>
				{
					if (ent != null)
						return;

					if (conf.RewardTypes.Harvest.Flesh.ContainsKey(name))
					{
						GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[name], ent);
						return;
					}

					if (d.gatherType == ResourceDispenser.GatherType.Tree)
					{
						GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Tree["Tree"], ent, "", 0, "Tree");
						return;
					}
				});
			}
		}

		List<uint> Bonuses = new List<uint>();
		void OnDispenserBonus(ResourceDispenser d, BasePlayer player, Item i)
		{
			if (!conf.Settings.Rewards.HarvestReward)
				return;
			if (d == null || player == null)
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, HarvestPermission))
				return;

			var ent = d.GetComponent<BaseEntity>();
			if (ent == null || Bonuses.Contains(ent.net.ID)) //metal/hqm
				return;

			Bonuses.Add(ent.net.ID);

			// Remove??
			if (d.gatherType == ResourceDispenser.GatherType.Tree)
			{
				GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Tree["Tree"], ent, "", 0, "Tree");
				return;
			}
			// ?

			foreach (var entry in ores)
				if (ent.ShortPrefabName.Contains(entry))
					GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Ore[entry], ent, "", 0, entry);
		}

		List<string> ores = new List<string>() { "metal", "stone", "sulfur" };
		void OnGrowableGathered(GrowableEntity growable, Item item, BasePlayer player)
		{
			if (player == null || growable == null || !conf.Settings.Rewards.PickupReward)
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, PickupPermission))
				return;

			if (conf.RewardTypes.Pickup.ContainsKey(growable.ShortPrefabName))
				GiveReward(player, RewardType.Pickup, conf.RewardTypes.Pickup[growable.ShortPrefabName], growable);
		}

		List<uint> UsedIDs = new List<uint>();

		void OnItemAction(Item item, string action, BasePlayer player)
		{
			if (player == null || item == null || !conf.Settings.Rewards.OpenReward)
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, OpenPermission))
				return;

			if (conf.RewardTypes.Harvest.Flesh.ContainsKey(item.info.name))
				if (action == "Gut")
					GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[item.info.name], null, "", 0, item.info.name);
					//GiveReward(player, RewardType.Harvest, conf.RewardTypes.Harvest.Flesh[item.info.name], name: item.info.name);

			if (conf.RewardTypes.Open.ContainsKey(item.info.name))
				if (action == "unwrap")
					GiveReward(player, RewardType.Open, conf.RewardTypes.Open[item.info.name], null, "", 0, item.info.name);
					//GiveReward(player, RewardType.Open, conf.RewardTypes.Open[item.info.name], name: item.info.name);
		}

		private void OnCollectiblePickup(Item item, BasePlayer player, CollectibleEntity entity)
		{
			if (player == null || entity == null || !conf.Settings.Rewards.PickupReward)
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, PickupPermission))
				return;
			if (UsedIDs.Contains(entity.net.ID))
				return;

			UsedIDs.Add(entity.net.ID);
			if (conf.RewardTypes.Pickup.ContainsKey(entity.ShortPrefabName))
				GiveReward(player, RewardType.Pickup, conf.RewardTypes.Pickup[entity.ShortPrefabName], entity);
		}

		void OnLootEntityEnd(BasePlayer player, BaseCombatEntity container)
		{
			if (player == null || container?.PrefabName == null || container?.ShortPrefabName == null || conf == null || !conf.Settings.Rewards.OpenReward)
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, OpenPermission))
				return;

			string name = container.PrefabName.Contains("underwater_labs") ? "underwater_labs_" + container.ShortPrefabName : container.ShortPrefabName;

			NextTick(() =>
			{
				if (container == null && player != null)
					if (conf.RewardTypes.Open.ContainsKey(name))
						GiveReward(player, RewardType.Open, conf.RewardTypes.Open[name], container, "", 0, name);
			});
		}

		private void OnEntityDeath(BaseEntity entity, HitInfo info)
		{
			if (entity?.net?.ID == null || !loaded)
				return;

			var ID = entity.net.ID;
			BasePlayer attacker = null;
			if (entity is BaseHelicopter || entity is BradleyAPC)
			{
				attacker = BasePlayer.FindByID(GetMajorityAttacker(entity.net.ID));
				NextTick(() =>
				{
					if (VehicleAttackers.ContainsKey(ID))
						VehicleAttackers.Remove(ID);
				});
			}

			if (attacker == null)
			{
				if (!(entity is GunTrap) && !(entity is SamSite) && !(entity is AutoTurret) && !(entity is FlameTurret))
					return;
				attacker = info?.InitiatorPlayer;
			}

			if (attacker == null)
				return;

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, KillPermission))
				return;

			if (attacker.userID == entity.OwnerID || FriendCheck(attacker.userID, entity.OwnerID))
				return;

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, entity.transform.position);

			if (conf.RewardTypes.Kill.MountedWeapons.ContainsKey(entity.ShortPrefabName))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.MountedWeapons[entity.ShortPrefabName], entity, weapon ?? "", distance);

			if (conf.RewardTypes.Kill.Vehicles.ContainsKey(entity.ShortPrefabName))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Vehicles[entity.ShortPrefabName], entity, weapon ?? "", distance);
		}

		private void OnEntityDeath(BaseVehicle vehicle, HitInfo info)
		{
			if (!loaded || vehicle == null)
				return;
	
			var ID = vehicle.net.ID;
			var attacker = BasePlayer.FindByID(GetMajorityAttacker(vehicle.net.ID));

			if (attacker?.net?.connection == null || !conf.Settings.Rewards.KillReward)
				return;

			if (attacker.userID == vehicle.OwnerID || FriendCheck(attacker.userID, vehicle.OwnerID))
				return;

			NextTick(() => 
			{
				if (VehicleAttackers.ContainsKey(ID))
					VehicleAttackers.Remove(ID);
			});

			string name = vehicle.GetType().ToString();
			if (vehicle is CH47Helicopter || vehicle is CH47HelicopterAIController)
				name = vehicle.ShortPrefabName;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, KillPermission))
				return;

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, vehicle.transform.position);

			if (conf.RewardTypes.Kill.Vehicles.ContainsKey(name))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Vehicles[name], vehicle, weapon ?? "", distance, name);
		}

		private void OnEntityDeath(BaseNpc animal, HitInfo info)
		{
			if (!loaded || animal == null)
				return;
			var attacker = info?.InitiatorPlayer;
			if (attacker?.net?.connection == null || !conf.Settings.Rewards.KillReward)
				return;

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, KillPermission))
				return;

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, animal.transform.position);

			if (conf.RewardTypes.Kill.Animals.ContainsKey(animal.ShortPrefabName))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Animals[animal.ShortPrefabName], animal, weapon ?? "", distance);
		}

		private void OnEntityDeath(SimpleShark shark, HitInfo info)
		{
			if (!loaded || shark == null)
				return;
			var attacker = info?.InitiatorPlayer;
			if (attacker?.net?.connection == null || !conf.Settings.Rewards.KillReward)
				return;

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, KillPermission))
				return;

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, shark.transform.position);

			if (conf.RewardTypes.Kill.Animals.ContainsKey(shark.ShortPrefabName))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Animals[shark.ShortPrefabName], shark, weapon ?? "", distance);
		} 

		void OnEntityTakeDamage(BaseEntity entity, HitInfo info)
		{
			if (!loaded || entity == null)
				return;
			var player = info?.InitiatorPlayer; 
			if (player == null)
				return;

			var ent = entity is BaseVehicleModule ? entity.GetComponentInParent<BaseVehicle>() : entity;
			if (ent?.net?.ID == null)
				return;

			if (!(ent is BaseHelicopter) && !(ent is BaseVehicle) && !(ent is BradleyAPC))
				return;
			if (player.userID == ent.OwnerID || FriendCheck(player.userID, ent.OwnerID))
				return;

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(player.UserIDString, KillPermission))
				return;

			if (!VehicleAttackers.ContainsKey(ent.net.ID))
				VehicleAttackers.Add(ent.net.ID, new Dictionary<ulong, int>());
			if (!VehicleAttackers[ent.net.ID].ContainsKey(player.userID))
				VehicleAttackers[ent.net.ID].Add(player.userID, 1);
			else
				VehicleAttackers[ent.net.ID][player.userID]++;
		}

		private void OnEntityDeath(LootContainer barrel, HitInfo info)
		{
			if (!loaded || barrel == null)
				return;
			var attacker = info?.InitiatorPlayer;
			if (attacker?.net?.connection == null || !conf.Settings.Rewards.OpenReward)
				return;

			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, OpenPermission))
				return;

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, barrel.transform.position);

			if (conf.RewardTypes.Open.ContainsKey(barrel.ShortPrefabName))
				GiveReward(attacker, RewardType.Open, conf.RewardTypes.Open[barrel.ShortPrefabName], barrel, weapon ?? "", distance);
		}

		Dictionary<ulong, DateTime> LastKills = new Dictionary<ulong, DateTime>();
		Dictionary<ulong, string> CorpseTypes = new Dictionary<ulong, string>();

		void OnEntityKill(BasePlayer player) => OnPlayerDeath(player, null); //Check for cases where both hooks fire.
		void OnPlayerDeath(BasePlayer player, HitInfo info)
		{
			//NRE here - No idea when/why.
			if (!loaded || player == null)
				return;

			if (player.Health() > 0)
				return;
			var attacker = info?.InitiatorPlayer;
			if (attacker?.net?.connection == null || !conf.Settings.Rewards.KillReward)
				return;
			if (conf.Settings.Rewards.Use_Permissions && !HasPerm(attacker.UserIDString, KillPermission))
				return;

			var weapon = info?.Weapon?.GetItem()?.info?.shortname ?? info?.WeaponPrefab?.ShortPrefabName;
			var distance = Vector3.Distance(attacker.transform.position, player.transform.position);

			if (player.userID.IsSteamId())
			{
				if (player == attacker)
					return;
				if (FriendCheck(attacker.userID, player.userID))
					return;
				if ((DateTime.Now - LastKills[attacker.userID]).TotalSeconds < conf.Settings.General.Player_Kill_Reward_CoolDown_Seconds)
					return;

				CorpseTypes[player.userID] = "player_corpse";

				LastKills[attacker.userID] = DateTime.Now;
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.Players["Players"], player, weapon ?? "", distance, "Players");
				return;
			}

			CorpseTypes[player.userID] = GetNPCType(player);
			if (CorpseTypes[player.userID] == null)
			{
				Puts("Null corpse type - please notify author.");
				return;
			}

			if (conf.RewardTypes.Kill.NPCs.ContainsKey(CorpseTypes[player.userID]))
				GiveReward(attacker, RewardType.Kill, conf.RewardTypes.Kill.NPCs[CorpseTypes[player.userID]], player, weapon ?? "", distance);
		}

		public string GetNPCType(BasePlayer player)
		{
			foreach (var comp in player.GetComponents<Component>())
			{
				if (comp.ToString().Contains("BotData"))
					return "BotSpawn";
				if (comp.ToString().Contains("HordeMember"))
					return "ZombieHorde";
			}

			foreach (var entry in names)
				if (player.PrefabName.Contains(entry.Key))
					return entry.Value;

			NPCPlayerApex npc = player as NPCPlayerApex;
			if (npc != null)
			{
				if (npc is NPCMurderer)
					return "Murderer";

				if (npc is Scientist)
				{
					if (npc.GetFact(NPCPlayerApex.Facts.IsMilitaryTunnelLab) == 1)
						return "MilitaryTunnelScientist";
					return "Scientist"; 
				}
			}
			else if (player is NPCPlayer)
			{
				var instance = player?.GetComponent<SpawnPointInstance>()?.parentSpawnPoint;
				if (instance != null)
				{
					var name = instance?.GetComponentInParent<PrefabParameters>()?.ToString();
					if (name != null)
					{
						foreach (var n in names)
							if (name.Contains(n.Key))
								return n.Value;
					}
				}
			}
			return "Scientist";
		}
		#endregion

		Dictionary<string, string> names = new Dictionary<string, string>() //Friendlier names for config file clarity.
		{
			{ "oilrig", "OilRig" },
			{ "excavator", "Excavator" },
			{ "scientistpeacekeeper", "CompoundScientist" },
			{ "bandit_guard", "BanditTown" },
			{ "scientist_gunner", "MountedScientist" },
			{ "junkpile", "JunkPileScientist" },
			{ "scarecrow", "ScareCrow" },
			{ "scientist_full", "MilitaryTunnelScientist" },
			{ "scientist_turret", "CargoShip" },
			{ "scientistnpc_cargo", "CargoShip" },
			{ "scientist_astar", "CargoShip" },
			{ "heavyscientist", "HeavyScientist" },
			{ "tunneldweller", "TunnelDweller" },
			{ "airfield" , "Airfield" },
			{ "trainyard" , "Trainyard" },
			{ "underwaterdweller" , "UnderwaterDweller" }
		};

		#region Allies
		bool FriendCheck(ulong player, ulong victim)
		{
			if (!player.IsSteamId() || !victim.IsSteamId())
				return false;
			if (Clans && conf.Settings.Allies.UseClansPlugin && IsClanmate(player, victim))
				return true;
			if (Friends && conf.Settings.Allies.UseFriendsPlugin && IsFriend(player, victim))
				return true;
			if (conf.Settings.Allies.UseRustTeams && IsTeamMate(player, victim))
				return true;

			return false;
		}

		bool IsClanmate(ulong playerId, ulong friendId)
		{
			object playerTag = Clans?.Call("GetClanOf", playerId);
			object friendTag = Clans?.Call("GetClanOf", friendId);
			if (playerTag is string && friendTag is string)
				if (playerTag == friendTag) return true;
			return false;
		}

		bool IsFriend(ulong playerID, ulong friendID) => (bool)Friends?.Call("IsFriend", playerID, friendID);

		bool IsTeamMate(ulong player, ulong victim)
		{
			var team1 = RelationshipManager.ServerInstance.FindPlayersTeam(player);
			var team2 = RelationshipManager.ServerInstance.FindPlayersTeam(victim); 
			return team1 != null && team2 != null && team1 == team2;
		}
		#endregion

		#region Data
		StoredData storedData;
		class StoredData
		{
			public Dictionary<ulong, PlayerPrefs> PlayerPrefs = new Dictionary<ulong, PlayerPrefs>();
			public Dictionary<string, double> ZoneMultipliers = new Dictionary<string, double>();
			public Dictionary<string, string> FriendlyNames = new Dictionary<string, string>();
		}

		class PlayerPrefs
		{
			public bool ShowReward(RewardType type)
			{
				switch (type)
				{
					case RewardType.Kill: return Show_Kills;
					case RewardType.Harvest: return Show_Harvest;
					case RewardType.Open: return Show_Open;
					case RewardType.Pickup: return Show_Pickup;
					case RewardType.Activity: return Show_Activity;
					case RewardType.Welcome: return Show_Welcome;
				}
				return false;
			}
			public int Type = 0;
			public bool Show_Kills = true;
			public bool Show_Harvest = true;
			public bool Show_Open = true;
			public bool Show_Pickup = true;
			public bool Show_Activity = true;
			public bool Show_Welcome = true;
			public int Position = 3;
		}

		public List<string[]> Positions = new List<string[]>() { new string[] { "0.05 0.8", "0.15 0.9" }, new string[] { "0.85 0.8", "0.95 0.9" }, new string[] { "0.45 0.8", "0.55 0.9" }, new string[] { "0.45 0.45", "0.55 0.55" } };
		public List<string> Indicies = new List<string>() { "Top_Left", "Top_Right", "Top_Middle", "Middle" };
		public List<string> Notification = new List<string>() { "Chat", "Banner", "Icon", "Off" };

		void Loaded()
		{
			storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("RustRewards");
			SaveData();
		}

		void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("RustRewards", storedData);
		#endregion

		#region Config
		private ConfigData conf;

		private bool LoadConfigVariables()
		{
			try
			{
				conf = Config.ReadObject<ConfigData>();
			}
			catch
			{
				return false;
			}
			SaveConf();
			return true;
		}

		protected override void LoadDefaultConfig()
		{
			Puts("Creating new config file.");
		}

		void SaveConf() => Config.WriteObject(conf, true);

		class ConfigData
		{
			public Settings Settings = new Settings();
			public Dictionary<string, double> Distance_Multipliers = new Dictionary<string, double>() { { "Distance_050", 1.0 }, { "Distance_100", 1.0 }, { "Distance_200", 1.0 }, { "Distance_300", 1.0 }, { "Distance_400", 1.0 } };
			public Dictionary<string, double> Group_Multipliers = new Dictionary<string, double>() { { "Default", 1.0 } };
			public Dictionary<string, double> Permission_Multipliers = new Dictionary<string, double>() { { "Default", 1.0 } };
			public Dictionary<string, double> Weapon_Multipliers = rr.WeaponsList;
			public RewardTypes RewardTypes = new RewardTypes();
		}
		public double Get_Distance_Multiplier(float distance) => distance >= 400 ? conf.Distance_Multipliers["Distance_400"] : distance >= 300 ? conf.Distance_Multipliers["Distance_300"] : distance >= 200 ? conf.Distance_Multipliers["Distance_200"] : distance >= 100 ? conf.Distance_Multipliers["Distance_100"] : distance >= 50 ? conf.Distance_Multipliers["Distance_050"] : 1;

		public class RewardTypes
		{
			public Kill Kill = rr.Kills;
			public Harvest Harvest = rr.Harvests;
			public Dictionary<string, double> Open = rr.Open;
			public Dictionary<string, double> Pickup = rr.Pickup;
		}

		public class Settings
		{
			public General General = new General();
			public RewardCurrency RewardCurrency = new RewardCurrency();
			public Allies Allies = new Allies();
			public ThirdPartyPlugins Plugins = new ThirdPartyPlugins();
			public Announcements Announcements = new Announcements();
			public Multipliers Multipliers = new Multipliers();
			public Rewards Rewards = new Rewards();
			public UI UI = new UI();
		}

		public class UI
		{
			public string MainCommandAlias = "rustrewards";
			public string ButtonColour = "0.7 0.32 0.17 1";
			public string ButtonColour2 = "0.4 0.1 0.1 1";
			public double Reward_Small_Increment = 1.0;
			public double Reward_Large_Increment = 10.0;
			public double Multiplier_Increment = 0.1;
			public string BackgroundImage = "https://www.wallpapertip.com/wmimgs/16-169722_transparent-background-sticky-note-clipart.png";
		}

		public class General
		{
			public bool Disable_All_Notifications = false;
			public bool TakeMoneyfromVictim = false;
			public bool LogToFile = false;
			public bool LogToConsole = false;
			public int HappyHour_BeginHour = 17;
			public int HappyHour_EndHour = 21;
			public int Player_Kill_Reward_CoolDown_Seconds = 0;
			public bool View_Reward_Values = true;
		}

		public class Allies
		{
			public bool UseFriendsPlugin = true;
			public bool UseClansPlugin = true;
			public bool UseRustTeams = true;
		}

		public class RewardCurrency
		{
			public bool UseScrap = false;
			public bool UseEconomics = false;
			public bool UseServerRewards = false;
		}
		public class ThirdPartyPlugins
		{
			public bool UseGUIAnnouncementsPlugin = false;
			public bool UseZoneManagerPlugin = false;
			public bool UseNoEscape = false;
		}

		public class Announcements
		{
			public string ChatMessageFormat = "<color=#CCBB00>{0}</color><color=#FFFFFF>{1}</color>";
			public string GUI_Announcement_Banner_Colour = "Blue";
			public string GUI_Announcement_Text_Colour = "Yellow";
		}

		public class Multipliers
		{
			public bool UseDynamicDistance = false;
			public double DynamicDistance = 0.01f;
			public double HappyHour = 1.0;
			public double RaidableBases = 1.0;
		}

		public class Rewards
		{
			public int ActivityReward_Seconds = 600;
			public double ActivityRewardAmount = 0.0;
			public double WelcomeMoneyAmount = 0.0;
			public bool Use_Permissions = false;
			public bool OpenReward = true;
			public bool KillReward = true;
			public bool PickupReward = true;
			public bool HarvestReward = true;
		}

		#endregion

		#region Messages

		Dictionary<string, string> Messages = new Dictionary<string, string>
		{
			["Kill"] = "You received {0} | Kill | {1} | {2}m.",
			["Harvest"] = "You received {0} | Harvest | {1}.",
			["Open"] = "You received {0} | Loot | {1}.",
			["Pickup"] = "You received {0} | Pickup | {1}.",
			["Activity"] = "You received an activity reward of {0}.",
			["Welcome"] = "You received a welcome reward of {0}.",
			["NotificationInfo"] = "Here you can toggle notification type Chat/Banner/Icon/Off, \nenable and disable notifications for the various categories, \nand set the position for Icon UI notifications on-screen.",
			["activity"] = "You received {0} Reward for activity.",
			["happyhourend"] = "Happy Hour(s) ended.",
			["happyhourstart"] = "Happy Hour(s) started.",
			["Prefix"] = "Rust Rewards : ",
			["rrm changed"] = "Rewards Messages for {0} is now {1}. Currently on are: {2}",
			["rrm syntax"] = "/rrm syntax:  /rrm type state  Type is one of a, h, o, p or k (Activity, Havest, Open, Pickup or Kill).  State is on or off.  for example /rrm h off",
			["rrm type"] = "type must be one of: a, h, o, p or k only. (Activity, Havest, Open, Pickup or Kill",
			["rrm state"] = "state need to be one of: on or off.",
			["VictimNoMoney"] = "{0} doesn't have enough money.",
			["VictimKilled"] = "You lost {0} Reward for being killed by a player",
			["rewardset"] = "Reward was set",
			["setrewards"] = "Variables you can set:",
			["pvptoosoon"] = "It is too soon for another reward on killing {0}!",
			["NoEscapeBlocked"] = "You can't get rewards while blocked!"
		};
		#endregion

		#region CUI
		const string Font = "robotocondensed-regular.ttf";
		const string Sprite = "assets/content/textures/generic/fulltransparent.tga";

		void OnPlayerDisconnected(BasePlayer player) => DestroyMenu(player, true, false, false);

		void DestroyMenu(BasePlayer player, bool all, bool admin, bool prefs)
		{
			if (admin)
				SaveConf();
			if (prefs)
				SaveData();

			if (all)
			{ 
				CuiHelper.DestroyUi(player, "RRPUI");
				CuiHelper.DestroyUi(player, "RRRUI");
				CuiHelper.DestroyUi(player, "RRBGUI");
			}
			CuiHelper.DestroyUi(player, "RRMainUI");
		}

		[ChatCommand("rr")]
		void RustRewardsUI(BasePlayer player, string command, string[] args)
		{
			if (conf.Settings.General.Disable_All_Notifications)
            {
				RRBGUI(player);
				RRMainUI(player, 0, 0, 0);
			}
			else
				RRPlayerUI(player);
		}

		[ConsoleCommand("rrv")]
		private void rrv(ConsoleSystem.Arg arg)
		{
			RRBGUI(arg.Player());
			RRMainUI(arg.Player(), 0, 0, 0); 
		}

		void RRPlayerUI(BasePlayer player)
		{
			DestroyMenu(player, true, false, false);
			string guiString = string.Format("0.1 0.1 0.1 0.98");
			var elements = new CuiElementContainer();
			var mainName = elements.Add(new CuiPanel { Image = { Color = guiString }, RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.95" }, CursorEnabled = true }, "Overlay", "RRPUI");
			elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0.95", AnchorMax = $"0.999 1" }, CursorEnabled = true }, mainName);
			elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"0.999 0.05" }, CursorEnabled = true }, mainName);
			elements.Add(new CuiButton { Button = { Command = "CloseRR false true", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = "0.955 0.96", AnchorMax = "0.99 0.99" }, Text = { Text = "X", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
			elements.Add(new CuiLabel { Text = { Text = $"Rust Rewards", FontSize = 20, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.2 0.95", AnchorMax = "0.8 1" } }, mainName);
			elements.Add(new CuiLabel { Text = { Text = $"Reward Notification Settings  - {player.displayName}", FontSize = 20, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.2 0.85", AnchorMax = "0.8 0.9" } }, mainName);

			double t = 0.76;
			double b = 0.8;

			var record = storedData.PlayerPrefs[player.userID];
			var fields = record.GetType().GetFields().ToList();


			elements.Add(new CuiLabel { Text = { Text = $"Type", FontSize = 16, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.38 {t}", AnchorMax = $"0.49 {b}" } }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"RRChangeType {record.Type}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.51 {t}", AnchorMax = $"0.62 {b}" }, Text = { Text = $"{Notification[record.Type]}", FontSize = 16, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

			t -= 0.05;
			b -= 0.05;

			for (int i = 0; i < fields.Count(); i++)
			{
				if (fields[i].Name == "Position" || fields[i].Name == "Type")
					continue;
				bool val = (bool)fields[i].GetValue(record);
				elements.Add(new CuiLabel { Text = { Text = $"{fields[i].Name.Replace("_", " ")}", FontSize = 16, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.38 {t}", AnchorMax = $"0.49 {b}" } }, mainName);
				elements.Add(new CuiButton { Button = { Command = $"RRChangePref {fields[i].Name}", Color = val ? conf.Settings.UI.ButtonColour : conf.Settings.UI.ButtonColour2 }, RectTransform = { AnchorMin = $"0.51 {t}", AnchorMax = $"0.62 {b}" }, Text = { Text = $"{val}", FontSize = 16, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

				t -= 0.05;
				b -= 0.05;
			}
			elements.Add(new CuiLabel { Text = { Text = $"Icon Position", FontSize = 16, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.38 {t}", AnchorMax = $"0.49 {b}" } }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"RRChangePos {record.Position}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.51 {t}", AnchorMax = $"0.62 {b}" }, Text = { Text = $"{Indicies[record.Position].Replace("_", " ")}", FontSize = 16, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

			elements.Add(new CuiLabel { Text = { Text = Lang("NotificationInfo"), FontSize = 16, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.2 0.1", AnchorMax = "0.8 0.25" } }, mainName);

			if (conf.Settings.General.View_Reward_Values || HasPerm(player.UserIDString, AdminUIPermission))
				elements.Add(new CuiButton { Button = { Command = "rrv", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.01", AnchorMax = $"0.6 0.040" }, Text = { Text = "View reward values", FontSize = 14, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);


			CuiHelper.AddUi(player, elements);
		}

		[ConsoleCommand("RRChangePref")]
		private void RRChangePref(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			var record = storedData.PlayerPrefs[player.userID];
			var field = record.GetType().GetField(arg.Args[0]);
			field.SetValue(record, !(bool)field.GetValue(record));

			RRPlayerUI(player);
		}

		[ConsoleCommand("RRChangePos")]
		private void RRChangePos(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			var record = storedData.PlayerPrefs[player.userID];
			record.Position = record.Position == 3 ? 0 : record.Position + 1;
			RRPlayerUI(player);
			SendTestNotify(player);
		}

		[ConsoleCommand("RRChangeType")]
		private void RRChangeType(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			var record = storedData.PlayerPrefs[player.userID];
			record.Type = record.Type == Notification.Count() - 1 ? 0 : record.Type + 1;
			if (!GUIAnnouncements && record.Type == 1)
				record.Type++;
			RRPlayerUI(player);
			SendTestNotify(player);
		}

		void SendTestNotify(BasePlayer player) 
        {
			if (!conf.Settings.General.Disable_All_Notifications)
			{
				var prefs = storedData.PlayerPrefs[player.userID];
				if (prefs.Type != 3 && prefs.ShowReward(RewardType.Activity))
				{
					if (prefs.Type == 2)
					{
						RRRUI(player, RewardType.Activity, $"+{1}");
					}
					else
						NotifyReward(player, "Reward notification test", Lang("Prefix", player.UserIDString), prefs.Type == 1);
				}
			}
		}

		void RRBGUI(BasePlayer player)
		{
			DestroyMenu(player, true, false, false);
			string guiString = string.Format("0.1 0.1 0.1 0.98");
			var elements = new CuiElementContainer();
			var mainName = elements.Add(new CuiPanel { Image = { Color = guiString }, RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.95" }, CursorEnabled = true }, "Overlay", "RRBGUI");
			elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0.95", AnchorMax = $"0.999 1" }, CursorEnabled = true }, mainName);
			elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"0.999 0.05" }, CursorEnabled = true }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"CloseRR {HasPerm(player.UserIDString, AdminUIPermission)} false", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = "0.955 0.96", AnchorMax = "0.99 0.99" }, Text = { Text = "X", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
			elements.Add(new CuiLabel { Text = { Text = "Rust Rewards", FontSize = 20, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.2 0.95", AnchorMax = "0.8 1" } }, mainName);
			CuiHelper.AddUi(player, elements);
		}

		void RRMainUI(BasePlayer player, int tab, int subtab, int subsubtab)
		{
			DestroyMenu(player, false, false, false);
			bool Control = HasPerm(player.UserIDString, AdminUIPermission);
			var elements = new CuiElementContainer();
			var mainName = elements.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.1 0", AnchorMax = "0.9 0.9" }, CursorEnabled = true }, "Overlay", "RRMainUI");
			elements.Add(new CuiElement { Parent = "RRMainUI", Components = { new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });

			double top = 0.875;
			double bottom = 0.9;

			elements.Add(new CuiButton { Button = { Command = $"RRUI {0} 0 0", Color = tab == 0 ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.15 0.95", AnchorMax = $"0.35 0.99" }, Text = { Text = $"Reward Values", FontSize = 18, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"RRUI {1} 0 0", Color = tab == 1 ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.95", AnchorMax = $"0.6 0.99" }, Text = { Text = $"Multipliers", FontSize = 18, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
			elements.Add(new CuiButton { Button = { Command = $"RRUI {2} 0 0", Color = tab == 2 ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.65 0.95", AnchorMax = $"0.85 0.99" }, Text = { Text = $"Zones", FontSize = 18, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

			bool odd = true;
			double l = 0.12;
			double r = 0.28;
			double left = 0;
			List<string> fields = tab == 0 ? typeof(RewardTypes).GetFields().Select(x => x.Name).ToList() : new List<string>() { "Permission", "Group", "Distance", "Weapon" };

			if (tab != 2)
				for (int i = 0; i < fields.Count(); i++)
				{
					if (fields[i] == "Activity" || fields[i] == "Welcome")
						continue;

					elements.Add(new CuiButton { Button = { Command = $"RRUI {tab} {i} 0", Color = subtab == i ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{l} 0.91", AnchorMax = $"{r} 0.935" }, Text = { Text = $"{fields[i]}", FontSize = 14, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					l += 0.2;
					r += 0.2;
				}

			l = 0.04;
			r = 0.16;

			//RewardValues
			if (tab == 0)
			{
				if (subtab == 0 || subtab == 1)
				{
					Type type = subtab == 0 ? typeof(Kill) : typeof(Harvest);
					var innerfields = type.GetFields().Select(x => x.Name).ToList();
					for (int i = 0; i < innerfields.Count(); i++)
					{

						elements.Add(new CuiButton { Button = { Command = $"RRUI {tab} {subtab} {i}", Color = subsubtab == i ? conf.Settings.UI.ButtonColour2 : conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{l} {top}", AnchorMax = $"{r} {bottom}" }, Text = { Text = $"{innerfields[i]}", FontSize = 14, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						l += 0.2;
						r += 0.2;
					}

					top -= 0.075f;
					bottom -= 0.075f;

					var records = (subtab == 0 ? type.GetField(innerfields[subsubtab]).GetValue(conf.RewardTypes.Kill) : type.GetField(innerfields[subsubtab]).GetValue(conf.RewardTypes.Harvest)) as Dictionary<string, double>;

					if (Control)
					{
						elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.02 {top}", AnchorMax = $"0.3 {bottom}" } }, mainName);

						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} - false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.242 {top + 0.003}", AnchorMax = $"0.255 {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} - false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.26 {top + 0.003}", AnchorMax = $"0.273 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} - true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.327 {top + 0.003}", AnchorMax = $"0.34 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} - true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.345 {top + 0.003}", AnchorMax = $"0.358 {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						top -= 0.025f;
						bottom -= 0.025f;
					}
					foreach (var value in records)
					{
						if (!Control && value.Value == 0)
							continue;

						if (top < 0.14)
						{
							if (left == 0.5)
							{
								Puts("UI Overflow - notify author");
								continue;
							}
							top = Control ? 0.775 : 0.8;
							bottom = Control ? 0.8 : 0.825;
							left = 0.5;
						}

						top -= 0.025f;
						bottom -= 0.025f;

						if (odd && left == 0)
							elements.Add(new CuiPanel { Image = { Color = $"0 0 0 0.8" }, RectTransform = { AnchorMin = $"0 {top}", AnchorMax = $"0.999 {bottom}" }, CursorEnabled = true }, mainName);

						elements.Add(new CuiLabel { Text = { Text = $"{value.Key}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.02} {top}", AnchorMax = $"{left + 0.3} {bottom}" } }, mainName);

						if (Control)
						{
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} {value.Key} false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.242} {top + 0.003}", AnchorMax = $"{left + 0.255} {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} {value.Key} false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.26} {top + 0.003}", AnchorMax = $"{left + 0.273} {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} {value.Key} true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.327} {top + 0.003}", AnchorMax = $"{left + 0.34} {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} {fields[subtab]} {innerfields[subsubtab]} {value.Key} true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.345} {top + 0.003}", AnchorMax = $"{left + 0.358} {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						}
						elements.Add(new CuiLabel { Text = { Text = $"{value.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.275} {top + 0.003}", AnchorMax = $"{left + 0.325} {bottom - 0.003}" } }, mainName);

						odd = !odd;
					}
				}

				if (subtab == 2)
				{
					if (Control)
					{
						elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.02 {top}", AnchorMax = $"0.3 {bottom}" } }, mainName);

						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Open - - false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.242 {top + 0.003}", AnchorMax = $"0.255 {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Open - - false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.26 {top + 0.003}", AnchorMax = $"0.273 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Open - - true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.327 {top + 0.003}", AnchorMax = $"0.34 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Open - - true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.345 {top + 0.003}", AnchorMax = $"0.358 {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						top -= 0.025f;
						bottom -= 0.025f;
					}

					foreach (var value in conf.RewardTypes.Open)
					{

						if (!Control && value.Value == 0)
							continue;
						if (top < 0.14)
						{
							if (left == 0.5)
							{
								Puts("UI Overflow - notify author");
								continue;
							}
							top = Control ? 0.85 : 0.875;
							bottom = Control ? 0.875 : 0.9;
							left = 0.5;
						}

						top -= 0.025;
						bottom -= 0.025;

						if (odd && left == 0)
							elements.Add(new CuiButton { Button = { Command = "", Color = "0 0 0 0.8" }, RectTransform = { AnchorMin = $"{left} {top}", AnchorMax = $"0.999 {bottom}" }, Text = { Text = "", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

						elements.Add(new CuiLabel { Text = { Text = $"{GetFriendly(value.Key, null)}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.02} {top}", AnchorMax = $"{left + 0.3} {bottom}" } }, mainName);

						if (Control)
						{
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Open - {value.Key} false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.242} {top + 0.003}", AnchorMax = $"{left + 0.255} {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Open - {value.Key} false 1", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.26} {top + 0.003}", AnchorMax = $"{left + 0.273} {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Open - {value.Key} true 1", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.327} {top + 0.003}", AnchorMax = $"{left + 0.34} {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Open - {value.Key} true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.345} {top + 0.003}", AnchorMax = $"{left + 0.358} {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						}
						elements.Add(new CuiLabel { Text = { Text = $"{value.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.275} {top + 0.003}", AnchorMax = $"{left + 0.325} {bottom - 0.003}" } }, mainName);
						odd = !odd;
					}
				}

				if (subtab == 3)
				{
					if (Control)
					{
						elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.02 {top}", AnchorMax = $"0.3 {bottom}" } }, mainName);

						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Pickup - - false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.242 {top + 0.003}", AnchorMax = $"0.255 {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Pickup - - false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.26 {top + 0.003}", AnchorMax = $"0.273 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Pickup - - true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.327 {top + 0.003}", AnchorMax = $"0.34 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeAll {tab} {subtab} {subsubtab} Pickup - - true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.345 {top + 0.003}", AnchorMax = $"0.358 {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						top -= 0.025f;
						bottom -= 0.025f;
					}
					foreach (var value in conf.RewardTypes.Pickup)
					{
						if (!Control && value.Value == 0)
							continue;
						if (top < 0.14)
						{
							if (left == 0.5)
							{
								Puts("UI Overflow - notify author");
								continue;
							}
							top = Control ? 0.85 : 0.875;
							bottom = Control ? 0.875 : 0.9;
							left = 0.5;
						}

						top -= 0.025;
						bottom -= 0.025;

						if (odd && left == 0)
							elements.Add(new CuiPanel { Image = { Color = $"0 0 0 0.8" }, RectTransform = { AnchorMin = $"0 {top}", AnchorMax = $"0.999 {bottom}" }, CursorEnabled = true }, mainName);

						elements.Add(new CuiLabel { Text = { Text = $"{value.Key}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.02 {top}", AnchorMax = $"0.3 {bottom}" } }, mainName);

						if (Control)
						{
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Pickup - {value.Key} false {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.242 {top + 0.003}", AnchorMax = $"0.255 {bottom - 0.003}" }, Text = { Text = "<<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Pickup - {value.Key} false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.26 {top + 0.003}", AnchorMax = $"0.273 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Pickup - {value.Key} true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.327 {top + 0.003}", AnchorMax = $"0.34 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							elements.Add(new CuiButton { Button = { Command = $"RRChangeNum {tab} {subtab} {subsubtab} Pickup - {value.Key} true {conf.Settings.UI.Reward_Large_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.345 {top + 0.003}", AnchorMax = $"0.358 {bottom - 0.003}" }, Text = { Text = ">>", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						}
						elements.Add(new CuiLabel { Text = { Text = $"{value.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"0.275 {top + 0.003}", AnchorMax = $"0.325 {bottom - 0.003}" } }, mainName);
						odd = !odd;
					}
				}
			}

			//Multipliers
			if (tab == 1)
			{
				Dictionary<string, Dictionary<string, double>> Collections = new Dictionary<string, Dictionary<string, double>>()
				{
					{ "Permission_Multipliers", conf.Permission_Multipliers },
					{ "Group_Multipliers", conf.Group_Multipliers },
					{ "Distance_Multipliers", conf.Distance_Multipliers },
					{ "Weapon_Multipliers", conf.Weapon_Multipliers }
				};

				if (Control)
				{
					elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.02 {top}", AnchorMax = $"0.3 {bottom}" } }, mainName);

					elements.Add(new CuiButton { Button = { Command = $"RRChangeAllMult {tab} {subtab} {subsubtab} {Collections.ElementAt(subtab).Key} - false {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.26 {top + 0.003}", AnchorMax = $"0.273 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					elements.Add(new CuiButton { Button = { Command = $"RRChangeAllMult {tab} {subtab} {subsubtab}  {Collections.ElementAt(subtab).Key} - true {conf.Settings.UI.Reward_Small_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.327 {top + 0.003}", AnchorMax = $"0.34 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

					top -= 0.025f;
					bottom -= 0.025f;
				}

				foreach (var value in Collections.ElementAt(subtab).Value)
				{
					if (!Control && value.Value == 1)
						continue;
					if (top < 0.14)
					{
						if (left == 0.5)
						{
							Puts("UI Overflow - notify author");
							continue;
						}
						top = Control ? 0.85 : 0.875;
						bottom = Control ? 0.875 : 0.9;
						left = 0.5;
					}

					top -= 0.025;
					bottom -= 0.025;

					if (odd && left == 0)
						elements.Add(new CuiButton { Button = { Command = "", Color = "0 0 0 0.8" }, RectTransform = { AnchorMin = $"0 {top}", AnchorMax = $"0.999 {bottom}" }, Text = { Text = "", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

					elements.Add(new CuiLabel { Text = { Text = $"{value.Key}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.02} {top}", AnchorMax = $"{left + 0.3} {bottom}" } }, mainName);

					if (Control)
					{
						elements.Add(new CuiButton { Button = { Command = $"RRChangeMult {tab} {subtab} {subsubtab} {Collections.ElementAt(subtab).Key} {value.Key} false {conf.Settings.UI.Multiplier_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.26} {top + 0.003}", AnchorMax = $"{left + 0.273} {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeMult {tab} {subtab} {subsubtab} {Collections.ElementAt(subtab).Key} {value.Key} true {conf.Settings.UI.Multiplier_Increment}", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.327} {top + 0.003}", AnchorMax = $"{left + 0.34} {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					}
					elements.Add(new CuiLabel { Text = { Text = $"{value.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.275} {top + 0.003}", AnchorMax = $"{left + 0.325} {bottom - 0.003}" } }, mainName);
					odd = !odd;
				}
			}
			if (tab == 2)
			{
				if (Control && storedData.ZoneMultipliers.Count() > 0)
				{
					elements.Add(new CuiLabel { Text = { Text = $"ALL", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"0.15 {top}", AnchorMax = $"0.25 {bottom}" } }, mainName);

					elements.Add(new CuiButton { Button = { Command = $"RRChangeAllZoneMult {tab} {subtab} {subsubtab} {storedData.ZoneMultipliers.First().Value} {conf.Settings.UI.Multiplier_Increment} false", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.26 {top + 0.003}", AnchorMax = $"0.273 {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					elements.Add(new CuiButton { Button = { Command = $"RRChangeAllZoneMult {tab} {subtab} {subsubtab}  {storedData.ZoneMultipliers.First().Value} {conf.Settings.UI.Multiplier_Increment} true", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.327 {top + 0.003}", AnchorMax = $"0.34 {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

					top -= 0.05f;
					bottom -= 0.05f;
				}

				elements.Add(new CuiLabel { Text = { Text = $"Zone ID", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.15} {top}", AnchorMax = $"{left + 0.25} {bottom}" } }, mainName);
				elements.Add(new CuiLabel { Text = { Text = $"Multiplier", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.275} {top}", AnchorMax = $"{left + 0.325} {bottom}" } }, mainName);

				foreach (var entry in storedData.ZoneMultipliers)
				{
					if (!Control && entry.Value == 1)
						continue;
					if (top < 0.3)
					{
						if (left == 0.5)
						{
							Puts("UI Overflow - notify author");
							continue;
						}
						top = Control ? 0.85 : 0.875;
						bottom = Control ? 0.875 : 0.9;
						left = 0.5;
					}

					top -= 0.025;
					bottom -= 0.025;

					if (odd && left == 0)
						elements.Add(new CuiButton { Button = { Command = "", Color = "0 0 0 0.8" }, RectTransform = { AnchorMin = $"0 {top}", AnchorMax = $"0.999 {bottom}" }, Text = { Text = "", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);

					if (Control)
					{
						elements.Add(new CuiButton { Button = { Command = $"RRChangeZoneMult {tab} {subtab} {subsubtab} {entry.Key} {conf.Settings.UI.Multiplier_Increment} false", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.26} {top + 0.003}", AnchorMax = $"{left + 0.273} {bottom - 0.003}" }, Text = { Text = "<", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
						elements.Add(new CuiButton { Button = { Command = $"RRChangeZoneMult {tab} {subtab} {subsubtab} {entry.Key} {conf.Settings.UI.Multiplier_Increment} true", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"{left + 0.327} {top + 0.003}", AnchorMax = $"{left + 0.34} {bottom - 0.003}" }, Text = { Text = ">", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
					}

					elements.Add(new CuiLabel { Text = { Text = $"{entry.Key}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleLeft }, RectTransform = { AnchorMin = $"{left + 0.15} {top}", AnchorMax = $"{left + 0.25} {bottom}" } }, mainName);
					elements.Add(new CuiLabel { Text = { Text = $"{entry.Value}", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"{left + 0.275} {top}", AnchorMax = $"{left + 0.325} {bottom}" } }, mainName);
					odd = !odd;
				}

				if (Control)
				{
					bool flag = false;
					if (ZoneManager)
					{
						List<string> playerzones = ((string[])ZoneManager?.CallHook("GetPlayerZoneIDs", player)).ToList();
						foreach (var zone in playerzones)
						{
							flag = true;
							if (!storedData.ZoneMultipliers.ContainsKey(zone))
								elements.Add(new CuiButton { Button = { Command = $"RRZone {tab} {subtab} {subsubtab} {zone} true", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.45 0.15", AnchorMax = $"0.55 0.18" }, Text = { Text = "Add current zone", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							else
								elements.Add(new CuiButton { Button = { Command = $"RRZone {tab} {subtab} {subsubtab} {zone} false", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.45 0.15", AnchorMax = $"0.55 0.18" }, Text = { Text = "Remove current zone", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
							break;
						}
					}
					if (!flag)
						elements.Add(new CuiButton { Button = { Command = "", Color = conf.Settings.UI.ButtonColour }, RectTransform = { AnchorMin = $"0.3 0.15", AnchorMax = $"0.7 0.18" }, Text = { Text = "Enter a zone to add or remove it.", FontSize = 11, Font = Font, Align = TextAnchor.MiddleCenter } }, mainName);
				}
			}
			CuiHelper.AddUi(player, elements);
		}
		#endregion

		#region UICommands
		[ConsoleCommand("RRUI")]
		private void RRUI(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			RRMainUI(player, Convert.ToInt16(arg.Args[0]), Convert.ToInt16(arg.Args[1]), Convert.ToInt16(arg.Args[2]));
		}

		[ConsoleCommand("RRChangeNum")]
		private void RRChangeNum(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			bool up = Convert.ToBoolean(arg.Args[6]);
			int num = Convert.ToInt16(arg.Args[7]);
			bool s = arg.Args[4] == "-";
			var r = conf.RewardTypes.GetType().GetField(arg.Args[3]);
			var robj = r.GetValue(conf.RewardTypes);
			var sub = s ? null : robj.GetType().GetField(arg.Args[4]);

			var subobj = s ? (Dictionary<string, double>)robj : (Dictionary<string, double>)sub.GetValue(robj);
			subobj[arg.Args[5]] = Math.Round(Mathf.Max(0, (float)(up ? subobj[arg.Args[5]] + num : subobj[arg.Args[5]] - num)), 1);

			if (!s)
				sub.SetValue(robj, subobj);
			else
				r.SetValue(conf.RewardTypes, subobj);

			RRMainUI(player, Convert.ToInt16(arg.Args[0]), Convert.ToInt16(arg.Args[1]), Convert.ToInt16(arg.Args[2]));
		}

		[ConsoleCommand("RRChangeAll")]
		private void RRChangeAll(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			bool up = Convert.ToBoolean(arg.Args[6]);
			int num = Convert.ToInt16(arg.Args[7]);
			bool s = arg.Args[4] == "-";
			var r = conf.RewardTypes.GetType().GetField(arg.Args[3]);
			var robj = r.GetValue(conf.RewardTypes);
			var sub = s ? null : robj.GetType().GetField(arg.Args[4]);

			var subobj = s ? (Dictionary<string, double>)robj : (Dictionary<string, double>)sub.GetValue(robj);

			var refnum = subobj.First().Value;
			foreach (var entry in subobj.ToDictionary(val => val.Key, val => val.Value))
				subobj[entry.Key] = Math.Round(Mathf.Max(0, (float)(up ? refnum + num : refnum - num)), 1);

			if (!s)
				sub.SetValue(robj, subobj);
			else
				r.SetValue(conf.RewardTypes, subobj);

			RRMainUI(player, Convert.ToInt16(arg.Args[0]), Convert.ToInt16(arg.Args[1]), Convert.ToInt16(arg.Args[2]));
		}

		[ConsoleCommand("RRChangeMult")]
		private void RRChangeMult(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			bool up = Convert.ToBoolean(arg.Args[5]);
			double num = Convert.ToDouble(arg.Args[6]);
			var r = conf.GetType().GetField(arg.Args[3]);
			var robj = (Dictionary<string, double>)r.GetValue(conf);
			robj[arg.Args[4]] = Math.Round(Mathf.Max(0, (float)(up ? robj[arg.Args[4]] + num : robj[arg.Args[4]] - num)), 1);
			r.SetValue(conf, robj);
			RRMainUI(player, Convert.ToInt16(arg.Args[0]), Convert.ToInt16(arg.Args[1]), Convert.ToInt16(arg.Args[2]));
		}

		[ConsoleCommand("RRChangeAllMult")]
		private void RRChangeAllMult(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			bool up = Convert.ToBoolean(arg.Args[5]);
			double num = Convert.ToDouble(arg.Args[6]);
			var r = conf.GetType().GetField(arg.Args[3]);
			var robj = (Dictionary<string, double>)r.GetValue(conf);
			var refnum = robj.First().Value;

			foreach (var entry in robj.ToDictionary(val => val.Key, val => val.Value))
				robj[entry.Key] = Math.Round(Mathf.Max(0, (float)(up ? refnum + num : refnum - num)), 1);

			r.SetValue(conf, robj);
			RRMainUI(player, Convert.ToInt16(arg.Args[0]), Convert.ToInt16(arg.Args[1]), Convert.ToInt16(arg.Args[2]));
		}

		[ConsoleCommand("RRZone")]
		private void RRZone(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);

			bool add = Convert.ToBoolean(arg.Args[4]);

			if (add)
			{
				if (!storedData.ZoneMultipliers.ContainsKey(arg.Args[3]))
					storedData.ZoneMultipliers.Add(arg.Args[3], 0.0);
			}
			else
				storedData.ZoneMultipliers.Remove(arg.Args[3]);
			RRMainUI(player, Convert.ToInt16(arg.Args[0]), Convert.ToInt16(arg.Args[1]), Convert.ToInt16(arg.Args[2]));
		}

		[ConsoleCommand("RRChangeZoneMult")]
		private void RRChangeZoneMult(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			double num = Convert.ToDouble(arg.Args[4]);
			bool up = Convert.ToBoolean(arg.Args[5]);
			var value = storedData.ZoneMultipliers[arg.Args[3]];
			storedData.ZoneMultipliers[arg.Args[3]] = Math.Round(Mathf.Max(0, (float)(up ? value + num : value - num)), 1);
			RRMainUI(player, Convert.ToInt16(arg.Args[0]), Convert.ToInt16(arg.Args[1]), Convert.ToInt16(arg.Args[2]));
		}

		[ConsoleCommand("RRChangeAllZoneMult")]
		private void RRChangeAllZoneMult(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;
			DestroyMenu(player, false, false, false);
			double num = Convert.ToDouble(arg.Args[4]);
			bool up = Convert.ToBoolean(arg.Args[5]);
			var value = Convert.ToDouble(arg.Args[3]);

			foreach (var entry in storedData.ZoneMultipliers.ToDictionary(val => val.Key, val => val.Value))
				storedData.ZoneMultipliers[entry.Key] = Math.Round(Mathf.Max(0, (float)(up ? value + num : value - num)), 1);

			RRMainUI(player, Convert.ToInt16(arg.Args[0]), Convert.ToInt16(arg.Args[1]), Convert.ToInt16(arg.Args[2]));
		}

		[ConsoleCommand("CloseRR")]
		private void CloseRR(ConsoleSystem.Arg arg)
		{
			var player = arg.Player();
			if (player == null)
				return;

			bool admin = Convert.ToBoolean(arg.Args[0]);
			bool prefs = Convert.ToBoolean(arg.Args[1]);
			DestroyMenu(player, true, admin, prefs);
		}
		#endregion
	}
}