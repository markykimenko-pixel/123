using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

namespace ZombieWar
{
    public sealed class ZombieWarScript : Script
    {
        private readonly ZombieDirector zombies;
        private readonly BackupDirector backup;
        private readonly WarSettings settings;
        private bool menuOpen;
        private bool noZombies;
        private bool f7, f8, f9, f10, num1, num2, num3;
        private DateTime nextAmbientUpdate = DateTime.MinValue;

        public ZombieWarScript()
        {
            settings = WarSettings.Load();
            zombies = new ZombieDirector(settings);
            backup = new BackupDirector();
            Tick += OnTick;
            Aborted += OnAborted;
            Interval = 100;
            UI.Notify("~b~ZombieWar~s~ rebuilt loaded. F10 menu | F7 spawn | F8 horde");
        }

        private void OnTick(object sender, EventArgs e)
        {
            bool k7 = Game.IsKeyPressed(Keys.F7);
            bool k8 = Game.IsKeyPressed(Keys.F8);
            bool k9 = Game.IsKeyPressed(Keys.F9);
            bool k10 = Game.IsKeyPressed(Keys.F10);
            bool n1 = Game.IsKeyPressed(Keys.NumPad1);
            bool n2 = Game.IsKeyPressed(Keys.NumPad2);
            bool n3 = Game.IsKeyPressed(Keys.NumPad3);

            if (k10 && !f10) { menuOpen = !menuOpen; ShowMenu(); }
            if (k7 && !f7) SpawnOne();
            if (k8 && !f8) { noZombies = !noZombies; UI.Notify(noZombies ? "~r~Zombie spawning disabled" : "~g~Zombie spawning enabled"); }
            if (k9 && !f9) { settings.Infection = !settings.Infection; settings.Save(); UI.Notify("Infection: " + (settings.Infection ? "ON" : "OFF")); }
            if (n1 && !num1) backup.Call("LOCAL");
            if (n2 && !num2) backup.Call("SWAT");
            if (n3 && !num3) backup.Call("MILITARY");

            f7 = k7; f8 = k8; f9 = k9; f10 = k10; num1 = n1; num2 = n2; num3 = n3;

            if (menuOpen) DrawMenuHelp();
            if (!noZombies && DateTime.UtcNow >= nextAmbientUpdate)
            {
                zombies.SpawnAmbient(settings.AmbientBatch);
                nextAmbientUpdate = DateTime.UtcNow.AddSeconds(settings.SpawnIntervalSeconds);
            }
            zombies.Update();
            backup.Update();
        }

        private void SpawnOne()
        {
            ZombieKind kind = (ZombieKind)((zombies.TotalSpawned % 5));
            if (zombies.Spawn(kind)) UI.Notify("~g~Spawned: " + kind);
            else UI.Notify("~r~Cannot spawn zombie here (limit/model unavailable)");
        }

        private void ShowMenu()
        {
            if (menuOpen) UI.Notify("~b~ZOMBIEWAR MENU~s~\nF7 Spawn special zombie\nF8 Toggle ambient horde\nF9 Toggle infection\nNum1 Patrol | Num2 SWAT | Num3 Military\nF10 Close");
            else UI.Notify("~b~ZombieWar menu closed");
        }

        private void DrawMenuHelp()
        {
            UI.ShowSubtitle("~b~ZOMBIEWAR~s~  F7: zombie  F8: horde " + (noZombies ? "OFF" : "ON") + "  F9: infection " + (settings.Infection ? "ON" : "OFF") + "\nNum1: patrol  Num2: SWAT  Num3: military  F10: close", 110);
        }

        private void OnAborted(object sender, EventArgs e)
        {
            zombies.Cleanup();
            backup.Cleanup();
            settings.Save();
        }
    }

    internal enum ZombieKind { Normal, Animal, Firefighter, Explosive, Riot }

    internal sealed class ZombieAgent
    {
        public Ped Ped;
        public ZombieKind Kind;
        public int BaseHealth;
        public bool Detonated;
    }

    internal sealed class ZombieDirector
    {
        private readonly WarSettings settings;
        private readonly List<ZombieAgent> active = new List<ZombieAgent>();
        private readonly Random random = new Random();
        private readonly int relationshipGroup;
        private readonly int playerGroup;
        public int TotalSpawned { get; private set; }
        private const int MaxZombies = 42;
        private static readonly string[] HumanModels = { "a_m_m_skidrow_01", "a_m_m_hillbilly_01", "a_m_o_acult_01", "a_f_y_juggalo_01", "u_m_y_zombie_01" };

        public ZombieDirector(WarSettings value)
        {
            settings = value;
            relationshipGroup = Function.Call<int>(Hash.GET_HASH_KEY, "ZW_ZOMBIES");
            playerGroup = Function.Call<int>(Hash.GET_HASH_KEY, "PLAYER");
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, relationshipGroup, playerGroup);
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, playerGroup, relationshipGroup);
        }

        public bool Spawn(ZombieKind kind)
        {
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists() || active.Count(a => a.Ped != null && a.Ped.Exists() && a.Ped.IsAlive) >= MaxZombies) return false;
            Vector3 pos = player.Position + player.ForwardVector * random.Next(12, 24) + new Vector3(random.Next(-10, 11), random.Next(-10, 11), 0f);
            string modelName = kind == ZombieKind.Animal ? "a_c_rottweiler" : HumanModels[random.Next(HumanModels.Length)];
            Model model = new Model(modelName);
            if (!model.IsInCdImage || !model.IsValid) return false;
            model.Request(1000);
            if (!model.IsLoaded) return false;
            Ped ped = World.CreatePed(model, pos);
            model.MarkAsNoLongerNeeded();
            if (ped == null || !ped.Exists()) return false;
            ped.IsPersistent = true;
            ped.BlockPermanentEvents = true;
            Function.Call(Hash.SET_PED_RELATIONSHIP_GROUP_HASH, ped.Handle, relationshipGroup);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped.Handle, 0, false);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 5, true);
            Function.Call(Hash.SET_PED_AS_ENEMY, ped.Handle, true);
            Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, true);
            int health = HealthFor(kind);
            Function.Call(Hash.SET_PED_MAX_HEALTH, ped.Handle, health);
            ped.Health = health;
            float speed = kind == ZombieKind.Animal ? 1.45f : settings.ZombieSpeed;
            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, ped.Handle, speed);
            if (kind == ZombieKind.Riot) Function.Call(Hash.SET_PED_ARMOUR, ped.Handle, 100);
            if (kind == ZombieKind.Animal) Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped.Handle, false);
            ped.Task.FightAgainst(player);
            active.Add(new ZombieAgent { Ped = ped, Kind = kind, BaseHealth = health });
            TotalSpawned++;
            return true;
        }

        public void SpawnAmbient(int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                ZombieKind kind = ZombieKind.Normal;
                int roll = random.Next(100);
                if (roll < 5) kind = ZombieKind.Animal;
                else if (roll < 10) kind = ZombieKind.Firefighter;
                else if (roll < 14) kind = ZombieKind.Explosive;
                else if (roll < 19) kind = ZombieKind.Riot;
                Spawn(kind);
            }
        }

        public void Update()
        {
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists()) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ZombieAgent z = active[i];
                if (z.Ped == null || !z.Ped.Exists() || !z.Ped.IsAlive)
                {
                    active.RemoveAt(i);
                    continue;
                }
                float distance = z.Ped.Position.DistanceTo(player.Position);
                if (z.Kind == ZombieKind.Explosive && !z.Detonated && distance < 3.2f)
                {
                    z.Detonated = true;
                    Function.Call(Hash.ADD_EXPLOSION, z.Ped.Position.X, z.Ped.Position.Y, z.Ped.Position.Z, 2, 3.0f, true, false, 1.0f);
                    z.Ped.Health = 0;
                }
                if (z.Kind == ZombieKind.Firefighter && distance < 18f && random.Next(100) < 2)
                {
                    Function.Call(Hash.ADD_EXPLOSION, player.Position.X + random.Next(-3, 4), player.Position.Y + random.Next(-3, 4), player.Position.Z, 3, 1.0f, true, false, 0.5f);
                }
                if (distance > 180f) { z.Ped.MarkAsNoLongerNeeded(); active.RemoveAt(i); }
                else if (distance > 55f && !z.Ped.IsInCombat) z.Ped.Task.FightAgainst(player);
            }
        }

        private static int HealthFor(ZombieKind kind)
        {
            switch (kind)
            {
                case ZombieKind.Animal: return 180;
                case ZombieKind.Firefighter: return 220;
                case ZombieKind.Explosive: return 180;
                case ZombieKind.Riot: return 1000;
                default: return 200;
            }
        }

        public void Cleanup()
        {
            foreach (ZombieAgent z in active) if (z.Ped != null && z.Ped.Exists()) z.Ped.MarkAsNoLongerNeeded();
            active.Clear();
        }
    }

    internal sealed class BackupDirector
    {
        private readonly List<Ped> units = new List<Ped>();
        private readonly Random random = new Random();

        public void Call(string type)
        {
            Ped player = Game.Player.Character;
            if (player == null || !player.Exists()) return;
            string model = type == "MILITARY" ? "s_m_y_blackops_01" : type == "SWAT" ? "s_m_y_swat_01" : "s_m_y_cop_01";
            string weapon = type == "MILITARY" ? "WEAPON_CARBINERIFLE" : type == "SWAT" ? "WEAPON_SMG" : "WEAPON_PISTOL";
            int count = type == "MILITARY" ? 4 : type == "SWAT" ? 4 : 2;
            for (int i = 0; i < count; i++)
            {
                Model m = new Model(model);
                if (!m.IsInCdImage || !m.IsValid) continue;
                m.Request(1000);
                if (!m.IsLoaded) continue;
                Vector3 p = player.Position + new Vector3(random.Next(-8, 9), random.Next(-8, 9), 0f);
                Ped ped = World.CreatePed(m, p);
                m.MarkAsNoLongerNeeded();
                if (ped == null || !ped.Exists()) continue;
                ped.IsPersistent = true;
                ped.BlockPermanentEvents = true;
                Function.Call(Hash.GIVE_WEAPON_TO_PED, ped.Handle, Function.Call<uint>(Hash.GET_HASH_KEY, weapon), 240, false, true);
                Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, ped.Handle, Function.Call<int>(Hash.GET_PLAYER_GROUP, Game.Player.Handle));
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped.Handle, 46, true);
                ped.Task.FollowToOffsetFromEntity(player, new Vector3((i % 2 == 0 ? -2f : 2f), -2f - i, 0f), 2f, -1, 2f, true);
                units.Add(ped);
            }
            UI.Notify("~b~Backup dispatched: " + type);
        }

        public void Update()
        {
            units.RemoveAll(p => p == null || !p.Exists() || !p.IsAlive);
        }

        public void Cleanup()
        {
            foreach (Ped p in units) if (p != null && p.Exists()) p.MarkAsNoLongerNeeded();
            units.Clear();
        }
    }

    internal sealed class WarSettings
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "ZombieWar.ini");
        public bool Infection = false;
        public float ZombieSpeed = 1.0f;
        public int AmbientBatch = 2;
        public int SpawnIntervalSeconds = 12;

        public static WarSettings Load()
        {
            WarSettings s = new WarSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    string[] pair = line.Split(new[] { '=' }, 2);
                    if (pair.Length != 2) continue;
                    string key = pair[0].Trim().ToLowerInvariant(), value = pair[1].Trim();
                    bool b; float f; int n;
                    if (key == "infection" && bool.TryParse(value, out b)) s.Infection = b;
                    if (key == "zombiespeed" && float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) s.ZombieSpeed = Math.Max(0.5f, Math.Min(2f, f));
                    if (key == "ambientbatch" && int.TryParse(value, out n)) s.AmbientBatch = Math.Max(0, Math.Min(5, n));
                    if (key == "spawnintervalseconds" && int.TryParse(value, out n)) s.SpawnIntervalSeconds = Math.Max(3, Math.Min(60, n));
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, "; ZombieWar rebuilt settings\nInfection=" + Infection + "\nZombieSpeed=" + ZombieSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\nAmbientBatch=" + AmbientBatch + "\nSpawnIntervalSeconds=" + SpawnIntervalSeconds + "\n");
            }
            catch { }
        }
    }
}
