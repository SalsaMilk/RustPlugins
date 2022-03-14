
using System.Collections.Generic;
using UnityEngine;
using Facepunch;
using System.Linq;
using System;
using System.Collections;
using Network;
using Rust;
using Oxide.Core.Plugins;
using Oxide.Core;
using UnityEngine.SceneManagement;

namespace Oxide.Plugins
{
    [Info("RustPlane", "Karuza", "01.03.03")]
    public class RustPlane : RustPlugin
    {
        private static RustPlane instance;

        private static Configuration configuration;
        private static readonly Dictionary<string, int> weaponsToAmmoTypeItemIdMap = new Dictionary<string, int>();
        protected static Effect reusableInstance = new Effect();
        List<Connection> connections = Facepunch.Pool.GetList<Connection>();

        [PluginReference]
        Plugin BulletProjectile;

        [HookMethod("OnSpawnPlane")]
        public object OnSpawnPlane(string planeType, Vector3 location, Vector3 rotation)
        {
            if (configuration == null || !configuration.PlaneConfigs.ContainsKey(planeType))
                return null;

            var newRot = Quaternion.Euler(rotation);
            DroppedItem worldModel = ItemManager.CreateByItemID(-1994909036)
                    .Drop(location, Vector3.zero, newRot)
                    .GetComponent<DroppedItem>();

            worldModel.allowPickup = false;
            worldModel.enableSaving = false;
            worldModel.globalBroadcast = true;

            // no despawn
            worldModel.Invoke("IdleDestroy", float.MaxValue);
            worldModel.CancelInvoke((Action)Delegate.CreateDelegate(typeof(Action), worldModel, "IdleDestroy"));
            var config = configuration.PlaneConfigs[planeType];
            var pc = worldModel.gameObject.AddComponent<PlaneController>();
            pc.InitializePlane(config);

            return worldModel;
        }

        object OnCollectiblePickup(Item item, BasePlayer player, CollectibleEntity entity)
        {
            if (!PlaneProp.Props.ContainsKey(entity.net.ID))
                return null;

            return true;
        }

        private void AddAmmoTypeToAmmoTypeMap(string ammoTypeShortName)
        {
            if (string.IsNullOrEmpty(ammoTypeShortName))
                return;

            if (!weaponsToAmmoTypeItemIdMap.ContainsKey(ammoTypeShortName))
                weaponsToAmmoTypeItemIdMap[ammoTypeShortName] = ItemManager.itemList.Find(x => x.shortname == ammoTypeShortName)?.itemid ?? 0;
        }

        object CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            if (!PlaneMount.Mounts.ContainsKey(entity.net.ID))
                return null;

            var planeMount = PlaneMount.Mounts[entity.net.ID];
            planeMount.MountPlayer(player);
            return true;
        }

        void OnEntityTakeDamage(Signage entity, HitInfo info)
        {
            HandlePropDamage(entity, info);
        }

        void OnEntityTakeDamage(PlanterBox entity, HitInfo info)
        {
            HandlePropDamage(entity, info);
        }

        void OnEntityTakeDamage(SolarPanel entity, HitInfo info)
        {
            HandlePropDamage(entity, info);
        }

        void OnEntityTakeDamage(LootContainer entity, HitInfo info)
        {
            HandlePropDamage(entity, info);
        }

        void HandlePropDamage(BaseEntity entity, HitInfo info)
        {
            if (!PlaneProp.Props.ContainsKey(entity.net.ID))
                return;

            var prop = PlaneProp.Props[entity.net.ID];
            prop.Plane.DoHitDamage(info);

            info.damageTypes.ScaleAll(0);
        }

        void OnEntityKill(BaseNetworkable entity)
        {
            if (entity == null)
                return;

            if (!PlaneProp.Props.ContainsKey(entity.net.ID))
                return;

            var prop = PlaneProp.Props[entity.net.ID];
            UnityEngine.Object.Destroy(prop.Plane);
        }

        object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (PlaneProp.Props.ContainsKey(entity.net.ID))
                return false;

            return null;
        }

        void OnEntityMounted(BaseMountable entity, BasePlayer player)
        {
            var parentEntity = entity?.GetParentEntity();
            if (parentEntity == null)
                return;

            if (PlaneController.Planes.ContainsKey(parentEntity.net.ID))
            {
                var planeController = PlaneController.Planes[parentEntity.net.ID];
                if (planeController.GetDriver() == player)
                {
                    planeController.AddPlayer(player);
                    return;
                }
            }
        }

        void OnServerInitialized()
        {
            instance = this;
            LoadConfig();

            foreach (var config in configuration.PlaneConfigs)
            {
                AddAmmoTypeToAmmoTypeMap(config.Value.MainCannonAmmoShortName);
            }
            GetConnections();
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            GetConnections();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            GetConnections();
        }

        private void GetConnections()
        {
            List<Connection> tempConnections = Facepunch.Pool.GetList<Connection>();
            foreach (BasePlayer target in BasePlayer.activePlayerList)
            {
                if (target.net?.connection == null)
                    continue;

                tempConnections.Add(target.net.connection);
            }

            this.connections = tempConnections;
        }

        void Unload()
        {
            DestroyPlaneControllers();
            Pool.FreeList(ref connections);
        }

        void DestroyPlaneControllers()
        {
            foreach (var pc in PlaneController.Planes.Values.ToList())
                UnityEngine.Object.Destroy(pc);
        }

        public class PlaneController : BaseVehicle
        {
            public static Dictionary<uint, PlaneController> Planes = new Dictionary<uint, PlaneController>();
            public bool IsActive { get; set; } = false;

            protected BaseHelicopterVehicle.HelicopterInputState currentInputState = new BaseHelicopterVehicle.HelicopterInputState();
            protected float currentThrottle;
            protected float engineThrustMax = 0;
            protected float pendingImpactDamage = 0.0f;
            protected float avgTerrainHeight = 0.0f;

            protected Vector3 torqueScale = Vector3.zero;
            protected Vector3 startPos;

            protected int mainCannonCurrentBarrel = 0;
            protected int secondaryCurrentBarrel = 0;
            protected int lastIndex = -1;

            protected float lastMainCannonAttackTime = 0.0f;
            protected float lastSecondaryAttackTime = 0.0f;
            protected float lastEffectTime = 0.0f;
            protected float lastDamageTime = 0.0f;
            protected float lastOnGround = 0.0f;
            protected float lastPlayerInputTime;
            protected float lastWaterCheck;
            protected float lastDoorUpdate;

            protected bool isOnGround = false;
            protected bool hasSwitch = false;
            protected bool hasEngine = false;
            protected bool hasDriver = false;

            private BaseEntity controller;
            private PlanePropeller propeller;
            private PlaneEngine engine;
            private ElectricSwitch onSwitch;
            private PlaneConfig config;

            private List<BaseMountable> mounts = new List<BaseMountable>();
            private List<BaseMountable> seats = new List<BaseMountable>();
            private List<BaseEntity> props = new List<BaseEntity>();
            private List<BaseEntity> otherProps = new List<BaseEntity>();
            private List<BaseEntity> doors = new List<BaseEntity>();
            private List<PlaneWheel> wheels = new List<PlaneWheel>();

            private uint planeNetId = 0;

            new public bool IsMounted()
            {
                foreach (var seat in seats)
                {
                    if (seat.IsMounted())
                        return true;
                }

                return false;
            }

            public bool IsOnGround
            {
                get
                {
                    if (isOnGround)
                    {
                        if (this.lastOnGround + 0.35f < Time.realtimeSinceStartup)
                            isOnGround = false;

                        return true;
                    }

                    return false;
                }
            }

            public bool InWater
            {
                get
                {
                    if (isOnGround)
                        return false;

                    return WaterLevel.Test(this.transform.position, true);
                }
            }

            public void InitializePlane(PlaneConfig config)
            {
                this.SetMaxHealth(config.MaxHealth);
                this.SetHealth(this.MaxHealth());
                this.config = config;

                this.torqueScale = config.TorqueScale;
                this.engineThrustMax = config.EngineThrustMax;
                this.rigidBody.mass = config.Mass;
                this.rigidBody.drag = config.Drag;
                this.rigidBody.angularDrag = config.AngularDrag;
                this.rigidBody.maxDepenetrationVelocity = config.MaxDepenetrationVelocity;
                this.rigidBody.centerOfMass = config.CenterOfMass;
                this.rigidBody.inertiaTensor = config.InertiaTensor;
                this.rigidBody.maxAngularVelocity = config.MaxAngularVelocity;
                this.bounds.extents = Vector3.one * 2;

                InitializeProps();
                IsActive = true;
            }

            void Awake()
            {
                this.controller = this.GetComponent<BaseEntity>();

                this.startPos = this.controller.transform.position;
                this.planeNetId = this.controller.net.ID;
                this.net = this.controller.net;
                this.controller.globalBroadcast = true;

                var rb = this.controller.GetComponent<Rigidbody>();
                this.rigidBody = rb;
                this.rigidBody.isKinematic = false;
                this.rigidBody.useGravity = true;
                this.rigidBody.detectCollisions = true;
                this.rigidBody.interpolation = RigidbodyInterpolation.None;
                this.rigidBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                this.rigidBody.constraints = RigidbodyConstraints.None;

                this.mountPoints = new List<BaseVehicle.MountPointInfo>();
                this.dismountPositions = new Transform[0];
                this.shouldShowHudHealth = true;

                Planes.Add(this.planeNetId, this);
            }

            public override void AttemptMount(BasePlayer player, bool doMountChecks = false)
            {
                var seat = this.seats.FirstOrDefault(st => !st.IsMounted());
                if (object.ReferenceEquals(seat, null))
                    return;

                seat.AttemptMount(player);

                BaseMountable idealMountPoint;
                if (!this.HasDriver())
                {
                    idealMountPoint = this.mountPoints[0].mountable;
                }
                else
                {
                    idealMountPoint = this.GetIdealMountPoint(player.eyes.position, player.transform.position);
                }

                if (object.ReferenceEquals(idealMountPoint, null))
                    base.AttemptMount(player);
                else
                    idealMountPoint.AttemptMount(player);

                if (player.GetMountedVehicle() != this)
                    return;

                this.PlayerMounted(player, idealMountPoint);
            }

            public override void PlayerMounted(BasePlayer player, BaseMountable mountPoint)
            {
                base.PlayerMounted(player, mountPoint);
                if (!object.ReferenceEquals(this.GetDriver(), null))
                    hasDriver = true;
            }

            new public BasePlayer GetDriver()
            {
                if (!this.seats.Any())
                    return null;

                return this.seats[0].GetMounted();
            }

            private void InitializeProps()
            {
                foreach (var prop in this.config.PropConfigs)
                {
                    InitializeProp(prop);
                }
            }

            private void InitializeProp(PropConfig propConfig)
            {
                BaseEntity prop;

                switch (propConfig.PropType)
                {
                    case PropType.World:
                        prop = CreateWorldModelProp(propConfig.ItemId, propConfig.Location, Quaternion.Euler(propConfig.Rotation), this, propConfig.PlanePart);
                        break;

                    case PropType.Entity:
                        prop = CreateEntityProp(propConfig.Location, propConfig.Rotation, propConfig.PrefabPath, propConfig.PlanePart);
                        break;

                    default:
                        return;
                }

                switch (propConfig.PlanePart)
                {
                    case PlanePart.Propeller:
                        // Gear
                        propeller = prop.gameObject.AddComponent<PlanePropeller>();
                        propeller.SetPlane(this);

                        // Prop 1
                        CreateWorldModelProp(1882709339, new Vector3(0.4f, 0.0f, 0), Quaternion.Euler(new Vector3(0, 0, 0)), prop, PlanePart.PropellerPart);
                        // Prop 2
                        CreateWorldModelProp(1882709339, new Vector3(-0.4f, 0.0f, 0), Quaternion.Euler(new Vector3(0, 0, 0)), prop, PlanePart.PropellerPart);

                        // Prop 3
                        CreateWorldModelProp(1882709339, new Vector3(0f, 0, 0.4f), Quaternion.Euler(new Vector3(0, 90, 0)), prop, PlanePart.PropellerPart);
                        // Prop 4
                        CreateWorldModelProp(1882709339, new Vector3(0f, 0, -0.4f), Quaternion.Euler(new Vector3(0, 90, 0)), prop, PlanePart.PropellerPart);
                        break;

                    case PlanePart.Engine:
                        this.hasEngine = true;
                        this.engine = prop.gameObject.AddComponent<PlaneEngine>();
                        this.engine.SetPlane(this);
                        break;

                    case PlanePart.Wheel:
                        var wheel = prop.gameObject.AddComponent<PlaneWheel>();
                        wheels.Add(wheel);
                        break;

                    case PlanePart.Mount:
                        InitializeMount(prop);
                        break;

                    case PlanePart.Seat:
                        InitializeSeat(prop);
                        break;

                    case PlanePart.OnSwitch:
                        this.hasSwitch = true;
                        this.onSwitch = prop as ElectricSwitch;
                        break;

                    default:
                        break;
                }
            }

            private BaseEntity CreateEntityProp(Vector3 location, Vector3 rotation, string prefab, PlanePart planePart)
            {
                var ent = GameManager.server.CreateEntity(prefab, location, Quaternion.Euler(rotation)) as BaseEntity;
                ent.enableSaving = false;
                if (prefab.Contains("chair") || prefab.Contains("sign") || prefab.Contains("refinery") || prefab.Contains("window.bars"))
                {
                    Destroy(ent.GetComponent<MeshCollider>());
                }

                var rigidbody = ent.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    rigidbody.isKinematic = true;
                }

                ent.gameObject.layer = this.gameObject.layer;
                ent.SetParent(this);
                ent.Spawn();

                ent.SetFlag(BaseEntity.Flags.Locked, true, true, true);
                var pp = ent.gameObject.AddComponent<PlaneProp>();
                pp.SetPlane(this);

                ent.syncPosition = true;
                ent.globalBroadcast = true;

                var bce = ent.GetComponent<BaseCombatEntity>();
                if (bce != null)
                {
                    bce.baseProtection.amounts[(int)DamageType.AntiVehicle] = 0.75f;
                }

                if (planePart == PlanePart.BuildingBlock)
                {
                    var buildingBlock = ent as BuildingBlock;
                    buildingBlock.SetGrade(BuildingGrade.Enum.Metal);
                    buildingBlock.SetHealthToMax();
                    buildingBlock.ClientRPC(null, "RefreshSkin");
                    buildingBlock.UpdateSkin();

                    var stability = ent.GetComponent<StabilityEntity>();
                    if (stability)
                    {
                        stability.grounded = true;
                    }

                    return ent;
                }

                if (planePart == PlanePart.Door)
                {
                    var stability = ent.GetComponent<StabilityEntity>();
                    if (stability)
                    {
                        stability.grounded = true;
                    }
                }

                if (planePart == PlanePart.OtherProp || planePart == PlanePart.Engine || planePart == PlanePart.Wheel || planePart == PlanePart.PropellerPart)
                    otherProps.Add(ent);
                else if (planePart == PlanePart.Door)
                    doors.Add(ent);
                else
                    props.Add(ent);

                return ent;
            }

            private void InitializeMount(BaseEntity entity)
            {
                var pm = entity.gameObject.AddComponent<PlaneMount>();
                pm.SetPlane(this);

                var mount = entity.GetComponent<BaseMountable>();
                //mount.isMobile = true;
                mount.syncPosition = true;
                this.mounts.Add(mount);
            }

            private void InflictWaterDamage()
            {
                Destroy(this);
            }

            private void LateUpdate()
            {
                if (!IsActive)
                    return;

                var currentTime = Time.realtimeSinceStartup;
                if (lastWaterCheck + 1f < currentTime)
                {
                    lastWaterCheck = currentTime;
                    if (InWater)
                    {
                        if (hasSwitch)
                            this.onSwitch.SetSwitch(false);
                        this.Invoke("InflictWaterDamage", 3.0f);
                        return;
                    }
                }

                UpdateDoors();

                var driver = this.GetDriver();
                if (object.ReferenceEquals(driver, null))
                    return;

                UpdateWeapons();
                DrawNames(driver);
                UpdateProps();
            }

            void UpdateDoors()
            {
                var currentTime = Time.realtimeSinceStartup;
                if (lastDoorUpdate + 0.1f > currentTime)
                {
                    return;
                }

                lastDoorUpdate = currentTime;
                foreach (var bb in this.doors)
                {
                    if (Net.sv.write.Start())
                    {
                        Net.sv.write.PacketID(Network.Message.Type.EntityDestroy);
                        Net.sv.write.EntityID(bb.net.ID);
                        Net.sv.write.Send(new SendInfo(instance.connections));
                    }

                    bb.SendNetworkUpdateImmediate();
                    bb.UpdateNetworkGroup();
                }
            }

            void UpdateProps()
            {
                if (!this.props.Any())
                    return;

                if (this.rigidBody.isKinematic)
                    return;

                var speed = this.rigidBody.velocity.magnitude;
                if (speed <= 0)
                    return;


                if (configuration.UseDistanceBasedUpdates)
                {
                    DistanceBasedUpdate();
                }
                else
                {
                    IndexBasedUpdate();
                }
            }

            void DistanceBasedUpdate()
            {
                var distance = configuration.DistanceBeforeUpdatingBody;
                if (Vector3.Distance(startPos, this.transform.position) <= distance)
                    return;

                this.startPos = this.transform.position;
                this.transform.hasChanged = true;

                //List<Connection> connections = this.GetSubscribers();
                for (int i = 0; i < this.props.Count; i++)
                {
                    if (Net.sv.write.Start())
                    {
                        Net.sv.write.PacketID(Network.Message.Type.EntityDestroy);
                        Net.sv.write.EntityID(this.props[i].net.ID);
                        Net.sv.write.Send(new SendInfo(instance.connections));
                    }

                    this.props[i].SendNetworkUpdateImmediate();
                    this.props[i].UpdateNetworkGroup();
                }
            }

            void IndexBasedUpdate()
            {
                this.lastIndex += 1;
                var propToUpdate = this.props.ElementAtOrDefault(this.lastIndex);
                if (object.ReferenceEquals(propToUpdate, null))
                {
                    this.lastIndex = 0;
                    propToUpdate = this.props.ElementAt(0);
                }

                if (Net.sv.write.Start())
                {
                    Net.sv.write.PacketID(Network.Message.Type.EntityDestroy);
                    Net.sv.write.EntityID(propToUpdate.net.ID);
                    Net.sv.write.Send(new SendInfo(instance.connections));
                }

                propToUpdate.SendNetworkUpdateImmediate();
                propToUpdate.UpdateNetworkGroup();
            }

            void DrawNames(BasePlayer driver)
            {
                if (!configuration.DrawNames)
                    return;

                bool setAdmin = false;
                if (!driver.HasPlayerFlag(BasePlayer.PlayerFlags.IsAdmin))
                {
                    driver.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                    driver.SendNetworkUpdateImmediate();
                    setAdmin = true;
                }

                foreach (var plane in Planes)
                {
                    if (!plane.Value.hasDriver)
                        continue;

                    var planeDriver = plane.Value.GetDriver();
                    if (planeDriver.net.ID == driver.net.ID)
                        continue;

                    if (Vector3.Distance(plane.Value.transform.position, this.transform.position) < 300)
                        continue;

                    Color color = Color.red;
                    if (driver.currentTeam > 0 && driver.currentTeam == planeDriver.currentTeam)
                        color = Color.green;

                    driver.SendConsoleCommand(
                           "ddraw.text",
                           0.0f,
                           Color.red,
                           planeDriver.transform.position + Vector3.up,
                           planeDriver.displayName
                       );
                }

                if (setAdmin)
                {
                    driver.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    driver.SendNetworkUpdateImmediate();
                }
            }

            private void UpdateWeapons()
            {
                UpdateWeapon(BUTTON.FIRE_PRIMARY,
                    config.MainCannonProjectileType,
                    config.MainCannonBarrelConfig,
                    config.MainCannonAmmoPrefab,
                    config.MainCannonFireRate,
                    config.MainCannonAmmoSpeed,
                    config.MainCannonDamageTypes,
                    config.MainCannonAmmoShortName,
                    config.MainCannonAimCone,
                    config.MainCannonMuzzleEffect,
                    config.MainBarrelCustomConfigs,
                    ref lastMainCannonAttackTime,
                    ref this.mainCannonCurrentBarrel);


                UpdateWeapon(BUTTON.FIRE_SECONDARY,
                    config.SecondaryProjectileType,
                    config.SecondaryBarrelConfig,
                    config.SecondaryAmmoPrefab,
                    config.SecondaryFireRate,
                    config.SecondaryAmmoSpeed,
                    config.SecondaryDamageTypes,
                    config.SecondaryAmmoShortName,
                    config.SecondaryAimCone,
                    config.SecondaryMuzzleEffect,
                    config.SecondaryBarrelCustomConfigs,
                    ref lastSecondaryAttackTime,
                    ref this.secondaryCurrentBarrel);
            }

            private void UpdateWeapon(BUTTON btn, ProjectileType projType, BarrelConfiguration barrelConfig, string prefab, float fireRate, float ammoSpeed, List<DamageTypeEntry> damageTypes, string ammoShortName, float aimCone, string muzzleEffect, List<CustomBarrelConfiguration> customBarrelConfigurations, ref float lastAttackTime, ref int currentBarrel)
            {
                if (string.IsNullOrEmpty(prefab))
                    return;

                BasePlayer driver = this.GetDriver();
                if (!driver.serverInput.IsDown(btn))
                    return;

                float currentTime = UnityEngine.Time.realtimeSinceStartup;
                if (currentTime <= lastAttackTime + fireRate)
                    return;

                if (!this.DoesUserHaveAmmo(driver, ammoShortName))
                    return;

                lastAttackTime = currentTime;
                Vector3 muzzlePos = GetMuzzlePosition(barrelConfig, currentBarrel, customBarrelConfigurations);
                if (muzzlePos == Vector3.zero)
                    return;

                TriggerMuzzleEffect(barrelConfig, muzzleEffect, currentBarrel, customBarrelConfigurations);
                Vector3 aimMod = barrelConfig == BarrelConfiguration.DualBottom ? Vector3.up * -1.5f : this.transform.forward;
                Vector3 modifiedAimDir = AimConeUtil.GetAimConeQuat(aimCone) * aimMod;
                switch (projType)
                {
                    case ProjectileType.Bullet:
                        instance.BulletProjectile.CallHook("ShootProjectile", driver, muzzlePos, modifiedAimDir * ammoSpeed, damageTypes, prefab);
                        break;

                    case ProjectileType.ServerProjectile:
                        FireWithServerProjectile(muzzlePos, modifiedAimDir, ammoSpeed, prefab, damageTypes);
                        break;

                    case ProjectileType.BombProjectile:
                        FireBombProjectile(ammoSpeed, ammoShortName, barrelConfig, damageTypes, ref currentBarrel);
                        return;

                    default:
                        break;
                }

                this.UseAmmo(driver, ammoShortName);
                UpdateBarrel(ref currentBarrel);
            }

            private void FireBombProjectile(float speed, string shortName, BarrelConfiguration barrelConfig, List<DamageTypeEntry> damageTypes, ref int currentBarrel)
            {
                float nextfireTime = config.BombFireRate;
                int barrel = currentBarrel;
                for (int i = 0; i < config.BombCount; i++)
                {
                    instance.timer.Once(nextfireTime, () =>
                    {
                        var driver = this.GetDriver();
                        if (!this.IsActive || object.ReferenceEquals(driver, null))
                            return;

                        Vector3 muzzlePos = GetMuzzlePosition(barrelConfig, barrel);
                        Effect.server.Run("assets/prefabs/deployable/research table/effects/research-table-deploy.prefab", muzzlePos, Vector3.down);

                        this.UpdateBarrel(ref barrel);
                        this.UseAmmo(driver, shortName);

                        var rotation = Quaternion.LookRotation(this.transform.forward + (this.transform.right * 90));

                        DroppedItem worldModel = ItemManager.CreateByName(shortName)
                            .Drop(muzzlePos, Vector3.down * speed, rotation)
                            .GetComponent<DroppedItem>();

                        worldModel.globalBroadcast = true;
                        worldModel.enableSaving = false;
                        worldModel.allowPickup = false;

                        var bomb = worldModel.gameObject.AddComponent<PlaneBomb>();
                        bomb.Initialize(driver, damageTypes, config.BombRadius);
                    });

                    nextfireTime += config.BombFireRate;
                }

                currentBarrel = barrel;
            }

            private GameObject FireWithServerProjectile(Vector3 muzzlePos, Vector3 aimDir, float speed, string ammoPrefab, List<DamageTypeEntry> damageTypes)
            {
                GameObject bulletEnt = GameManager.server.CreatePrefab(ammoPrefab, muzzlePos, new Quaternion(), false);
                ServerProjectile serverProjectile = bulletEnt.GetComponent<ServerProjectile>();
                TimedExplosive timedExplosive = bulletEnt.GetComponent<TimedExplosive>();

                var baseEnt = bulletEnt.GetComponent<BaseEntity>();
                BasePlayer player = this.GetDriver();
                baseEnt.creatorEntity = player;

                if (timedExplosive != null)
                {
                    timedExplosive.OwnerID = player.userID;

                    if (damageTypes != null && damageTypes.Any())
                    {
                        timedExplosive.damageTypes = damageTypes;
                    }
                }

                if (serverProjectile != null)
                {
                    serverProjectile.gravityModifier = 0.5f;
                    serverProjectile.speed = speed;
                    serverProjectile.InitializeVelocity(aimDir * speed);
                }

                baseEnt.Spawn();
                bulletEnt.SetActive(true);

                return bulletEnt;
            }


            private void UpdateBarrel(ref int nextBarrel)
            {
                if (++nextBarrel > 1)
                    nextBarrel = 0;
            }

            private void TriggerMuzzleEffect(BarrelConfiguration barrelConfig, string muzzleEffect, int currentBarrel, List<CustomBarrelConfiguration> customConfig = null)
            {
                if (string.IsNullOrEmpty(muzzleEffect))
                    return;

                Vector3 posLocal;
                switch (barrelConfig)
                {
                    case BarrelConfiguration.DualFront:
                        posLocal = (currentBarrel % 2 == 0 ? Vector3.right : Vector3.left) * 0.18f + (Vector3.forward * 2.3f) + (Vector3.up * 0.9f);
                        break;

                    case BarrelConfiguration.DualSide:
                        posLocal = (currentBarrel % 2 == 0 ? Vector3.right : Vector3.left) * 1.88f + (Vector3.forward * 1.3f) + (Vector3.up * 0.5f);
                        break;

                    case BarrelConfiguration.TieFighter:
                        posLocal = (currentBarrel % 2 == 0 ? Vector3.right : Vector3.left) * 0.18f + (Vector3.forward * 0.9f) + (Vector3.up * 0.3f);
                        Effect.server.Run("assets/prefabs/weapons/toolgun/effects/attack.prefab", this.controller, 0, posLocal, Vector3.forward, null);
                        break;

                    case BarrelConfiguration.Custom:
                        var customBarrelConfig = customConfig.ElementAtOrDefault(currentBarrel);
                        if (!customBarrelConfig.IsValid)
                        {
                            if (currentBarrel == 0)
                            {
                                throw new ArgumentNullException("Invalid Custom Barrel Config");
                            }
                            else
                            {
                                currentBarrel = 0;
                                customBarrelConfig = customConfig[0];
                            }
                        }

                        posLocal = (Vector3.right * customBarrelConfig.MuzzleFxRightModifier) + (Vector3.forward * customBarrelConfig.MuzzleFxForwardModifier) + (Vector3.up * customBarrelConfig.MuzzleFxUpModifier);
                        Effect.server.Run(customBarrelConfig.MuzzleFx, this.controller, 0, posLocal, Vector3.forward, null);
                        break;

                    case BarrelConfiguration.DualBottom:
                    default:
                        return;
                }

                Effect.server.Run(muzzleEffect, this.controller, 0, posLocal, Vector3.forward, null);
            }

            private Vector3 GetMuzzlePosition(BarrelConfiguration barrelConfig, int currentBarrel, List<CustomBarrelConfiguration> customConfig = null)
            {
                Vector3 muzzlePos = Vector3.zero;
                var vehicleTransform = this.transform;
                switch (barrelConfig)
                {
                    case BarrelConfiguration.Bottom:
                        muzzlePos = vehicleTransform.localPosition + Vector3.down;
                        break;

                    case BarrelConfiguration.SupplyDrop:
                        muzzlePos = vehicleTransform.localPosition + (Vector3.down * 7);
                        break;

                    case BarrelConfiguration.DualBottom:
                        muzzlePos = vehicleTransform.localPosition + Vector3.down + (vehicleTransform.right * 0.75f * (currentBarrel % 2 == 0 ? 1 : -1));
                        break;

                    case BarrelConfiguration.DualFront:
                        muzzlePos = vehicleTransform.localPosition + ((Vector3.up * 0.8f) + (vehicleTransform.forward * 2f) + (vehicleTransform.right * 0.25f * (currentBarrel % 2 == 0 ? 1 : -1)));
                        break;

                    case BarrelConfiguration.DualSide:
                        muzzlePos = vehicleTransform.localPosition + ((Vector3.up * 0.4f) + (vehicleTransform.forward * 1.2f) + (vehicleTransform.right * 1.15f * (currentBarrel % 2 == 0 ? 1 : -1)));
                        break;

                    case BarrelConfiguration.TieFighter:
                        muzzlePos = vehicleTransform.localPosition + ((Vector3.up * 0.3f) + (vehicleTransform.forward * 0.9f) + (vehicleTransform.right * 0.25f * (currentBarrel % 2 == 0 ? 1 : -1)));
                        break;

                    case BarrelConfiguration.Custom:
                        var customBarrelConfig = customConfig.ElementAtOrDefault(currentBarrel);
                        if (!customBarrelConfig.IsValid)
                        {
                            if (currentBarrel == 0)
                            {
                                throw new ArgumentNullException("Invalid Custom Barrel Config");
                            }
                            else
                            {
                                currentBarrel = 0;
                                customBarrelConfig = customConfig[0];
                            }
                        }

                        muzzlePos = vehicleTransform.localPosition + ((Vector3.up * customBarrelConfig.MuzzleUpModifier) + (vehicleTransform.forward * customBarrelConfig.MuzzleForwardModifier) + (vehicleTransform.right * customBarrelConfig.MuzzleRightModifier));
                        break;

                    default:
                        break;
                }

                return muzzlePos;
            }

            private void UseAmmo(BasePlayer player, string shortName)
            {
                if (config.UnlimitedAmmo)
                    return;

                var ammo = player.inventory.FindItemID(weaponsToAmmoTypeItemIdMap[shortName]);
                ammo?.UseItem(1);
            }

            private bool DoesUserHaveAmmo(BasePlayer player, string ammoTypeShortName)
            {
                if (config.UnlimitedAmmo)
                    return true;

                if (player.inventory.GetAmount(weaponsToAmmoTypeItemIdMap[ammoTypeShortName]) > 0)
                    return true;

                return false;
            }

            private DroppedItem CreateWorldModelProp(int itemId, Vector3 location, Quaternion rotation, BaseEntity parent, PlanePart planePart)
            {
                DroppedItem worldModel = ItemManager.CreateByItemID(itemId)
                    .Drop(location, Vector3.zero, rotation)
                    .GetComponent<DroppedItem>();
                worldModel.SetParent(parent);
                worldModel.gameObject.layer = parent.gameObject.layer;
                var pp = worldModel.gameObject.AddComponent<PlaneProp>();
                pp.SetPlane(this);

                worldModel.globalBroadcast = true;
                worldModel.syncPosition = true;
                worldModel.enableSaving = false;
                // if you don't want the laptop to move
                Destroy(worldModel.GetComponent<Rigidbody>());

                // if you don't want players to pick it up
                worldModel.allowPickup = false;

                // no despawn
                worldModel.Invoke("IdleDestroy", float.MaxValue);
                worldModel.CancelInvoke((Action)Delegate.CreateDelegate(typeof(Action), worldModel, "IdleDestroy"));

                if (planePart == PlanePart.OtherProp || planePart == PlanePart.Engine || planePart == PlanePart.Wheel || planePart == PlanePart.PropellerPart)
                    otherProps.Add(worldModel);
                else
                    props.Add(worldModel);

                return worldModel;
            }

            private void InitializeSeat(BaseEntity entity)
            {
                var seat = entity as BaseMountable;
                //seat.isMobile = true;
                seat.syncPosition = true;
                seat.canWieldItems = false;

                List<Transform> seatDismountPoints = Pool.GetList<Transform>();
                for (int i = 0; i < seat.dismountPositions.Length; i++)
                {
                    seatDismountPoints.Add(seat.dismountPositions[i]);
                }

                GameObject topDismount = new GameObject();
                topDismount.transform.position = this.transform.position + (this.transform.up * 3);
                topDismount.transform.transform.SetParent(this.transform, true);
                seatDismountPoints.Add(topDismount.transform);

                GameObject leftDismount = new GameObject();
                leftDismount.transform.position = this.transform.position + (this.transform.right * -3) + (this.transform.up * 1);
                leftDismount.transform.transform.SetParent(this.transform, true);
                seatDismountPoints.Add(leftDismount.transform);

                GameObject rightDismount = new GameObject();
                rightDismount.transform.position = this.transform.position + (this.transform.right * 3) + (this.transform.up * 1);
                rightDismount.transform.transform.SetParent(this.transform, true);
                seatDismountPoints.Add(rightDismount.transform);

                GameObject forwardDismount = new GameObject();
                forwardDismount.transform.position = this.transform.position + (this.transform.forward * 4) + (this.transform.up * 1);
                forwardDismount.transform.transform.SetParent(this.transform, true);
                seatDismountPoints.Add(forwardDismount.transform);

                GameObject backDismount = new GameObject();
                backDismount.transform.position = this.transform.position + (this.transform.forward * -4) + (this.transform.up * 1);
                backDismount.transform.transform.SetParent(this.transform, true);
                seatDismountPoints.Add(backDismount.transform);

                seat.dismountPositions = seatDismountPoints.ToArray();
                seat.SendNetworkUpdateImmediate();

                var mpi = new BaseVehicle.MountPointInfo()
                {
                    mountable = seat,
                    pos = seat.transform.position,
                    rot = seat.transform.rotation.eulerAngles,
                };

                this.mountPoints.Add(mpi);

                List<Transform> dismountPoints = Pool.GetList<Transform>();
                for (int i = 0; i < this.dismountPositions.Length; i++)
                {
                    dismountPoints.Add(this.dismountPositions[i]);
                }

                dismountPoints.AddRange(seatDismountPoints
                    .Where(gdmp => !dismountPoints.Any(dmp => dmp.transform.position == gdmp.transform.position)));

                this.dismountPositions = dismountPoints.ToArray();
                this.seats.Add(seat);

                Pool.FreeList(ref seatDismountPoints);
                Pool.FreeList(ref dismountPoints);
            }

            private RaycastHit deployedHitInfo;

            new void FixedUpdate()
            {
                if (!IsActive)
                    return;

                if (this.rigidBody.isKinematic)
                    return;

                BasePlayer driver = null;
                if (hasDriver)
                {
                    driver = this.GetDriver();
                    if (driver == null)
                    {
                        hasDriver = false;
                    }
                    else
                    {
                        this.PilotInput(driver.serverInput, driver);
                        driver.transform.rotation = this.transform.rotation;
                        driver.ServerRotation = this.transform.rotation;
                        driver.MovePosition(this.transform.position);
                    }
                }
                else if (isOnGround)
                {
                    return;
                }
                else
                {
                    this.SetDefaultInputState();
                }

                this.MovementUpdate(driver);
            }

            public void ProcessCollision(Collision collision)
            {
                foreach (var wheel in wheels)
                {
                    if (Vector3.Distance(collision.GetContact(0).point, wheel.transform.position) < 1)
                        return;
                }

                if (IsOnGround)
                {
                    return;
                }

                var currentTime = Time.realtimeSinceStartup;
                if (currentTime < this.lastDamageTime + 0.533f)
                    return;

                this.lastDamageTime = currentTime;
                float magnitude = collision.relativeVelocity.magnitude;
                if (collision.gameObject != null && (1 << collision.collider.gameObject.layer & 1218519297) <= 0)
                    return;

                if (magnitude < 15f)
                    return;

                float a = 0f;
                a = Mathf.InverseLerp(5f, config.CollisionMagnitude, magnitude);

                if (a <= 0.0f)
                    return;

                this.pendingImpactDamage += Mathf.Max(a, 0.08f);
                if (currentTime < this.lastEffectTime + 0.25f)
                {
                    this.lastEffectTime = currentTime;
                    Vector3 point = collision.GetContact(0).point;

                    Effect.server.Run("assets/content/vehicles/scrap heli carrier/effects/debris_effect.prefab", point + (this.transform.position - point) * 0.25f, this.transform.up, null, false);
                }

                this.Invoke("DelayedImpactDamage", 0.015f);
            }

            public void DelayedImpactDamage()
            {
                this.health -= this.pendingImpactDamage * this.MaxHealth();
                if (health <= 0)
                    Destroy(this);

                this.pendingImpactDamage = 0.0f;
            }

            private void OnCollisionStay()
            {
                this.lastOnGround = Time.realtimeSinceStartup;
                this.isOnGround = true;
            }

            private void OnCollisionEnter(Collision collision)
            {
                this.ProcessCollision(collision);
            }

            public virtual void PilotInput(InputState inputState, BasePlayer player)
            {
                this.currentInputState.Reset();
                if (inputState.IsDown(BUTTON.FORWARD))
                {
                    if (inputState.IsDown(BUTTON.DUCK) && !config.EnableVTOL)
                        this.currentInputState.throttle = 0.6f;
                    else
                        this.currentInputState.throttle = 1f;
                }
                else if (inputState.IsDown(BUTTON.BACKWARD))
                {
                    this.currentInputState.throttle = -1f;
                }
                else
                {
                    this.currentInputState.throttle = 0f;
                }

                this.currentInputState.yaw = inputState.IsDown(BUTTON.RIGHT) ? 1f : 0.0f;
                this.currentInputState.yaw -= inputState.IsDown(BUTTON.LEFT) ? 1f : 0.0f;
                this.currentInputState.pitch = this.MouseToBinary(inputState.current.mouseDelta.y);
                this.currentInputState.roll = this.MouseToBinary(-inputState.current.mouseDelta.x);
                this.lastPlayerInputTime = Time.realtimeSinceStartup;
            }

            public float MouseToBinary(float amount)
            {
                return Mathf.Clamp(amount, -1f, 1f);
            }

            public virtual void SetDefaultInputState()
            {
                if (hasSwitch)
                    this.onSwitch.ResetState();

                this.currentInputState.Reset();
                this.currentInputState.throttle = 0f;
            }

            public virtual bool IsEngineOn()
            {
                if (hasEngine)
                    return this.engine.IsOn;

                return IsSwitchOn();
            }

            public virtual bool IsSwitchOn()
            {
                if (hasSwitch)
                    return this.onSwitch.IsOn();

                return !object.ReferenceEquals(this.GetDriver(), null);
            }

            private void PerformStall()
            {
                float horizonAngle = Vector3.Dot(this.transform.forward, Vector3.up);
                if (horizonAngle > 0.1f || horizonAngle < -0.1f)
                    this.rigidBody.AddForce(Vector3.up * config.StallForce * 0.75f, ForceMode.Force);
                else
                    this.rigidBody.AddForce(Vector3.up * config.StallForce, ForceMode.Force);
            }

            public virtual void MovementUpdate(BasePlayer driver)
            {
                if (!this.IsOnGround)
                {
                    bool isStalled = false;
                    bool aboveCeiling = false;
                    if (this.rigidBody.velocity.magnitude < config.StallVelocity)
                    {
                        isStalled = true;
                    }

                    if (config.ServiceCeiling > 0)
                    {
                        isStalled = aboveCeiling = TerrainMeta.HeightMap.GetHeight(this.transform.position) + config.ServiceCeiling < this.transform.position.y;
                    }

                    if (isStalled)
                    {
                        PerformStall();
                        if (aboveCeiling)
                        {
                            return;
                        }
                    }
                }

                if (!this.IsEngineOn())
                {
                    return;
                }

                BaseHelicopterVehicle.HelicopterInputState currentInputState = this.currentInputState;

                this.currentThrottle = Mathf.Lerp(this.currentThrottle, currentInputState.throttle, config.LerpModifier * Time.fixedDeltaTime);

                if (this.IsOnGround && !config.EnableVTOL)
                {
                    this.currentThrottle = Mathf.Clamp(this.currentThrottle, -0.5f, config.MaxThrottle);
                }
                else
                {
                    this.currentThrottle = Mathf.Clamp(this.currentThrottle, 0.0f, config.MaxThrottle);
                }

                //************************************************************
                //4 Major forces on Plane: Thrust, Lift, Weight, Drag
                //************************************************************

                float angleOfAttack = -Mathf.Deg2Rad * Vector3.Dot(this.rigidBody.velocity, this.transform.up);
                float slipAoA = Mathf.Deg2Rad * Vector3.Dot(this.rigidBody.velocity, this.transform.right);

                var sideSlipCoefficient = 0.001f * slipAoA;
                var liftCoefficient = 8000 * angleOfAttack;
                var thrustCoefficient = this.currentThrottle * this.engineThrustMax;

                //************************************************************
                // Pitch from lift, yaw & roll from sideslip
                //************************************************************
                var rollCoEfficient = currentInputState.roll * this.torqueScale.z;
                var pitchCoEfficient = currentInputState.pitch * this.torqueScale.x;
                float yawCoEfficient = currentInputState.yaw * this.torqueScale.y;
                pitchCoEfficient += liftCoefficient * 0.001f;
                yawCoEfficient += -sideSlipCoefficient * 2f;
                rollCoEfficient += -sideSlipCoefficient * 0.1f;

                //************************************************************
                // Calc vectors now we have coefficients
                //************************************************************
                var ThrustForceVector = (this.transform.forward * 3) * thrustCoefficient;
                var LiftVector = this.transform.up * liftCoefficient * 1;
                var SideSlip = Vector3.right * sideSlipCoefficient;

                var RollTorque = Vector3.forward * rollCoEfficient;
                var YawTorque = this.transform.up * yawCoEfficient;
                var PitchTorque = (Vector3.right * pitchCoEfficient) * 2;

                //************************************************************
                // Apply forces
                //************************************************************
                if (hasDriver && config.EnableVTOL && driver.serverInput.IsDown(BUTTON.DUCK))
                {
                    this.rigidBody.AddForce((this.transform.up * config.VTOLModifier) * thrustCoefficient, ForceMode.Force);
                    this.rigidBody.AddRelativeTorque(RollTorque);
                    this.rigidBody.AddTorque(YawTorque);
                }
                else
                {
                    this.rigidBody.AddForce(ThrustForceVector, ForceMode.Force);
                    if (this.IsOnGround)
                    {
                        if (this.currentThrottle > 0)
                        {
                            this.rigidBody.AddForce(LiftVector * config.TakeOffLiftModifier, ForceMode.Force);
                            if (currentInputState.yaw > 0)
                                this.rigidBody.transform.Rotate(this.rigidBody.transform.up, 1);
                            else if (currentInputState.yaw < 0)
                                this.rigidBody.transform.Rotate(this.rigidBody.transform.up, -1);
                        }
                    }
                    else
                    {
                        this.rigidBody.AddForce(LiftVector * config.LiftModifier, ForceMode.Force);
                        this.rigidBody.AddForce(SideSlip, ForceMode.Force);
                        this.rigidBody.AddRelativeTorque(RollTorque);
                        this.rigidBody.AddTorque(YawTorque);
                    }
                }

                this.rigidBody.AddRelativeTorque(PitchTorque);
            }

            public void AddPlayer(BasePlayer player)
            {

            }

            public void DoHitDamage(HitInfo info)
            {
                var attacker = info.InitiatorPlayer;
                if (attacker != null)
                {
                    reusableInstance.Init(Effect.Type.Generic, attacker.transform.position, Vector3.zero);
                    reusableInstance.pooledString = configuration.PlayerHitMarkerFx;
                    EffectNetwork.Send(reusableInstance, attacker.net.connection);
                }

                this.health -= info.damageTypes.Total();

                if (health <= 0)
                    Destroy(this);
            }

            public static void RemovePlayer(BasePlayer player)
            {
                if (player == null)
                    return;
            }

            void OnDestroy()
            {
                Planes.Remove(this.planeNetId);
                this.IsActive = false;

                foreach (var fx in this.config.ExplosionFxs)
                {
                    Effect.server.Run(fx, this.transform.position, Vector3.zero);
                }

                var entities = new List<BasePlayer>();
                Vis.Entities(this.transform.position, 7f, entities);
                BasePlayer driver = this.GetDriver();
                BaseNetworkable currentBaseNet;

                if (driver != null)
                {
                    PlaneController.RemovePlayer(driver);
                }

                foreach (var prop in this.props)
                {
                    if (prop == null)
                        continue;

                    var worldItem = prop.GetComponent<DroppedItem>();
                    if (worldItem != null)
                        worldItem.DestroyItem();

                    currentBaseNet = prop.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);

                    Destroy(prop.gameObject);
                }

                foreach (var prop in this.otherProps)
                {
                    if (prop == null)
                        continue;

                    var worldItem = prop.GetComponent<DroppedItem>();
                    if (worldItem != null)
                        worldItem.DestroyItem();

                    currentBaseNet = prop.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);

                    Destroy(prop.gameObject);
                }

                foreach (var prop in this.doors)
                {
                    if (prop == null)
                        continue;

                    currentBaseNet = prop.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);

                    Destroy(prop.gameObject);
                }

                foreach (var mnt in this.mounts)
                {
                    if (mnt == null)
                        continue;

                    currentBaseNet = mnt.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);

                    Destroy(mnt.gameObject);
                }

                foreach (var seat in this.seats)
                {
                    if (seat == null)
                        continue;

                    currentBaseNet = seat.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);

                    Destroy(seat.gameObject);
                }

                foreach (var whl in this.wheels)
                {
                    if (whl == null)
                        continue;

                    var worldItem = whl.GetComponent<DroppedItem>();
                    if (worldItem != null)
                        worldItem.DestroyItem();

                    currentBaseNet = whl.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);

                    Destroy(whl.gameObject);
                }

                if (this.controller != null)
                {
                    var worldItem = this.controller.GetComponent<DroppedItem>();
                    if (worldItem != null)
                        worldItem.DestroyItem();

                    currentBaseNet = this.controller.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);
                    Destroy(this.controller.gameObject);
                }

                var explosionDamage = this.config.ExplosionDamage;
                if (explosionDamage > 0)
                {
                    instance.NextTick(() =>
                    {
                        foreach (var entity in entities.ToList())
                        {
                            if (entity.IsDead())
                                continue;

                            entity.Hurt(explosionDamage, DamageType.Explosion, driver);
                        }
                    });
                }
            }
        }

        #region Plane Parts
        public class PlaneProp : MonoBehaviour
        {
            public PlaneController Plane { get; private set; }
            public static Dictionary<uint, PlaneProp> Props = new Dictionary<uint, PlaneProp>();
            private uint propNetId = 0;

            void Awake()
            {
                this.propNetId = this.GetComponent<BaseEntity>().net.ID;
                Props.Add(this.propNetId, this);
            }

            void OnDestroy()
            {
                Props.Remove(this.propNetId);
            }

            public void SetPlane(PlaneController plane)
            {
                this.Plane = plane;
            }
        }

        public class PlaneMount : MonoBehaviour
        {
            public static Dictionary<uint, PlaneMount> Mounts = new Dictionary<uint, PlaneMount>();
            private BaseMountable mount = null;
            private uint mountNetId = 0;
            private BaseMountable parentMount = null;
            private PlaneController plane;

            void Awake()
            {
                this.mount = this.GetComponent<BaseMountable>();
                this.mountNetId = this.mount.net.ID;
                Mounts.Add(this.mountNetId, this);
            }

            public void SetPlane(PlaneController _plane)
            {
                plane = _plane;
            }

            public void MountPlayer(BasePlayer player)
            {
                this.plane.AttemptMount(player);
            }

            void OnDestroy()
            {
                Mounts.Remove(this.mountNetId);

                if (this.mount != null)
                {
                    BaseNetworkable currentBaseNet = this.mount.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);
                    Destroy(this.mount.gameObject);
                }
            }
        }


        public class PlanePropeller : MonoBehaviour
        {
            private BaseEntity propeller;
            private PlaneController plane;
            private float lastRotation = 0;

            void Awake()
            {
                this.propeller = this.GetComponent<BaseEntity>();
            }

            public void SetPlane(PlaneController newPlane)
            {
                this.plane = newPlane;
            }

            void Update()
            {
                var currentTime = Time.realtimeSinceStartup;
                if (this.lastRotation + 0.025f > currentTime)
                    return;

                this.lastRotation = currentTime;
                if (this.plane != null && this.plane.IsEngineOn())
                {
                    this.propeller.transform.Rotate(new Vector3(0, 45, 0), Space.Self);
                }
            }
        }

        public class PlaneWheel : MonoBehaviour
        {
            public static Dictionary<uint, PlaneWheel> Wheels = new Dictionary<uint, PlaneWheel>();

            private uint netId;

            void Awake()
            {
                this.netId = this.GetComponent<BaseEntity>().net.ID;
                Wheels.Add(this.netId, this);
            }

            void OnDestroy()
            {
                Wheels.Remove(this.netId);
            }
        }

        public class PlaneEngine : MonoBehaviour
        {
            private FuelGenerator engine;
            private PlaneController plane;
            private float lastUpdateTime;

            public bool IsOn
            {
                get
                {
                    return engine.IsOn();
                }
            }

            void Awake()
            {
                this.engine = this.GetComponent<FuelGenerator>();
                this.engine.fuelPerSec = 0.0f;

                ItemContainer inv = this.engine.inventory;
                Item slot = inv.GetSlot(0);
                if (slot != null)
                    slot.amount = 500;
                else
                {
                    var item = ItemManager.itemList.FirstOrDefault(it => it.shortname.Contains("lowgradefuel"));
                    inv.AddItem(item, 1);
                    inv.SetLocked(true);
                }
            }

            public void SetPlane(PlaneController newPlane)
            {
                this.plane = newPlane;
            }

            void LateUpdate()
            {
                if (object.ReferenceEquals(this.plane, null))
                    return;

                if (!this.plane.IsActive)
                    return;

                var currentTime = Time.realtimeSinceStartup;
                if (lastUpdateTime + 0.5f > currentTime)
                    return;

                lastUpdateTime = currentTime;
                bool isOn = this.plane.IsSwitchOn();
                if (isOn && !engine.IsOn())
                    engine.SetGeneratorState(isOn);
                else if (!isOn && engine.IsOn())
                    engine.SetGeneratorState(isOn);
            }
        }

        public class PlaneBomb : MonoBehaviour
        {
            private BaseEntity entity;
            private BasePlayer owner;
            private List<DamageTypeEntry> damageTypes;
            private bool isActive = false;
            private float radius = 0;

            public bool InWater
            {
                get
                {
                    return TerrainMeta.WaterMap.GetHeight(this.transform.position) > this.transform.position.y;
                }
            }

            void Awake()
            {
                this.entity = this.GetComponent<BaseEntity>();
                this.isActive = true;
            }

            void Update()
            {
                if (!isActive)
                    return;

                if (this.InWater)
                {
                    isActive = false;
                    Destroy(this);
                }
            }

            public void Initialize(BasePlayer player, List<DamageTypeEntry> damageTypes, float radius)
            {
                owner = player;
                this.damageTypes = damageTypes;
                this.radius = radius;
            }

            private void OnCollisionEnter(Collision collision)
            {
                if (!isActive)
                    return;

                Destroy(this);
            }

            void OnDestroy()
            {
                Effect.server.Run("assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab", this.transform.position, Vector3.zero);
                DamageUtil.RadiusDamage(owner, this.entity, this.transform.position, 2, 5, damageTypes, 1075980544, true);

                if (this.entity != null)
                {
                    var currentBaseNet = this.entity.GetComponent<BaseNetworkable>();
                    if (!currentBaseNet.IsDestroyed)
                        currentBaseNet?.Kill(BaseNetworkable.DestroyMode.None);
                    Destroy(this.entity.gameObject);
                }
            }
        }
        #endregion

        #region Configuration
        public class Configuration
        {
            public float DistanceBeforeUpdatingBody { get; set; } = 50;
            public string PlayerHitMarkerFx { get; set; } = "assets/bundled/prefabs/fx/hit_notify.prefab";
            public bool DrawNames { get; set; } = false;
            public bool UseDistanceBasedUpdates { get; set; } = true;
            public bool AllowSamTarget { get; set; }
            public float SamUpdateFrequency { get; set; } = 1;
            public float SamScanRadius { get; set; } = 350f;
            public bool SamIgnoreAuthPlayers { get; set; } = false;

            public Dictionary<string, PlaneConfig> PlaneConfigs = new Dictionary<string, PlaneConfig>(StringComparer.OrdinalIgnoreCase);
        }

        public class PlaneConfig
        {
            public Vector3 TorqueScale { get; set; }
            public float EngineThrustMax { get; set; }
            public float MaxThrottle { get; set; }
            public float LiftModifier { get; set; }
            public float TakeOffLiftModifier { get; set; }
            public float StallForce { get; set; }
            public float StallVelocity { get; set; }
            public float LerpModifier { get; set; }
            public float Mass { get; set; }
            public float Drag { get; set; }
            public float AngularDrag { get; set; }
            public float MaxDepenetrationVelocity { get; set; }
            public Vector3 CenterOfMass { get; set; }
            public Vector3 InertiaTensor { get; set; }
            public float MaxAngularVelocity { get; set; }
            public float MaxHealth { get; set; }
            public float ServiceCeiling { get; set; }
            public string MainCannonAmmoPrefab { get; set; }
            public string MainCannonAmmoShortName { get; set; }
            public float MainCannonAmmoSpeed { get; set; }
            public float MainCannonFireRate { get; set; }
            public string MainCannonMuzzleEffect { get; set; }
            public BarrelConfiguration MainCannonBarrelConfig { get; set; }
            public ProjectileType MainCannonProjectileType { get; set; }
            public float MainCannonAimCone { get; set; }

            public List<DamageTypeEntry> MainCannonDamageTypes { get; set; } = new List<DamageTypeEntry>();
            public List<CustomBarrelConfiguration> MainBarrelCustomConfigs { get; set; }

            public float BombFireRate { get; set; }
            public int BombCount { get; set; }
            public float BombRadius { get; set; } = 4f;

            public string SecondaryAmmoPrefab { get; set; }
            public string SecondaryAmmoShortName { get; set; }
            public float SecondaryAmmoSpeed { get; set; }
            public float SecondaryFireRate { get; set; }
            public string SecondaryMuzzleEffect { get; set; }
            public float SecondaryAimCone { get; set; }
            public BarrelConfiguration SecondaryBarrelConfig { get; set; }
            public ProjectileType SecondaryProjectileType { get; set; }
            public List<DamageTypeEntry> SecondaryDamageTypes { get; set; } = new List<DamageTypeEntry>();
            public List<CustomBarrelConfiguration> SecondaryBarrelCustomConfigs { get; set; }
            public bool UnlimitedAmmo { get; set; } = false;
            //public float FuelPerSecond { get; set; } = 0.5f;
            //public bool RequireFuel { get; set; } = true;
            public float CollisionMagnitude { get; set; } = 40f;
            public bool EnableVTOL { get; set; }
            public float VTOLModifier { get; set; }
            public float ExplosionDamage { get; set; }
            public Dictionary<string, int> BuildCosts = new Dictionary<string, int>();
            public List<string> ExplosionFxs = new List<string>();
            public List<PropConfig> PropConfigs { get; set; } = new List<PropConfig>();
        }

        public struct PropConfig
        {
            public PlanePart PlanePart;
            public PropType PropType;
            public string PrefabPath;
            public int ItemId;
            public Vector3 Location;
            public Vector3 Rotation;
        }

        public struct CustomBarrelConfiguration
        {
            public float MuzzleForwardModifier;
            public float MuzzleRightModifier;
            public float MuzzleUpModifier;
            public float MuzzleFxForwardModifier;
            public float MuzzleFxRightModifier;
            public float MuzzleFxUpModifier;
            public string MuzzleFx;
            public bool IsValid;
        }

        public enum PropType
        {
            World,
            Entity
        }

        public enum PlanePart
        {
            Propeller,
            Engine,
            Wheel,
            Prop,
            OtherProp,
            Mount,
            Seat,
            OnSwitch,
            PropellerPart,
            Door,
            BuildingBlock
        }

        public enum BarrelConfiguration
        {
            DualFront,
            DualBottom,
            Bottom,
            DualSide,
            TieFighter,
            SupplyDrop,
            Custom
        }

        public enum ProjectileType
        {
            Bullet,
            ServerProjectile,
            BombProjectile
        }
        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                configuration = Config.ReadObject<Configuration>();
                if (configuration == null)
                    throw new Exception();
            }
            catch
            {
                Config.WriteObject(configuration, false, $"{Interface.Oxide.ConfigDirectory}/{Name}.jsonError");
                PrintError("The configuration file contains an error and has been replaced with a default config.");
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            configuration = new Configuration();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(configuration);
        }

        #endregion
    }
}
