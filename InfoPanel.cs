using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("InfoPanel", "Mevent#4546", "0.1.10")]
	public class InfoPanel : RustPlugin
	{
		#region Fields

		[PluginReference] private Plugin ImageLibrary;

		private const string Layer = "UI_InfoPanel";

		private readonly List<BasePlayer> MenuUsers = new List<BasePlayer>();

		private static InfoPanel _instance;

		private readonly Dictionary<string, string> Events = new Dictionary<string, string>
		{
			["heli"] = "#FFFFFFFF",
			["air"] = "#FFFFFFFF",
			["cargo"] = "#FFFFFFFF",
			["bradley"] = "#FFFFFFFF",
			["ch47"] = "#FFFFFFFF"
		};

		#endregion

		#region Config

		private static ConfigData _config;

		private class ConfigData
		{
			[JsonProperty(PropertyName = "Server Name")]
			public ServerName ServerName = new ServerName
			{
				Display = true,
				Title = "<b>SERVER NAME</b>",
				OffMin = "43 -21",
				OffMax = "183 -5"
			};

			[JsonProperty(PropertyName = "Panel display type (Overlay or Hud)")]
			public string LayerType = "Overlay";

			[JsonProperty(PropertyName = "Panel color (background)")]
			public string LabelColor = "#A7A7A725";

			[JsonProperty(PropertyName = "Panel color (close)")]
			public string CloseColor = "#FF00003B";

			[JsonProperty(PropertyName = "Command to hide the panel")]
			public string HideCmd = "panel";

			[JsonProperty(PropertyName = "Setting the MENU button")]
			public Menu menuCfg = new Menu
			{
				Display = true,
				Title = "/MENU",
				Command = "menu",
				OffMin = "5 -55",
				OffMax = "40 -43"
			};

			[JsonProperty(PropertyName = "Settings Players")]
			public Panel UsersIcon = new Panel
			{
				Display = true,
				Image = "https://i.imgur.com/MUkpWFA.png",
				OffMin = "138 -40",
				OffMax = "183 -24"
			};

			[JsonProperty(PropertyName = "Settings Time")]
			public Panel TimeIcon = new Panel
			{
				Display = true,
				Image = "https://i.imgur.com/c5AW7sO.png",
				OffMin = "186 -21",
				OffMax = "234 -5"
			};

			[JsonProperty(PropertyName = "Settings Sleepers")]
			public Panel SleepersIcon = new Panel
			{
				Display = true,
				Image = "https://i.imgur.com/UvLItA7.png",
				OffMin = "186 -40",
				OffMax = "234 -24"
			};

			[JsonProperty(PropertyName = "Settings Сoordinates")]
			public Panel CoordsPanel = new Panel
			{
				Display = true,
				Image = "https://i.imgur.com/VicmD9Q.png",
				OffMin = "237 -21",
				OffMax = "347 -5"
			};

			[JsonProperty(PropertyName = "Settings Logotype")]
			public LogoSettings Logotype = new LogoSettings
			{
				Display = true,
				Image = "https://i.imgur.com/UFmy9HT.png",
				LogoCmd = "chat.say /store",
				OffMin = "5 -40",
				OffMax = "40 -5"
			};

			[JsonProperty(PropertyName = "Settings Economy")]
			public Economy Economy = new Economy
			{
				Display = false,
				Hook = "Balance",
				Plugin = "Economics",
				Image = "https://i.imgur.com/K4dCGkQ.png",
				OffMin = "237 -40",
				OffMax = "297 -24"
			};

			[JsonProperty(PropertyName = "Settings Events")]
			public SettingsEvents Events = new SettingsEvents
			{
				EventHelicopter = new EventSetting
				{
					Display = true,
					Image = "https://i.imgur.com/Y0rVkt8.png",
					OnColor = "#0CF204FF",
					OffColor = "#FFFFFFFF",
					OffMin = "43 -40",
					OffMax = "59 -24"
				},
				EventAirdrop = new EventSetting
				{
					Display = true,
					Image = "https://i.imgur.com/GcQKlg2.png",
					OnColor = "#0CF204FF",
					OffColor = "#FFFFFFFF",
					OffMin = "62 -40",
					OffMax = "78 -24"
				},
				EventCargoship = new EventSetting
				{
					Display = true,
					Image = "https://i.imgur.com/3jigtJS.png",
					OnColor = "#0CF204FF",
					OffColor = "#FFFFFFFF",
					OffMin = "81 -40",
					OffMax = "97 -24"
				},
				EventBradley = new EventSetting
				{
					Display = true,
					Image = "https://i.imgur.com/6Vtl3NG.png",
					OnColor = "#0CF204FF",
					OffColor = "#FFFFFFFF",
					OffMin = "100 -40",
					OffMax = "116 -24"
				},
				EventCh47 = new EventSetting
				{
					Display = true,
					Image = "https://i.imgur.com/6U5ww9g.png",
					OnColor = "#0CF204FF",
					OffColor = "#FFFFFFFF",
					OffMin = "119 -40",
					OffMax = "135 -24"
				}
			};

			[JsonProperty(PropertyName = "Menu Buttons")]
			public Buttons Buttons = new Buttons
			{
				IndentStart = -58,
				Height = 20,
				Width = 130,
				Margin = 3,
				CloseButton = new CloseMenuBTN
				{
					OffMin = "43 -55",
					OffMax = "53 -43"
				},
				List = new List<Btn>
				{
					new Btn
					{
						Image = "https://i.imgur.com/WeHYCni.png",
						Command = "chat.say /store",
						Title = "SHOP"
					},
					new Btn
					{
						Image = "https://i.imgur.com/buPPBW9.png",
						Command = "chat.say /menu",
						Title = "MENU"
					},
					new Btn
					{
						Image = "https://i.imgur.com/oFhPHky.png",
						Command = "chat.say /map",
						Title = "MAP"
					}
				}
			};
		}

		private class LogoSettings : Panel
		{
			[JsonProperty(PropertyName =
				"Command [EXAMPLE] chat command: chat.say /store  OR  console command: kill")]
			public string LogoCmd;
		}

		private abstract class MainPanel
		{
			[JsonProperty(PropertyName = "Enable display?")]
			public bool Display;

			[JsonProperty(PropertyName = "Offset Min")]
			public string OffMin;

			[JsonProperty(PropertyName = "Offset Max")]
			public string OffMax;
		}

		private class Panel : MainPanel
		{
			[JsonProperty(PropertyName = "Image URL")]
			public string Image;
		}

		private class Economy : Panel
		{
			[JsonProperty(PropertyName = "Hook")] public string Hook;

			[JsonProperty(PropertyName = "Plugin Name")]
			public string Plugin;

			public int ShowBalance(BasePlayer player)
			{
				return _instance?.plugins?.Find(Plugin)?.Call<int>(Hook, player.userID) ?? 0;
			}
		}

		private class SettingsEvents
		{
			[JsonProperty(PropertyName = "Bradley")]
			public EventSetting EventBradley;

			[JsonProperty(PropertyName = "Helicopter")]
			public EventSetting EventHelicopter;

			[JsonProperty(PropertyName = "Cargoplane")]
			public EventSetting EventAirdrop;

			[JsonProperty(PropertyName = "Cargoship")]
			public EventSetting EventCargoship;

			[JsonProperty(PropertyName = "CH47")] public EventSetting EventCh47;
		}

		private class EventSetting : Panel
		{
			[JsonProperty(PropertyName = "Active Color")]
			public string OnColor;

			[JsonProperty(PropertyName = "Deactive Color")]
			public string OffColor;
		}

		private class Buttons
		{
			[JsonProperty(PropertyName = "Screen edge offset")]
			public float IndentStart;

			[JsonProperty(PropertyName = "Width")] public float Width;

			[JsonProperty(PropertyName = "Height")]
			public float Height;

			[JsonProperty(PropertyName = "Margin")]
			public float Margin;

			[JsonProperty(PropertyName = "Close Button")]
			public CloseMenuBTN CloseButton;

			[JsonProperty(PropertyName = "Settings Buttons", ObjectCreationHandling = ObjectCreationHandling.Replace)]
			public List<Btn> List;
		}

		private class Btn
		{
			[JsonProperty(PropertyName = "Image URL")]
			public string Image;

			[JsonProperty(PropertyName = "Command")]
			public string Command;

			[JsonProperty(PropertyName = "Title")] public string Title;
		}

		private class CloseMenuBTN
		{
			[JsonProperty(PropertyName = "Offset Min")]
			public string OffMin;

			[JsonProperty(PropertyName = "Offset Max")]
			public string OffMax;
		}

		private class Menu : MainPanel
		{
			[JsonProperty(PropertyName = "Title")] public string Title;

			[JsonProperty(PropertyName = "Commnd")]
			public string Command;
		}

		private class ServerName : MainPanel
		{
			[JsonProperty(PropertyName = "Title")] public string Title;
		}

		protected override void LoadConfig()
		{
			base.LoadConfig();
			try
			{
				_config = Config.ReadObject<ConfigData>();
				if (_config == null) throw new Exception();
				SaveConfig();
			}
			catch
			{
				PrintError("Your configuration file contains an error. Using default configuration values.");
				LoadDefaultConfig();
			}
		}

		protected override void SaveConfig()
		{
			Config.WriteObject(_config);
		}

		protected override void LoadDefaultConfig()
		{
			_config = new ConfigData();
		}

		#endregion

		#region Data

		private PluginData _data;

		private void SaveData()
		{
			Interface.Oxide.DataFileSystem.WriteObject(Name, _data);
		}

		private void LoadData()
		{
			try
			{
				_data = Interface.Oxide.DataFileSystem.ReadObject<PluginData>(Name);
			}
			catch (Exception e)
			{
				PrintError(e.ToString());
			}

			if (_data == null) _data = new PluginData();
		}

		private class PluginData
		{
			[JsonProperty(PropertyName = "Players", ObjectCreationHandling = ObjectCreationHandling.Replace)]
			public List<ulong> Players = new List<ulong>();
		}

		#endregion

		#region Hooks

		private void OnServerInitialized()
		{
			if (!ImageLibrary)
			{
				PrintError("Please setup ImageLibrary plugin!");
				Interface.Oxide.UnloadPlugin(Title);
				return;
			}

			if (_config.Economy.Display && !plugins.Find(_config.Economy.Plugin))
			{
				PrintError("Please setup Economy plugin!");
				Interface.Oxide.UnloadPlugin(Title);
				return;
			}

			_instance = this;

			LoadData();

			_config.Buttons.List.ForEach(btn => ImageLibrary.Call("AddImage", btn.Image, btn.Image));

			if (_config.Logotype.Display)
				ImageLibrary.Call("AddImage", _config.Logotype.Image, _config.Logotype.Image);

			if (_config.UsersIcon.Display)
				ImageLibrary.Call("AddImage", _config.UsersIcon.Image, _config.UsersIcon.Image);

			if (_config.TimeIcon.Display)
				ImageLibrary.Call("AddImage", _config.TimeIcon.Image, _config.TimeIcon.Image);

			if (_config.SleepersIcon.Display)
				ImageLibrary.Call("AddImage", _config.SleepersIcon.Image, _config.SleepersIcon.Image);

			if (_config.CoordsPanel.Display)
				ImageLibrary.Call("AddImage", _config.CoordsPanel.Image, _config.CoordsPanel.Image);

			if (_config.Events.EventAirdrop.Display)
				ImageLibrary.Call("AddImage", _config.Events.EventAirdrop.Image,
					_config.Events.EventAirdrop.Image);

			if (_config.Events.EventBradley.Display)
				ImageLibrary.Call("AddImage", _config.Events.EventBradley.Image,
					_config.Events.EventBradley.Image);

			if (_config.Events.EventCargoship.Display)
				ImageLibrary.Call("AddImage", _config.Events.EventCargoship.Image,
					_config.Events.EventCargoship.Image);

			if (_config.Events.EventHelicopter.Display)
				ImageLibrary.Call("AddImage", _config.Events.EventHelicopter.Image,
					_config.Events.EventHelicopter.Image);

			if (_config.Events.EventCh47.Display)
				ImageLibrary.Call("AddImage", _config.Events.EventCh47.Image,
					_config.Events.EventCh47.Image);

			if (_config.Economy.Display)
				ImageLibrary.Call("AddImage", _config.Economy.Image, _config.Economy.Image);

			foreach (var entity in BaseNetworkable.serverEntities)
				OnEntitySpawned(entity as BaseEntity);

			foreach (var player in BasePlayer.activePlayerList)
				InitializeUI(player);

			if (_config.TimeIcon.Display ||
			    _config.Economy.Display ||
			    _config.CoordsPanel.Display)
				timer.Every(5, () =>
				{
					foreach (var player in BasePlayer.activePlayerList)
					{
						if (player.IsNpc) continue;

						if (_config.TimeIcon.Display)
							RefreshUI(player, "time");

						if (_config.Economy.Display)
							RefreshUI(player, "balance");

						if (_config.CoordsPanel.Display)
							RefreshUI(player, "coords");
					}
				});

			AddCovalenceCommand(_config.menuCfg.Command, nameof(CmdMenu));
			AddCovalenceCommand(_config.HideCmd, nameof(CmdSwitch));
		}

		private void Unload()
		{
			foreach (var player in BasePlayer.activePlayerList)
				CuiHelper.DestroyUi(player, Layer);

			SaveData();

			_config = null;
			_instance = null;
		}

		private void OnEntitySpawned(BaseEntity entity)
		{
			EntityHandle(entity, true);
		}

		private void OnEntityKill(BaseEntity entity)
		{
			EntityHandle(entity, false);
		}

		private readonly List<BasePlayer> ConnectPlayers = new List<BasePlayer>();

		private void OnPlayerConnected(BasePlayer player)
		{
			if (player == null || ConnectPlayers.Contains(player)) return;

			if (ConnectPlayers.Count == 0)
				Subscribe(nameof(OnPlayerSleepEnded));

			ConnectPlayers.Add(player);
		}

		private void OnPlayerSleepEnded(BasePlayer player)
		{
			if (player == null || !ConnectPlayers.Contains(player)) return;

			ConnectPlayers.Remove(player);

			if (ConnectPlayers.Count == 0)
				Unsubscribe(nameof(OnPlayerSleepEnded));

			InitializeUI(player);

			UpdateOnline();
		}

		private void OnPlayerDisconnected(BasePlayer player, string reason)
		{
			timer.In(1f, UpdateOnline);
		}

		#endregion

		#region Commands

		private void CmdMenu(IPlayer user, string command, string[] args)
		{
			var player = user?.Object as BasePlayer;
			if (player == null) return;

			if (MenuUsers.Contains(player))
			{
				CuiHelper.DestroyUi(player, Layer + ".Menu.Opened");
				MenuUsers.Remove(player);
			}
			else
			{
				ButtonsUI(player);
				MenuUsers.Add(player);
			}
		}

		[ConsoleCommand("sendconscmd")]
		private void SendCMD(ConsoleSystem.Arg args)
		{
			if (args.Player() != null)
			{
				var player = args.Player();
				var convertcmd =
					$"{args.Args[0]}  \" {string.Join(" ", args.Args.ToList().GetRange(1, args.Args.Length - 1))}\" 0";
				player.SendConsoleCommand(convertcmd);
			}
		}

		private void CmdSwitch(IPlayer cov, string command, string[] args)
		{
			var player = cov?.Object as BasePlayer;
			if (player == null) return;

			if (args.Length == 0)
			{
				if (_data.Players.Contains(player.userID))
				{
					_data.Players.Remove(player.userID);
					InitializeUI(player);
				}
				else
				{
					_data.Players.Add(player.userID);
					CuiHelper.DestroyUi(player, Layer);
				}

				return;
			}

			switch (args[0])
			{
				case "show":
				case "on":
				{
					if (_data.Players.Remove(player.userID))
						InitializeUI(player);
					break;
				}
				case "hide":
				case "off":
				{
					if (!_data.Players.Contains(player.userID))
						_data.Players.Add(player.userID);
					CuiHelper.DestroyUi(player, Layer);
					break;
				}
			}
		}

		#endregion

		#region Interface

		private void InitializeUI(BasePlayer player)
		{
			if (_data.Players.Contains(player.userID)) return;

			var container = new CuiElementContainer
			{
				{
					new CuiPanel {RectTransform = {AnchorMin = "0 1", AnchorMax = "0 1"}, Image = {Color = "0 0 0 0"}},
					_config.LayerType, Layer
				}
			};

			if (_config.Logotype.Display)
			{
				container.Add(new CuiButton
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.Logotype.OffMin,
						OffsetMax = _config.Logotype.OffMax
					},
					Button = {Color = HexToCuiColor(_config.LabelColor), Command = _config.Logotype.LogoCmd},
					Text = {Text = ""}
				}, Layer, Layer + ".Logo");

				UI.LoadImage(ref container, ".Logo.Icon", ".Logo", oMin: "2.5 2.5", oMax: "-2.5 -2.5",
					image: _config.Logotype.Image);
			}

			if (_config.TimeIcon.Display)
			{
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.TimeIcon.OffMin,
						OffsetMax = _config.TimeIcon.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".Time.Label");

				UI.LoadImage(ref container, ".Time.Icon", ".Time.Label", "0 0", "0 1", "1 1",
					"15 -1", image: _config.TimeIcon.Image);
			}

			if (_config.SleepersIcon.Display)
			{
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.SleepersIcon.OffMin,
						OffsetMax = _config.SleepersIcon.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".Sleepers.Label");
				UI.LoadImage(ref container, ".Sleepers.Icon", ".Sleepers.Label", "0 0", "0 1", "1 1",
					"15 -1", image: _config.SleepersIcon.Image);
			}

			if (_config.CoordsPanel.Display)
			{
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.CoordsPanel.OffMin,
						OffsetMax = _config.CoordsPanel.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".Coords.Label");
				UI.LoadImage(ref container, ".Coords.Icon", ".Coords.Label", "0 0", "0 1", "1 1",
					"15 -1", image: _config.CoordsPanel.Image);
			}

			if (_config.ServerName.Display)
			{
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.ServerName.OffMin,
						OffsetMax = _config.ServerName.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".ServerName");
				container.Add(new CuiLabel
				{
					RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
					Text =
					{
						FadeIn = 1f, Color = "1 1 1 1", Text = _config.ServerName.Title,
						Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 12
					}
				}, Layer + ".ServerName");
			}

			if (_config.Events.EventHelicopter.Display)
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1",
						OffsetMin = _config.Events.EventHelicopter.OffMin,
						OffsetMax = _config.Events.EventHelicopter.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".Helicopter");

			if (_config.Events.EventAirdrop.Display)
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.Events.EventAirdrop.OffMin,
						OffsetMax = _config.Events.EventAirdrop.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".Air");

			if (_config.Events.EventCargoship.Display)
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1",
						OffsetMin = _config.Events.EventCargoship.OffMin,
						OffsetMax = _config.Events.EventCargoship.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".Cargo");

			if (_config.Events.EventBradley.Display)
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.Events.EventBradley.OffMin,
						OffsetMax = _config.Events.EventBradley.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".Bradley");

			if (_config.Events.EventCh47.Display)
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.Events.EventCh47.OffMin,
						OffsetMax = _config.Events.EventCh47.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".CH47");

			if (_config.UsersIcon.Display)
			{
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.UsersIcon.OffMin,
						OffsetMax = _config.UsersIcon.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".Online.Label");
				UI.LoadImage(ref container, ".Online.Icon", ".Online.Label", "0 0", "0 1", "1 1",
					"15 -1", image: _config.UsersIcon.Image);
			}

			if (_config.Economy.Display)
			{
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.Economy.OffMin,
						OffsetMax = _config.Economy.OffMax
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer, Layer + ".Balance.Label");

				UI.LoadImage(ref container, ".Balance.Icon", ".Balance.Label", "0 0", "0 1", "1 1",
					"15 -1", image: _config.Economy.Image);
			}

			if (_config.menuCfg.Display)
				container.Add(new CuiButton
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.menuCfg.OffMin,
						OffsetMax = _config.menuCfg.OffMax
					},
					Button = {Color = HexToCuiColor(_config.LabelColor), Command = "chat.say /menu"},
					Text =
					{
						Color = "1 1 1 1", Text = _config.menuCfg.Title, Align = TextAnchor.MiddleCenter,
						Font = "robotocondensed-regular.ttf", FontSize = 10
					}
				}, Layer, Layer + ".Menu");

			CuiHelper.DestroyUi(player, Layer);
			CuiHelper.AddUi(player, container);

			RefreshUI(player, "all");
		}

		private void ButtonsUI(BasePlayer player)
		{
			if (_data.Players.Contains(player.userID)) return;

			var ButtonsContainer = new CuiElementContainer();
			var ySwitch = _config.Buttons.IndentStart;

			ButtonsContainer.Add(new CuiPanel
			{
				RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
				Image = {Color = "0 0 0 0"}
			}, Layer, Layer + ".Menu.Opened");

			ButtonsContainer.Add(new CuiPanel
			{
				RectTransform =
				{
					AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = _config.Buttons.CloseButton.OffMin,
					OffsetMax = _config.Buttons.CloseButton.OffMax
				},
				Image = {Color = HexToCuiColor(_config.CloseColor)}
			}, Layer + ".Menu.Opened", Layer + ".Menu.Opened.Close");
			ButtonsContainer.Add(new CuiButton
			{
				RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
				Button = {Color = "0 0 0 0", Command = $"chat.say /{_config.menuCfg.Command}"},
				Text =
				{
					Text = "X", Align = TextAnchor.MiddleCenter, Font = "robotocondensed-regular.ttf", FontSize = 10
				}
			}, Layer + ".Menu.Opened.Close");

			_config.Buttons.List.ForEach(button =>
			{
				ButtonsContainer.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"5 {ySwitch - _config.Buttons.Height}",
						OffsetMax = $"{_config.Buttons.Width + 5} {ySwitch}"
					},
					Image = {Color = HexToCuiColor(_config.LabelColor)}
				}, Layer + ".Menu.Opened", Layer + $".Menu.Opened.{button.Image}");

				UI.LoadImage(ref ButtonsContainer, $".Menu.Opened.{button.Image}.Img", $".Menu.Opened.{button.Image}",
					"0 0", "0 1", "3 1", "21 -1", image: button.Image);

				ButtonsContainer.Add(new CuiLabel
				{
					RectTransform =
						{AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = $"{_config.Buttons.Height + 2} 0"},
					Text =
					{
						Text = $"{button.Title}", Align = TextAnchor.MiddleCenter,
						Font = "robotocondensed-regular.ttf", FontSize = 12
					}
				}, Layer + $".Menu.Opened.{button.Image}");

				ButtonsContainer.Add(new CuiButton
				{
					RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"},
					Button = {Color = "0 0 0 0", Command = $"sendconscmd {button.Command}"},
					Text = {Text = ""}
				}, Layer + $".Menu.Opened.{button.Image}");

				ySwitch -= _config.Buttons.Height + _config.Buttons.Margin;
			});

			CuiHelper.DestroyUi(player, Layer + ".Menu.Opened");
			CuiHelper.AddUi(player, ButtonsContainer);
		}

		private void RefreshUI(BasePlayer player, string Type)
		{
			if (_data.Players.Contains(player.userID) || ConnectPlayers.Contains(player)) return;

			var container = new CuiElementContainer();

			switch (Type)
			{
				case "coords":
				{
					var pos = player.transform.position;
					UI.CreateLabel(ref container, player, ".Refresh.Coords", ".Coords.Label",
						text:
						$"X: {pos.x:0} Z: {pos.z:0}");
					break;
				}
				case "online":
				{
					UI.CreateLabel(ref container, player, ".Refresh.Online", ".Online.Label",
						text: $"{GetOnline()}");
					break;
				}
				case "balance":
				{
					UI.CreateLabel(ref container, player, ".Refresh.Balance", ".Balance.Label",
						oMin: "14 1",
						text: $"{_config.Economy.ShowBalance(player)}");
					break;
				}
				case "time":
				{
					UI.CreateLabel(ref container, player, ".Refresh.Time", ".Time.Label",
						text: TOD_Sky.Instance.Cycle.DateTime.ToString("HH:mm"));
					break;
				}
				case "sleepers":
				{
					UI.CreateLabel(ref container, player, ".Refresh.Sleepers", ".Sleepers.Label",
						text: $"{BasePlayer.sleepingPlayerList.Count}");
					break;
				}
				case "heli":
				{
					CuiHelper.DestroyUi(player, Layer + ".Events.Helicopter");
					UI.LoadImage(ref container, ".Events.Helicopter", ".Helicopter", oMin: "1 1", oMax: "-1 -1",
						color: HexToCuiColor(Events[Type]), image: _config.Events.EventHelicopter.Image);
					break;
				}
				case "air":
				{
					CuiHelper.DestroyUi(player, Layer + ".Events.Air");
					UI.LoadImage(ref container, ".Events.Air", ".Air", oMin: "1 1", oMax: "-1 -1",
						color: HexToCuiColor(Events[Type]), image: _config.Events.EventAirdrop.Image);
					break;
				}
				case "cargo":
				{
					CuiHelper.DestroyUi(player, Layer + ".Events.Cargo");
					UI.LoadImage(ref container, ".Events.Cargo", ".Cargo", oMin: "1 1", oMax: "-1 -1",
						color: HexToCuiColor(Events[Type]), image: _config.Events.EventCargoship.Image);
					break;
				}
				case "bradley":
				{
					CuiHelper.DestroyUi(player, Layer + ".Events.Bradley");
					UI.LoadImage(ref container, ".Events.Bradley", ".Bradley", oMin: "1 1", oMax: "-1 -1",
						color: HexToCuiColor(Events[Type]), image: _config.Events.EventBradley.Image);
					break;
				}
				case "ch47":
				{
					CuiHelper.DestroyUi(player, Layer + ".Events.CH47");
					UI.LoadImage(ref container, ".Events.CH47", ".CH47", oMin: "1 1", oMax: "-1 -1",
						color: HexToCuiColor(Events[Type]), image: _config.Events.EventCh47.Image);
					break;
				}
				case "all":
				{
					CuiHelper.DestroyUi(player, Layer + ".Events.Helicopter");
					CuiHelper.DestroyUi(player, Layer + ".Events.Air");
					CuiHelper.DestroyUi(player, Layer + ".Events.Cargo");
					CuiHelper.DestroyUi(player, Layer + ".Events.Bradley");
					CuiHelper.DestroyUi(player, Layer + ".Events.CH47");

					if (_config.CoordsPanel.Display)
					{
						var pos = player.transform.position;
						UI.CreateLabel(ref container, player, ".Refresh.Coords", ".Coords.Label",
							text:
							$"X: {pos.x:0} Z: {pos.z:0}");
					}

					if (_config.UsersIcon.Display)
						UI.CreateLabel(ref container, player, ".Refresh.Online", ".Online.Label",
							text: $"{GetOnline()}");

					if (_config.Economy.Display)
						UI.CreateLabel(ref container, player, ".Refresh.Balance", ".Balance.Label",
							oMin: "14 1",
							text: $"{_config.Economy.ShowBalance(player)}");

					if (_config.TimeIcon.Display)
						UI.CreateLabel(ref container, player, ".Refresh.Time", ".Time.Label",
							text: TOD_Sky.Instance.Cycle.DateTime.ToString("HH:mm"));

					if (_config.SleepersIcon.Display)
						UI.CreateLabel(ref container, player, ".Refresh.Sleepers",
							".Sleepers.Label", text: $"{BasePlayer.sleepingPlayerList.Count}");

					if (_config.Events.EventHelicopter.Display)
						UI.LoadImage(ref container, ".Events.Helicopter", ".Helicopter", oMin: "1 1",
							oMax: "-1 -1", color: HexToCuiColor(Events["heli"]),
							image: _config.Events.EventHelicopter.Image);

					if (_config.Events.EventAirdrop.Display)
						UI.LoadImage(ref container, ".Events.Air", ".Air", oMin: "1 1", oMax: "-1 -1",
							color: HexToCuiColor(Events["air"]), image: _config.Events.EventAirdrop.Image);

					if (_config.Events.EventCargoship.Display)
						UI.LoadImage(ref container, ".Events.Cargo", ".Cargo", oMin: "1 1", oMax: "-1 -1",
							color: HexToCuiColor(Events["cargo"]), image: _config.Events.EventCargoship.Image);

					if (_config.Events.EventBradley.Display)
						UI.LoadImage(ref container, ".Events.Bradley", ".Bradley", oMin: "1 1", oMax: "-1 -1",
							color: HexToCuiColor(Events["bradley"]), image: _config.Events.EventBradley.Image);

					if (_config.Events.EventCh47.Display)
						UI.LoadImage(ref container, ".Events.CH47", ".CH47", oMin: "1 1", oMax: "-1 -1",
							color: HexToCuiColor(Events["ch47"]), image: _config.Events.EventCh47.Image);
					break;
				}
			}

			CuiHelper.AddUi(player, container);
		}

		#endregion

		#region Utils

		private void UpdateOnline()
		{
			foreach (var basePlayer in BasePlayer.activePlayerList)
			{
				if (_config.UsersIcon.Display)
					RefreshUI(basePlayer, "online");

				if (_config.SleepersIcon.Display)
					RefreshUI(basePlayer, "sleepers");
			}
		}

		private class UI
		{
			public static void LoadImage(ref CuiElementContainer container, string name, string parent,
				string aMin = "0 0", string aMax = "1 1", string oMin = "13 1", string oMax = "0 -1",
				string color = "1 1 1 1", string image = "")
			{
				container.Add(new CuiElement
				{
					Name = Layer + name,
					Parent = Layer + parent,
					Components =
					{
						new CuiRawImageComponent
							{Png = _instance.ImageLibrary.Call<string>("GetImage", $"{image}"), Color = color},
						new CuiRectTransformComponent
							{AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax}
					}
				});
			}

			public static void CreateLabel(ref CuiElementContainer container, BasePlayer player, string name,
				string parent, string aMin = "0 0", string aMax = "1 1", string oMin = "13 1", string oMax = "0 -1",
				string color = "1 1 1 1", string text = "", TextAnchor align = TextAnchor.MiddleCenter,
				int fontsize = 12, string font = "robotocondensed-regular.ttf")
			{
				CuiHelper.DestroyUi(player, Layer + name);
				container.Add(new CuiLabel
				{
					RectTransform = {AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax},
					Text = {Text = text, Color = color, Align = align, Font = font, FontSize = fontsize}
				}, Layer + parent, Layer + name);
			}
		}

		private void EntityHandle(BaseEntity entity, bool spawn)
		{
			if (entity == null) return;

			if (entity is CargoPlane && _config.Events.EventAirdrop.Display)
			{
				Events["air"] = spawn ? _config.Events.EventAirdrop.OnColor : _config.Events.EventAirdrop.OffColor;
				foreach (var player in BasePlayer.activePlayerList)
					RefreshUI(player, "air");
			}

			if (entity is BradleyAPC && _config.Events.EventBradley.Display)
			{
				Events["bradley"] = spawn ? _config.Events.EventBradley.OnColor : _config.Events.EventBradley.OffColor;
				foreach (var player in BasePlayer.activePlayerList)
					RefreshUI(player, "bradley");
			}

			if (entity is BaseHelicopter && _config.Events.EventHelicopter.Display)
			{
				Events["heli"] =
					spawn ? _config.Events.EventHelicopter.OnColor : _config.Events.EventHelicopter.OffColor;
				foreach (var player in BasePlayer.activePlayerList)
					RefreshUI(player, "heli");
			}

			if (entity is CargoShip && _config.Events.EventCargoship.Display)
			{
				Events["cargo"] =
					spawn ? _config.Events.EventCargoship.OnColor : _config.Events.EventCargoship.OffColor;
				foreach (var player in BasePlayer.activePlayerList)
					RefreshUI(player, "cargo");
			}

			if (entity is CH47Helicopter && _config.Events.EventCh47.Display)
			{
				Events["ch47"] = spawn ? _config.Events.EventCh47.OnColor : _config.Events.EventCh47.OffColor;
				foreach (var player in BasePlayer.activePlayerList)
					RefreshUI(player, "ch47");
			}
		}

		private static string HexToCuiColor(string hex)
		{
			if (string.IsNullOrEmpty(hex)) hex = "#FFFFFFFF";

			var str = hex.Trim('#');

			if (str.Length == 6)
				str += "FF";

			if (str.Length != 8) throw new Exception(hex);

			var r = byte.Parse(str.Substring(0, 2), NumberStyles.HexNumber);
			var g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
			var b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);
			var a = byte.Parse(str.Substring(6, 2), NumberStyles.HexNumber);

			Color color = new Color32(r, g, b, a);

			return $"{color.r:F2} {color.g:F2} {color.b:F2} {color.a:F2}";
		}

		private int GetOnline()
		{
			return BasePlayer.activePlayerList.Count;
		}

		#endregion
	}
}