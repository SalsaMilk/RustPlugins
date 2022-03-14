namespace Oxide.Plugins
{
    [Info("No Death Screen", "Orange", "1.0.1")]
    [Description("Disables the death screen by automatically respawning players")]
    public class NoDeathScreen : RustPlugin
    {
        private const string permUse = "nodeathscreen.use";

        private void Init()
        {
            permission.RegisterPermission(permUse, this);
        }
        
        private void OnEntityDeath(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, permUse) == false)
            {
                return;
            }
            
            timer.Once(0.5f, ()=>
            {
                if (player == null)
                {
                    return;
                }
                
                if (player.IsConnected == false)
                {
                    return;
                }
                
                if (player.IsAlive())
                {
                    return;
                }
                
                player.Respawn();
            });
        }
    }
}