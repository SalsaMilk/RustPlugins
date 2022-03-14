namespace Oxide.Plugins
{
    [Info("Helicopter Instant Takeoff", "bsdinis", "0.0.6")]
    [Description("Allows helicopters to instantly takeoff from the ground.")]
    class HelicopterInstantTakeoff : RustPlugin
    {
        object OnEngineStart(MiniCopter heli)
        {
            if (!heli.Grounded()) return null;
            heli.engineController.FinishStartingEngine();
            return false;
        }
    }
}