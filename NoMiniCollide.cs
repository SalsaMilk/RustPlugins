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

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NoMiniCollide", "Salsa", "1.1.0")]
    [Description("Anti-ramming measure for minicopters")]
    class NoMiniCollide : RustPlugin
    {
        private void Init()
        {
            DisableCollision(15, 15);
            DisableCollision(15, 8);
            DisableCollision(8, 8);
            DisableCollision(9, 9);
        }

        private void Unload()
        {
            EnableCollision(15, 15);
            EnableCollision(15, 8);
            EnableCollision(8, 8);
            EnableCollision(9, 9);
        }

        private void DisableCollision(int a, int b)
        {
            if (Physics.GetIgnoreLayerCollision(a, b))
            {
                Puts($"Collision already disabled for layers {a} and {b}");
                return;
            }
            Puts($"Disabled collision for layers {a} and {b}");
            Physics.IgnoreLayerCollision(a, b, true);
        }

        private void EnableCollision(int a, int b)
        {
            Puts($"Enabled collision for layers {a} and {b}"); 
            Physics.IgnoreLayerCollision(a, b, false);
        }

    }
}