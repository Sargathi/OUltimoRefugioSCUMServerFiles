using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

namespace HomeSystem
{
    #region Data Models

    public class HomeLocation
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class PlayerHomeData
    {
        // Dictionary to store multiple homes: Name -> Coordinates
        public Dictionary<string, HomeLocation> Homes { get; set; } = new Dictionary<string, HomeLocation>();
    }

    #endregion

    #region Configuration

    public class HomeConfig
    {
        public int DefaultMaxHomes { get; set; } = 0;
        public int VipPrataMaxHomes { get; set; } = 1;
        public int VipOuroMaxHomes { get; set; } = 2;
        public int TeleportDelaySeconds { get; set; } = 30;
        public int TeleportCooldownMinutes { get; set; } = 40; 
        public string VipPrataPermission { get; set; } = "sethome.vip_prata";
        public string VipOuroPermission { get; set; } = "sethome.vip_ouro";
        public string VipPrataFilePath { get; set; } = @"C:\scumserver\vip_prata.txt";
        public string VipOuroFilePath { get; set; } = @"C:\scumserver\vip_ouro.txt";
    }

    #endregion

    [Info("Home System", "Standelone", "1.2.0")]
    [Description("Allows players to set, teleport to, and delete homes with configurable limits and cooldowns.")]
    public class HomePlugin : OxygenPlugin
    {
        private HomeConfig _cfg;
        private Dictionary<string, PlayerHomeData> _database;
        
        private Dictionary<string, DateTime> _cooldowns = new Dictionary<string, DateTime>();

        #region Initialization

        public override void OnLoad()
        {
            _cfg = LoadConfig<HomeConfig>() ?? new HomeConfig();
            _cfg.VipPrataFilePath = Environment.GetEnvironmentVariable("VIP_PRATA_FILE_PATH") ?? _cfg.VipPrataFilePath;
            _cfg.VipOuroFilePath = Environment.GetEnvironmentVariable("VIP_OURO_FILE_PATH") ?? _cfg.VipOuroFilePath;
            SaveConfig(_cfg);

            _database = LoadData<Dictionary<string, PlayerHomeData>>("HomeData") 
                        ?? new Dictionary<string, PlayerHomeData>();
            
            Console.WriteLine("[HomeSystem] Plugin initialized successfully.");
        }

        public override void OnUnload()
        {
            SaveData("HomeData", _database);
        }

        public override void OnPlayerDisconnected(PlayerBase player)
        {
            if (player == null || string.IsNullOrEmpty(player.SteamId)) return;
            _cooldowns.Remove(player.SteamId);
        }

        #endregion

        #region Commands

        [Command("sethome")]
        private void SetHomeCommand(PlayerBase player, string[] args)
        {
            if (args.Length < 1)
            {
                player.Reply("Usage: /sethome <name>", Color.Orange);
                return;
            }

            string homeName = args[0].ToLower();
            string steamId = player.SteamId;

            if (!_database.ContainsKey(steamId))
                _database[steamId] = new PlayerHomeData();

            int limit = GetPlayerHomeLimit(player);
            
            if (!_database[steamId].Homes.ContainsKey(homeName) && _database[steamId].Homes.Count >= limit)
            {
                player.Reply($"Limit reached! You can only have {limit} homes.", Color.Red);
                return;
            }

            _database[steamId].Homes[homeName] = new HomeLocation 
            { 
                X = player.Location.X, 
                Y = player.Location.Y, 
                Z = player.Location.Z 
            };

            player.Reply($"Home '{homeName}' has been successfully set!", Color.Green);
            SaveData("HomeData", _database);
        }

        [Command("home")]
        private async Task HomeTeleportCommand(PlayerBase player, string[] args)
        {
            if (args.Length < 1)
            {
                player.Reply("Usage: /home <name>", Color.Orange);
                return;
            }

            string steamId = player.SteamId;

            if (_cooldowns.TryGetValue(steamId, out DateTime nextAllowedTime))
            {
                if (DateTime.UtcNow < nextAllowedTime)
                {
                    TimeSpan remaining = nextAllowedTime - DateTime.UtcNow;
                    player.Reply($"Cooldown active! You can teleport again in {(int)remaining.TotalMinutes}m {remaining.Seconds}s.", Color.Orange);
                    return;
                }
            }

            string homeName = args[0].ToLower();

            if (!_database.ContainsKey(steamId) || !_database[steamId].Homes.ContainsKey(homeName))
            {
                player.Reply($"Home '{homeName}' not found!", Color.Red);
                return;
            }

            var target = _database[steamId].Homes[homeName];
            player.Reply($"Teleporting to '{homeName}' in {_cfg.TeleportDelaySeconds} seconds. Do not move!", Color.Yellow);

            bool success = await WaitForTeleport(player);

            if (success)
            {
                player.ProcessCommand($"Teleport {target.X:F0} {target.Y:F0} {target.Z:F0}");
                player.Reply($"You have arrived at home: {homeName}", Color.Green);

                if (_cfg.TeleportCooldownMinutes > 0)
                {
                    _cooldowns[steamId] = DateTime.UtcNow.AddMinutes(_cfg.TeleportCooldownMinutes);
                }
            }
        }

        [Command("delhome")]
        private void DeleteHomeCommand(PlayerBase player, string[] args)
        {
            if (args.Length < 1)
            {
                player.Reply("Usage: /delhome <name>", Color.Orange);
                return;
            }

            string homeName = args[0].ToLower();
            string steamId = player.SteamId;

            if (_database.ContainsKey(steamId) && _database[steamId].Homes.Remove(homeName))
            {
                player.Reply($"Home '{homeName}' has been deleted.", Color.Green);
                SaveData("HomeData", _database);
            }
            else
            {
                player.Reply($"Home '{homeName}' not found.", Color.Red);
            }
        }

        [Command("homes")]
        private void ListHomesCommand(PlayerBase player, string[] args)
        {
            string steamId = player.SteamId;
            if (!_database.ContainsKey(steamId) || _database[steamId].Homes.Count == 0)
            {
                player.Reply("You don't have any homes set.", Color.Yellow);
                return;
            }

            string homeList = "\n=== YOUR HOMES ===\n";
            foreach (var home in _database[steamId].Homes.Keys)
            {
                homeList += $"• {home}\n";
            }
            player.Reply(homeList, Color.Blue);
        }

        #endregion

        #region Helper Methods

        private async Task<bool> WaitForTeleport(PlayerBase player)
        {
            var startPos = player.Location;
            int totalCheckIntervals = _cfg.TeleportDelaySeconds * 2;

            for (int i = 0; i < totalCheckIntervals; i++)
            {
                await Task.Delay(500);

                double distance = Math.Sqrt(Math.Pow(player.Location.X - startPos.X, 2) + Math.Pow(player.Location.Y - startPos.Y, 2));
                
                if (distance > 50.0)
                {
                    player.Reply("Teleport cancelled! You moved.", Color.Red);
                    return false;
                }
            }

            return true;
        }

        private int GetPlayerHomeLimit(PlayerBase player)
        {
            string steamId = player.SteamId;
            int limit = _cfg.DefaultMaxHomes;

            // Fonte principal: arquivos vip_*.txt (mesma origem dos demais benefícios VIP).
            bool isVipOuro = IsInFile(_cfg.VipOuroFilePath, steamId);
            bool isVipPrata = !isVipOuro && IsInFile(_cfg.VipPrataFilePath, steamId);

            if (isVipOuro) return _cfg.VipOuroMaxHomes;
            if (isVipPrata) return _cfg.VipPrataMaxHomes;

            // Fallback: permissões OxyMod, para manter compatibilidade.
            if (player.HasPermission(_cfg.VipOuroPermission)) return _cfg.VipOuroMaxHomes;
            if (player.HasPermission(_cfg.VipPrataPermission)) return _cfg.VipPrataMaxHomes;

            return limit;
        }

        private bool IsInFile(string filePath, string steamId)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                return File.ReadAllLines(filePath)
                           .Any(l => l.Trim().Equals(steamId, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}