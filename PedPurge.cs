using System;
using System.Collections.Generic;
using System.Media;
using GTA;
using GTA.Native;
using GTA.Math;
using LemonUI;
using LemonUI.Menus;
using System.Windows.Forms;
using System.IO;

public class PedPurge : Script
{
    private bool isPurgeActive = false;
    private ObjectPool menuPool;
    private NativeMenu mainMenu;
    private Random random = new Random();
    private RelationshipGroup aggressiveGroup;
    private SoundPlayer player;

    private string configPath = @"scripts\PedPurge\Config\PedPurge.ini";
    private Keys menuKey = Keys.F10;
    private bool enableAudio = true;
    private Weather purgeWeather = Weather.Smog;
    private int purgeHour = 23;
    private int minPurgeWeaponAmmo = 50;
    private int maxPurgeWeaponAmmo = 150;
    private bool enableChaoticTraffic = true;
    private float pedTargetSearchRadius = 50.0f;
    private bool disableEmergencyServices = true;
    private bool allowNPCsToLeaveVehicles = true;

    public PedPurge()
    {
        LoadConfig();

        menuPool = new ObjectPool();
        mainMenu = new NativeMenu("PedPurge Menu", "Options");
        menuPool.Add(mainMenu);

        var startPurgeItem = new NativeItem("Start Purge");
        startPurgeItem.Activated += (sender, e) => StartPurge();
        mainMenu.Add(startPurgeItem);

        var stopPurgeItem = new NativeItem("Stop Purge");
        stopPurgeItem.Activated += (sender, e) => StopPurge();
        mainMenu.Add(stopPurgeItem);

        var toggleAudioItem = new NativeCheckboxItem("Enable Audio", enableAudio);
        toggleAudioItem.CheckboxChanged += (sender, e) =>
        {
            enableAudio = toggleAudioItem.Checked;
            SaveConfig();
        };
        mainMenu.Add(toggleAudioItem);

        KeyDown += (sender, e) =>
        {
            if (e.KeyCode == menuKey)
            {
                mainMenu.Visible = !mainMenu.Visible;
            }
        };

        Tick += OnTick;
        aggressiveGroup = World.AddRelationshipGroup("AggressiveNPCs");
    }

    private void OnTick(object sender, EventArgs e)
    {
        menuPool.Process();

        if (isPurgeActive)
        {
            MakePedsHostile();
            if (enableChaoticTraffic)
                MakeTrafficChaotic();
        }
    }

    private void StartPurge()
    {
        if (isPurgeActive)
        {
            GTA.UI.Notification.Show("Purge is already active.");
            return;
        }

        isPurgeActive = true;
        GTA.UI.Notification.Show("The Purge has begun!");

        SetPurgeAtmosphere();
        if (enableAudio)
        {
            PlayPurgeAudio();
        }

        Game.Player.Character.RelationshipGroup = aggressiveGroup;
        Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, (int)Relationship.Hate, aggressiveGroup, aggressiveGroup);
        Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, (int)Relationship.Hate, aggressiveGroup, Game.Player.Character.RelationshipGroup);

        if (disableEmergencyServices)
        {
            ToggleEmergencyServices(false);
        }
    }

    private void StopPurge()
    {
        if (!isPurgeActive)
        {
            GTA.UI.Notification.Show("Purge is not active.");
            return;
        }

        isPurgeActive = false;
        GTA.UI.Notification.Show("The Purge has ended.");

        player?.Stop();
        player?.Dispose();
        player = null;

        ToggleEmergencyServices(true);
    }

    private void ToggleEmergencyServices(bool enable)
    {
        Function.Call(Hash.ENABLE_DISPATCH_SERVICE, 1, enable); // Police
        Function.Call(Hash.ENABLE_DISPATCH_SERVICE, 2, enable); // Fire Department
        Function.Call(Hash.ENABLE_DISPATCH_SERVICE, 3, enable); // EMS
    }

    private void MakePedsHostile()
    {
        foreach (var ped in World.GetAllPeds())
        {
            if (ped.IsHuman && !ped.IsPlayer && ped.IsAlive)
            {
                ped.RelationshipGroup = aggressiveGroup;

                if (!ped.Weapons.HasWeapon(WeaponHash.Pistol))
                {
                    ped.Weapons.Give(GetRandomWeapon(), random.Next(minPurgeWeaponAmmo, maxPurgeWeaponAmmo), true, true);
                }

                // Find the closest valid target manually
                Ped target = null;
                float closestDistance = float.MaxValue;

                foreach (var potentialTarget in World.GetAllPeds())
                {
                    if (potentialTarget != ped && potentialTarget.IsHuman && potentialTarget.IsAlive)
                    {
                        float distance = ped.Position.DistanceTo(potentialTarget.Position);
                        if (distance < closestDistance && distance <= pedTargetSearchRadius)
                        {
                            closestDistance = distance;
                            target = potentialTarget;
                        }
                    }
                }

                if (target != null)
                {
                    ped.Task.Combat(target);
                }

                ped.KeepTaskWhenMarkedAsNoLongerNeeded = true;
                ped.BlockPermanentEvents = true;
            }
        }
    }


    private void MakeTrafficChaotic()
    {
        foreach (var vehicle in World.GetAllVehicles())
        {
            if (vehicle.Driver != null && vehicle.Driver.IsHuman && !vehicle.Driver.IsPlayer && vehicle.IsAlive)
            {
                if (allowNPCsToLeaveVehicles && random.NextDouble() < 0.5)
                {
                    vehicle.Driver.Task.LeaveVehicle();
                }
                else
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, vehicle.Driver.Handle, vehicle.Handle, random.Next(50, 150), (int)VehicleDrivingFlags.AllowGoingWrongWay);
                }
            }
        }
    }

    private WeaponHash GetRandomWeapon()
    {
        WeaponHash[] weapons = { WeaponHash.Knife, WeaponHash.Pistol, WeaponHash.Bat, WeaponHash.MicroSMG, WeaponHash.AssaultRifle };
        return weapons[random.Next(weapons.Length)];
    }

    private void SetPurgeAtmosphere()
    {
        World.Weather = purgeWeather;
        Function.Call(Hash.SET_CLOCK_TIME, purgeHour, 0, 0);
        Game.Player.WantedLevel = 0;
        GTA.UI.Notification.Show($"Purge conditions set: {purgeWeather} weather at {purgeHour}:00.");
    }

    private void PlayPurgeAudio()
    {
        string audioFilePath = @"scripts\PedPurge\audio\ThePurge.wav";

        if (File.Exists(audioFilePath))
        {
            player = new SoundPlayer(audioFilePath);
            player.Play();
        }
        else
        {
            GTA.UI.Notification.Show("Purge audio file not found.");
        }
    }

    private void LoadConfig()
    {
        if (File.Exists(configPath))
        {
            foreach (var line in File.ReadLines(configPath))
            {
                if (line.StartsWith("MenuKey=")) menuKey = (Keys)Enum.Parse(typeof(Keys), line.Split('=')[1]);
                if (line.StartsWith("EnableAudio=")) enableAudio = bool.Parse(line.Split('=')[1]);
                if (line.StartsWith("PurgeWeather=")) purgeWeather = (Weather)Enum.Parse(typeof(Weather), line.Split('=')[1]);
                if (line.StartsWith("PurgeHour=")) purgeHour = int.Parse(line.Split('=')[1]);
                if (line.StartsWith("MinPurgeWeaponAmmo=")) minPurgeWeaponAmmo = int.Parse(line.Split('=')[1]);
                if (line.StartsWith("MaxPurgeWeaponAmmo=")) maxPurgeWeaponAmmo = int.Parse(line.Split('=')[1]);
                if (line.StartsWith("EnableChaoticTraffic=")) enableChaoticTraffic = bool.Parse(line.Split('=')[1]);
                if (line.StartsWith("PedTargetSearchRadius=")) pedTargetSearchRadius = float.Parse(line.Split('=')[1]);
                if (line.StartsWith("DisableEmergencyServices=")) disableEmergencyServices = bool.Parse(line.Split('=')[1]);
                if (line.StartsWith("AllowNPCsToLeaveVehicles=")) allowNPCsToLeaveVehicles = bool.Parse(line.Split('=')[1]);
            }
        }
    }

    private void SaveConfig()
    {
        File.WriteAllLines(configPath, new[]
        {
            $"MenuKey={menuKey}",
            $"EnableAudio={enableAudio}",
            $"PurgeWeather={purgeWeather}",
            $"PurgeHour={purgeHour}",
            $"MinPurgeWeaponAmmo={minPurgeWeaponAmmo}",
            $"MaxPurgeWeaponAmmo={maxPurgeWeaponAmmo}",
            $"EnableChaoticTraffic={enableChaoticTraffic}",
            $"PedTargetSearchRadius={pedTargetSearchRadius}",
            $"DisableEmergencyServices={disableEmergencyServices}",
            $"AllowNPCsToLeaveVehicles={allowNPCsToLeaveVehicles}"
        });
    }
}
