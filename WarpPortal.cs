using Rust;
using UnityEngine;
using Newtonsoft.Json;
using System.Collections.Generic;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("WarpPortal", "Blunt", "1.00")]
    [Description("Prevent Building Trigger Speed Boost")]

    public class WarpPortal : CovalencePlugin
    {
        #region Configuration

        private static PluginConfig config;

        private class PluginConfig
        {
            public WarpPortalSettings copterSettings { get; set; }

            public class WarpPortalSettings
            {
                [JsonProperty(PropertyName =
                    "Percent : Throttle will max out to this when around Monuments Triggers : ")]
                public int throttleEffect { get; set; }

                [JsonProperty(PropertyName = "Radius : Helicopter will detect Monument Triggers within this radius : ")]
                public float detectionRadius { get; set; }
            }

            public static PluginConfig DefaultConfig() => new PluginConfig()
            {
                copterSettings = new PluginConfig.WarpPortalSettings
                {
                    throttleEffect = 100,
                    detectionRadius = 1f,
                    miniCopterEnabled = true,
                    scrapHeliEnabled = true,
                }
            };
            }
            private class MiniCopterOptionsConfig
            {
                // Populated with Rust defaults.
                public float fuelPerSec = 0.25f;
                public float liftFraction = 0.25f;
                public float torqueScalePitch = 400f;
                public float torqueScaleYaw = 400f;
                public float torqueScaleRoll = 200f;

                private MiniCopterOptions plugin;

                public MiniCopterOptionsConfig(MiniCopterOptions plugin)
                {
                    this.plugin = plugin;

                    GetConfig(ref fuelPerSec, "Fuel per Second");
                    GetConfig(ref liftFraction, "Lift Fraction");
                    GetConfig(ref torqueScalePitch, "Pitch Torque Scale");
                    GetConfig(ref torqueScaleYaw, "Yaw Torque Scale");
                    GetConfig(ref torqueScaleRoll, "Roll Torque Scale");

                }
            }
        }
    }

}
