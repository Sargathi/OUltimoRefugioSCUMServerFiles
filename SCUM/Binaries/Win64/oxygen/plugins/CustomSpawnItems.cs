using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

// ============================================================
//  Custom Respawn Items  v4.0.7
//  O Último Refúgio — SCUM Server
//
//  Tiers de respawn (verificados via arquivo de texto, sem cache):
//
//  SEM WHITELIST — vanilla puro
//    Sem alteração nenhuma. Roupa de prisioneiro + cartão starter.
//
//  COM WHITELIST (kit básico)
//    Roupa amarela bamboo + itens básicos de sobrevivência.
//
//  VIP PRATA — lido de vip_prata.txt
//    Equipamento tático + arco, flechas, taco, MRE.
//
//  VIP OURO — lido de vip_ouro.txt
//    Equipamento tático + aljava + arco recurvo, etc. (sem itens cujo id contenha "backpack".)
//
//  Prioridade: Ouro > Prata > Whitelist > Vanilla
// ============================================================

namespace SpawnSystem
{
    #region Configuration

    public class SpawnSettings
    {
        public List<string> Equipment { get; set; } = new List<string>();
        public List<string> Inventory { get; set; } = new List<string>();
    }

    public class RespawnConfig
    {
        public bool   Enabled              { get; set; } = true;
        public string WhitelistFilePath    { get; set; } = @"C:\scumserver\whitelist_scum.txt";
        public string VipPrataFilePath     { get; set; } = @"C:\scumserver\vip_prata.txt";
        public string VipOuroFilePath      { get; set; } = @"C:\scumserver\vip_ouro.txt";
        public string Message              { get; set; } = "Seus itens VIP foram entregues. Bom jogo!";
        public string WlKitMessage         { get; set; } = "Bem-vindo! Seu kit de Whitelist foi entregue. Bom jogo!";
        public int    KitCooldownMinutes   { get; set; } = 40;
        public string MaleUnderwearItemId  { get; set; } = "Boxer_Briefs_01";
        public string FemaleBraItemId      { get; set; } = "F_Undershirt_Bra_01";
        public string FemaleUnderwearItemId { get; set; } = "Underpants_01";
        public string SocksItemId          { get; set; } = "Sock_01";
        public int RespawnSafetyDelayMs { get; set; } = 1200;
        public int EquipIntervalMs { get; set; } = 70;
        public int GiveItemIntervalMs { get; set; } = 60;
        /// <summary>Pausa após limpar o inventário, antes do paraquedas (o ator do jogador pode ainda não estar pronto para spawn de itens).</summary>
        public int PostInventoryClearDelayMs { get; set; } = 1000;
        /// <summary>Pausa após EquipParachute antes da roupa base.</summary>
        public int PostParachuteEquipDelayMs { get; set; } = 450;
        /// <summary>Pausa extra após roupa íntima/meias, antes do kit VIP/WL.</summary>
        public int PreKitEquipmentDelayMs { get; set; } = 800;
        /// <summary>Tentativas por peça em EquipItem/GiveItem (Spawn_Actor NULL costuma ser timing, não id).</summary>
        public int ItemSpawnAttemptCount { get; set; } = 3;
        /// <summary>Espera entre tentativas de equipar/entregar o mesmo id.</summary>
        public int ItemSpawnAttemptDelayMs { get; set; } = 400;

        public SpawnSettings WhitelistSet { get; set; } = new SpawnSettings
        {
            Equipment = new List<string>
            {
                "Bamboo_Hat_02",
                "Beijing_Shoes_04",
                "Tang_pants_04",
                "Tang_shirt_04"
            },
            Inventory = new List<string>
            {
                "Apple_2",                   // 1 maçã
                "Date",                      // 1 tâmara
                "WaterBottle",               // 1 garrafa d'água
                "Rag",                       // 6 trapos
                "Rag",
                "Rag",
                "Rag",
                "Rag",
                "Rag",
                "Credit_Card_Starter",       // cartão starter
                "Improvised_Wooden_Spear"    // lança de madeira
            }
        };

        public SpawnSettings VipPrataSet { get; set; } = new SpawnSettings
        {
            Equipment = new List<string>
            {
                "Military_Boonie_Hat_07",
                "Camouflage_Jacket_01",
                "Hiking_Boots_03",
                "Open_Finger_Gloves_01",
                "Short_Trousers_01_05",
                "Improvised_Quiver_02_Rags"
            },
            Inventory = new List<string>
            {
                "Credit_Card_Classic",
                "Improvised_Bow",
                "Wooden_ArrowStoneTip",
                "Wooden_ArrowStoneTip",
                "Wooden_ArrowStoneTip",
                "Wooden_ArrowStoneTip",
                "Wooden_ArrowStoneTip",
                "Wooden_ArrowStoneTip",
                "2H_Baseball_Bat_With_Wire",
                "MRE_Stew",
                "Emergency_Bandage_Big"
            }
        };

        public SpawnSettings VipOuroSet { get; set; } = new SpawnSettings
        {
            Equipment = new List<string>
            {
                "Military_Boonie_Hat_07",
                "Camouflage_Jacket_01",
                "Hiking_Boots_03",
                "Open_Finger_Gloves_01",
                "Short_Trousers_01_05",
                "Improvised_Quiver_02_Rags"
            },
            Inventory = new List<string>
            {
                "Credit_Card_Classic",
                "Recurve_Bow",
                "Wooden_ArrowMetalTip",
                "Wooden_ArrowMetalTip",
                "Wooden_ArrowMetalTip",
                "Wooden_ArrowMetalTip",
                "Wooden_ArrowMetalTip",
                "Wooden_ArrowMetalTip",
                "2H_Baseball_Bat_With_Wire",
                "MRE_Stew",
                "Emergency_Bandage_Big"
            }
        };
    }

    #endregion

    [Info("Custom Respawn Items", "OUltimoRefugio", "4.0.7")]
    [Description("Tiers de respawn via arquivos de texto: vanilla (sem WL), basico (WL), prata, ouro.")]
    public class CustomRespawnPlugin : OxygenPlugin
    {
        private RespawnConfig _cfg;
        private Dictionary<string, long> _kitCooldowns;

        public override void OnLoad()
        {
            _cfg = LoadConfig<RespawnConfig>() ?? new RespawnConfig();
            SanitizeVipOuroNoBackpackItems(_cfg);
            SaveConfig(_cfg);

            _kitCooldowns = LoadData<Dictionary<string, long>>("SpawnKitCooldowns")
                            ?? new Dictionary<string, long>();
            Console.WriteLine($"[SpawnSystem] v4.0.7 carregado. Cooldown: {_cfg.KitCooldownMinutes} min.");
        }

        public override void OnUnload()
        {
            SaveData("SpawnKitCooldowns", _kitCooldowns);
            Console.WriteLine("[SpawnSystem] Dados de cooldown salvos.");
        }

        public override void OnPlayerRespawned(PlayerBase player)
        {
            // Evita async void no hook (exceções não observadas podem derrubar o Oxygen).
            _ = HandlePlayerRespawnedAsync(player);
        }

        private async Task HandlePlayerRespawnedAsync(PlayerBase player)
        {
            try
            {
                if (player == null || string.IsNullOrEmpty(player.SteamId)) return;
                if (!_cfg.Enabled) return;

                string steamId = player.SteamId;
                Console.WriteLine($"[SpawnSystem] Respawn: {player.Name} ({steamId})");

                // ── Determina o tier via arquivos de texto (sem cache do OxyMod) ─
                bool isVipOuro  = IsInFile(_cfg.VipOuroFilePath,  steamId);
                bool isVipPrata = !isVipOuro && IsInFile(_cfg.VipPrataFilePath, steamId);
                bool hasWl      = !isVipOuro && !isVipPrata && IsInFile(_cfg.WhitelistFilePath, steamId);

                string tier = isVipOuro ? "ouro" : isVipPrata ? "prata" : hasWl ? "whitelist" : "vanilla";
                Console.WriteLine($"[SpawnSystem] {player.Name} -> tier: {tier}");

                // ── SEM WHITELIST: vanilla puro, sem tocar no inventário ─────────
                if (tier == "vanilla")
                {
                    await EquipBaselineUnderwearAndSocksAsync(player);
                    Console.WriteLine($"[SpawnSystem] {player.Name} sem WL — respawn vanilla.");
                    return;
                }

                // ── Para todos os outros tiers: limpa e equipa ───────────────────
                int safetyDelay = Math.Max(0, _cfg.RespawnSafetyDelayMs);
                if (safetyDelay > 0)
                    await Task.Delay(safetyDelay);

                try
                {
                    player.Inventory.Clear();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SpawnSystem] Falha ao limpar inventario de {player.Name}: {ex.Message}");
                }

                int postClearDelay = Math.Max(0, _cfg.PostInventoryClearDelayMs);
                if (postClearDelay > 0)
                    await Task.Delay(postClearDelay);

                try
                {
                    player.ProcessCommand("EquipParachute");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SpawnSystem] Falha ao equipar paraquedas de {player.Name}: {ex.Message}");
                }

                int postChuteDelay = Math.Max(0, _cfg.PostParachuteEquipDelayMs);
                if (postChuteDelay > 0)
                    await Task.Delay(postChuteDelay);

                await EquipBaselineUnderwearAndSocksAsync(player);

                int preKitDelay = Math.Max(0, _cfg.PreKitEquipmentDelayMs);
                if (preKitDelay > 0)
                    await Task.Delay(preKitDelay);

                SpawnSettings kit = tier == "ouro"  ? _cfg.VipOuroSet
                                  : tier == "prata" ? _cfg.VipPrataSet
                                  :                   _cfg.WhitelistSet;

                // Equipa roupas sempre (sem cooldown)
                foreach (var item in kit.Equipment)
                {
                    if (string.IsNullOrWhiteSpace(item)) continue;
                    if (tier == "ouro" && IsBackpackItemId(item)) continue;
                    await TryEquipItemWithRetriesAsync(player, item);
                    int equipInterval = Math.Max(0, _cfg.EquipIntervalMs);
                    if (equipInterval > 0)
                        await Task.Delay(equipInterval);
                }

                // ── Cooldown para itens de inventário ────────────────────────────
                long agora            = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long cooldownSegundos = _cfg.KitCooldownMinutes * 60L;
                bool cooldownAtivo    = false;
                long segundosRestantes = 0;

                if (_kitCooldowns.TryGetValue(steamId, out long ultimaEntrega))
                {
                    segundosRestantes = (ultimaEntrega + cooldownSegundos) - agora;
                    cooldownAtivo = segundosRestantes > 0;
                }

                if (cooldownAtivo)
                {
                    int minutosRestantes = (int)Math.Ceiling(segundosRestantes / 60.0);
                    player.Reply($"[Kit] Seus itens estarao disponiveis em {minutosRestantes} minuto(s).", Color.Yellow);
                    Console.WriteLine($"[SpawnSystem] {player.Name} bloqueado por cooldown. {minutosRestantes} min restantes.");
                }
                else
                {
                    foreach (var item in kit.Inventory)
                    {
                        if (string.IsNullOrWhiteSpace(item)) continue;
                        if (tier == "ouro" && IsBackpackItemId(item)) continue;
                        await TryGiveItemWithRetriesAsync(player, item);
                        int giveInterval = Math.Max(0, _cfg.GiveItemIntervalMs);
                        if (giveInterval > 0)
                            await Task.Delay(giveInterval);
                    }

                    _kitCooldowns[steamId] = agora;
                    SaveData("SpawnKitCooldowns", _kitCooldowns);

                    string msg = (tier == "whitelist") ? _cfg.WlKitMessage : _cfg.Message;
                    if (!string.IsNullOrEmpty(msg))
                        player.Reply(msg, Color.Green);

                    Console.WriteLine($"[SpawnSystem] Kit '{tier}' entregue para {player.Name}.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpawnSystem] Erro nao tratado no respawn: {ex}");
            }
        }

        private static bool IsBackpackItemId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return false;
            return itemId.IndexOf("backpack", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task TryEquipItemWithRetriesAsync(PlayerBase player, string itemId)
        {
            int attempts = Math.Max(1, _cfg.ItemSpawnAttemptCount);
            int between = Math.Max(0, _cfg.ItemSpawnAttemptDelayMs);
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    player.EquipItem(itemId);
                    return;
                }
                catch (Exception ex)
                {
                    if (i == attempts - 1)
                        Console.WriteLine($"[SpawnSystem] Falha ao equipar '{itemId}' para {player.Name} apos {attempts} tentativa(s): {ex.Message}");
                }
                if (i < attempts - 1 && between > 0)
                    await Task.Delay(between);
            }
        }

        private async Task TryGiveItemWithRetriesAsync(PlayerBase player, string itemId)
        {
            int attempts = Math.Max(1, _cfg.ItemSpawnAttemptCount);
            int between = Math.Max(0, _cfg.ItemSpawnAttemptDelayMs);
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    player.GiveItem(itemId);
                    return;
                }
                catch (Exception ex)
                {
                    if (i == attempts - 1)
                        Console.WriteLine($"[SpawnSystem] Falha ao entregar '{itemId}' para {player.Name} apos {attempts} tentativa(s): {ex.Message}");
                }
                if (i < attempts - 1 && between > 0)
                    await Task.Delay(between);
            }
        }

        private static void SanitizeVipOuroNoBackpackItems(RespawnConfig cfg)
        {
            if (cfg?.VipOuroSet == null) return;
            cfg.VipOuroSet.Equipment?.RemoveAll(IsBackpackItemId);
            cfg.VipOuroSet.Inventory?.RemoveAll(IsBackpackItemId);
        }

        private bool IsInFile(string filePath, string steamId)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                return File.ReadAllLines(filePath)
                           .Any(l => l.Trim().Equals(steamId, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpawnSystem] Erro ao ler {filePath}: {ex.Message}");
                return false;
            }
        }

        private async Task EquipBaselineUnderwearAndSocksAsync(PlayerBase player)
        {
            try
            {
                int equipInterval = Math.Max(0, _cfg.EquipIntervalMs);
                bool isFemale = IsLikelyFemale(player);
                if (isFemale)
                {
                    if (!string.IsNullOrWhiteSpace(_cfg.FemaleBraItemId))
                    {
                        await TryEquipItemWithRetriesAsync(player, _cfg.FemaleBraItemId);
                        if (equipInterval > 0) await Task.Delay(equipInterval);
                    }
                    if (!string.IsNullOrWhiteSpace(_cfg.FemaleUnderwearItemId))
                    {
                        await TryEquipItemWithRetriesAsync(player, _cfg.FemaleUnderwearItemId);
                        if (equipInterval > 0) await Task.Delay(equipInterval);
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(_cfg.MaleUnderwearItemId))
                    {
                        await TryEquipItemWithRetriesAsync(player, _cfg.MaleUnderwearItemId);
                        if (equipInterval > 0) await Task.Delay(equipInterval);
                    }
                }

                if (!string.IsNullOrWhiteSpace(_cfg.SocksItemId))
                {
                    await TryEquipItemWithRetriesAsync(player, _cfg.SocksItemId);
                    if (equipInterval > 0) await Task.Delay(equipInterval);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpawnSystem] Falha ao equipar roupas íntimas/meias: {ex.Message}");
            }
        }

        private bool IsLikelyFemale(PlayerBase player)
        {
            try
            {
                var type = player.GetType();
                var boolProp = type.GetProperty("IsFemale");
                if (boolProp?.PropertyType == typeof(bool))
                    return (bool)boolProp.GetValue(player);

                foreach (var propName in new[] { "Gender", "Sex", "CharacterGender" })
                {
                    var prop = type.GetProperty(propName);
                    if (prop == null) continue;
                    var value = prop.GetValue(player);
                    if (value == null) continue;

                    var text = value.ToString()?.Trim().ToLowerInvariant() ?? "";
                    if (text.Contains("female") || text == "1")
                        return true;
                    if (text.Contains("male") || text == "0")
                        return false;
                }
            }
            catch
            {
                // fallback silencioso para não interromper o respawn
            }

            return false;
        }

    }
}
