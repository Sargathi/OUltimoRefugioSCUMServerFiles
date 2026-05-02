using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

namespace SpawnSystem
{
    #region Configuration

    public class SpawnSettings
    {
        // Items to be automatically put into clothing slots (clothes, bags, etc.)
        public List<string> Equipment { get; set; } = new List<string>();

        // Items to be placed inside pockets or backpack (tools, consumables)
        public List<string> Inventory { get; set; } = new List<string>();
    }

    public class RespawnConfig
    {
        public bool Enabled { get; set; } = true;
        public string VipPrataPermission { get; set; } = "vip.prata";
        public string VipOuroPermission { get; set; } = "vip.ouro";
        public string Message { get; set; } = "Vantagens VIP: Equipamento de triagem inicial entregue com sucesso.";
        public int InitialRespawnDelayMs { get; set; } = 1000;
        public int PostInventoryClearDelayMs { get; set; } = 1000;
        public int PostEquipmentBatchDelayMs { get; set; } = 300;
        public int ItemSpawnAttemptCount { get; set; } = 3;
        public int ItemSpawnAttemptDelayMs { get; set; } = 400;

        // Default items for regular players
        public SpawnSettings StandardSet { get; set; } = new SpawnSettings
        {
            Equipment = new List<string> { "Bamboo_Hat_02", "Beijing_Shoes_04", "Tang_pants_04", "Tang_shirt_04" },
            Inventory = new List<string> { "Apple_2", "BeefRavioli", "CannedGoulash", "Emergency_Bandage_Big" }
        };

        // Kit Prata: arco, flechas, taco, MRE, bandagem + roupas táticas
        public SpawnSettings VipPrataSet { get; set; } = new SpawnSettings
        {
            Equipment = new List<string>
            {
                "Military_Boonie_Hat_07",
                "Camouflage_Jacket_01",
                "Hiking_Boots_03",
                "Open_Finger_Gloves_01",
                "Short_Trousers_01_05",
                "Improvised_Quiver_01"
            },
            Inventory = new List<string>
            {
                "Weapon_Improvised_Bow",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "2H_Baseball_Bat_With_Wire",
                "MRE_Stew",
                "Emergency_Bandage_Big"
            }
        };

        // Kit Ouro: tudo do Prata + Hunter 85 e munição Cal .22 (sem itens cujo id contenha "backpack")
        public SpawnSettings VipOuroSet { get; set; } = new SpawnSettings
        {
            Equipment = new List<string>
            {
                "Military_Boonie_Hat_07",
                "Camouflage_Jacket_01",
                "Hiking_Boots_03",
                "Open_Finger_Gloves_01",
                "Short_Trousers_01_05",
                "Improvised_Quiver_01"
            },
            Inventory = new List<string>
            {
                "Weapon_Improvised_Bow",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "Wooden_Arrow_Stone_Tip",
                "2H_Baseball_Bat_With_Wire",
                "BPC_Weapon_Hunter",
                "Cal_22_Ammobox",
                "MRE_Stew",
                "Emergency_Bandage_Big"
            }
        };
    }

    #endregion

    [Info("Custom Respawn Items", "OUltimoRefugio", "1.3.3")]
    [Description("Gives specific item sets to standard and VIP players upon respawn.")]
    public class CustomRespawnPlugin : OxygenPlugin
    {
        private RespawnConfig _cfg;

        #region Initialization

        public override void OnLoad()
        {
            _cfg = LoadConfig<RespawnConfig>() ?? new RespawnConfig();
            SanitizeVipOuroNoBackpackItems(_cfg);
            SaveConfig(_cfg);
            Console.WriteLine("[SpawnSystem] Plugin v1.3.3 carregado. Kits: Padrão, Prata e Ouro.");
        }

        #endregion

        #region Hook: OnPlayerRespawned

        public override void OnPlayerRespawned(PlayerBase player)
        {
            _ = HandlePlayerRespawnedAsync(player);
        }

        private async Task HandlePlayerRespawnedAsync(PlayerBase player)
        {
            try
            {
                if (player == null || !_cfg.Enabled) return;

                int initialDelay = Math.Max(0, _cfg.InitialRespawnDelayMs);
                if (initialDelay > 0)
                    await Task.Delay(initialDelay);

                // Determina qual kit usar com base na permissão do jogador
                SpawnSettings selectedSet;
                bool isVip = false;
                bool isVipOuro = false;

                if (player.HasPermission(_cfg.VipOuroPermission))
                {
                    selectedSet = _cfg.VipOuroSet;
                    isVip = true;
                    isVipOuro = true;
                }
                else if (player.HasPermission(_cfg.VipPrataPermission))
                {
                    selectedSet = _cfg.VipPrataSet;
                    isVip = true;
                }
                else
                {
                    selectedSet = _cfg.StandardSet;
                }

                // Limpa o inventário padrão do SCUM (roupas de presidiário)
                // SOMENTE após o delay, quando os slots já estão inicializados
                player.Inventory.Clear();

                int postClear = Math.Max(0, _cfg.PostInventoryClearDelayMs);
                if (postClear > 0)
                    await Task.Delay(postClear);

                // Pequena pausa após o Clear para o servidor processar a limpeza
                await Task.Delay(300);

                // 1. Equipa roupas e bolsas primeiro (cria espaço no inventário)
                foreach (var item in selectedSet.Equipment)
                {
                    string equipId = item;
                    if (isVipOuro && IsBackpackItemId(equipId)) continue;
                    await TryEquipItemWithRetriesAsync(player, equipId);
                }

                // Pausa para garantir que os slots de roupa estejam prontos
                int postEquipBatch = Math.Max(0, _cfg.PostEquipmentBatchDelayMs);
                if (postEquipBatch > 0)
                    await Task.Delay(postEquipBatch);

                // 2. Entrega ferramentas e armas no inventário
                foreach (var item in selectedSet.Inventory)
                {
                    string giveId = item;
                    if (isVipOuro && IsBackpackItemId(giveId)) continue;
                    await TryGiveItemWithRetriesAsync(player, giveId);
                }

                // Notifica o jogador VIP
                if (isVip && !string.IsNullOrEmpty(_cfg.Message))
                {
                    player.Reply(_cfg.Message, Color.Green);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SpawnSystem] Erro nao tratado no respawn: {ex}");
            }
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
                        Console.WriteLine($"[SpawnSystem] Falha ao equipar '{itemId}' apos {attempts} tentativa(s): {ex.Message}");
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
                        Console.WriteLine($"[SpawnSystem] Falha ao entregar '{itemId}' apos {attempts} tentativa(s): {ex.Message}");
                }
                if (i < attempts - 1 && between > 0)
                    await Task.Delay(between);
            }
        }

        private static bool IsBackpackItemId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return false;
            return itemId.IndexOf("backpack", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SanitizeVipOuroNoBackpackItems(RespawnConfig cfg)
        {
            if (cfg?.VipOuroSet == null) return;
            cfg.VipOuroSet.Equipment?.RemoveAll(IsBackpackItemId);
            cfg.VipOuroSet.Inventory?.RemoveAll(IsBackpackItemId);
        }

        #endregion
    }
}
