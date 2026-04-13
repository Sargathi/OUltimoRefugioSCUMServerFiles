using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

// ============================================================
//  Discord Whitelist Plugin  v13.0.0
//  O Último Refúgio — SCUM Server
//
//  Fluxo para jogador SEM whitelist:
//   0s    — SendNotification: "ATENCAO: Voce NAO esta na Whitelist!"
//   5s    — SendNotification: "Voce pode permanecer por 30 minutos."
//   5min  — SendMessage: aviso de 25 min restantes + link canal WL
//  10min  — SendMessage: aviso de 20 min restantes + link canal WL
//  15min  — SendMessage: aviso de 15 min restantes + link canal WL
//  20min  — SendMessage: aviso de 10 min restantes + link canal WL
//  25min  — SendMessage: aviso de 5 min restantes + link canal WL
//  30min  — Kick
// ============================================================

namespace DiscordWhitelistSystem
{
    public class WLConfig
    {
        public bool   Enabled            { get; set; } = true;
        public string WhitelistFilePath  { get; set; } = @"C:\scumserver\whitelist_scum.txt";
        public string DiscordInviteLink  { get; set; } = "https://discord.gg/ybUvUWmykH";
        public string WlChannelMention   { get; set; } = "#whitelist";
    }

    [Info("Discord Whitelist", "OUltimoRefugio", "13.0.0")]
    [Description("Permite 30 minutos de jogo para visitantes sem whitelist, com avisos a cada 5 minutos.")]
    public class DiscordWhitelistPlugin : OxygenPlugin
    {
        private WLConfig _cfg;
        private readonly HashSet<string> _pendingKick = new HashSet<string>();

        public override void OnLoad()
        {
            _cfg = LoadConfig<WLConfig>() ?? new WLConfig();
            SaveConfig(_cfg);
            Console.WriteLine($"[Whitelist] v13.0.0 carregado. Arquivo: {_cfg.WhitelistFilePath}");
        }

        public override void OnPlayerConnected(PlayerBase player)
        {
            if (player == null || string.IsNullOrEmpty(player.SteamId)) return;
            if (!_cfg.Enabled) return;
            if (_pendingKick.Contains(player.SteamId)) return;

            if (IsWhitelisted(player.SteamId))
            {
                Console.WriteLine($"[Whitelist] OK: {player.Name} ({player.SteamId})");
                return;
            }

            string steamId = player.SteamId;
            int    dbId    = player.DatabaseId;
            string name    = player.Name ?? steamId;
            string link    = _cfg.DiscordInviteLink;
            string canal   = _cfg.WlChannelMention;

            // Linha monitorada pelo bot Python para alerta no Discord
            Console.WriteLine($"[Whitelist] SEM WL: {name} ({steamId})");
            _pendingKick.Add(steamId);

            // 0s — aviso grande (SendNotification)
            player.ProcessCommand($"SendNotification 2 {dbId} \"ATENCAO: Voce NAO esta na Whitelist deste servidor!\"");

            // 5s — segundo aviso grande
            this.After(5f, () =>
            {
                if (!_pendingKick.Contains(steamId)) return;
                player.ProcessCommand($"SendNotification 2 {dbId} \"Voce pode permanecer por 30 minutos. Faca a Whitelist para receber o Kit Basico de Spawn e permanecer sem limite de tempo!\"");
            });

            // 5min (300s) — aviso de chat: 25 min restantes
            this.After(300f, () =>
            {
                if (!_pendingKick.Contains(steamId)) return;
                player.ProcessCommand($"SendMessage 0 {dbId} \"[Whitelist] Voce tem 25 minutos restantes. Acesse {link} e faca sua Whitelist para receber o Kit Basico de Spawn e permanecer sem limite de tempo!\"");
            });

            // 10min (600s) — 20 min restantes
            this.After(600f, () =>
            {
                if (!_pendingKick.Contains(steamId)) return;
                player.ProcessCommand($"SendMessage 0 {dbId} \"[Whitelist] Voce tem 20 minutos restantes. Acesse {link} e faca sua Whitelist para receber o Kit Basico de Spawn e permanecer sem limite de tempo!\"");
            });

            // 15min (900s) — 15 min restantes
            this.After(900f, () =>
            {
                if (!_pendingKick.Contains(steamId)) return;
                player.ProcessCommand($"SendMessage 0 {dbId} \"[Whitelist] Voce tem 15 minutos restantes. Acesse {link} e faca sua Whitelist para receber o Kit Basico de Spawn e permanecer sem limite de tempo!\"");
            });

            // 20min (1200s) — 10 min restantes
            this.After(1200f, () =>
            {
                if (!_pendingKick.Contains(steamId)) return;
                player.ProcessCommand($"SendMessage 0 {dbId} \"[Whitelist] Voce tem 10 minutos restantes. Acesse {link} e faca sua Whitelist para receber o Kit Basico de Spawn e permanecer sem limite de tempo!\"");
            });

            // 25min (1500s) — 5 min restantes
            this.After(1500f, () =>
            {
                if (!_pendingKick.Contains(steamId)) return;
                player.ProcessCommand($"SendMessage 0 {dbId} \"[Whitelist] ULTIMO AVISO: 5 minutos restantes! Acesse {link} e faca sua Whitelist agora!\"");
            });

            // 30min (1800s) — kick
            this.After(1800f, () =>
            {
                _pendingKick.Remove(steamId);
                player.ProcessCommand($"kick {steamId}");
                Console.WriteLine($"[Whitelist] EXPULSO (30min): {name} ({steamId})");
            });
        }

        public override void OnPlayerDisconnected(PlayerBase player)
        {
            if (player == null || string.IsNullOrEmpty(player.SteamId)) return;
            _pendingKick.Remove(player.SteamId);
        }

        private bool IsWhitelisted(string steamId)
        {
            try
            {
                if (!File.Exists(_cfg.WhitelistFilePath))
                {
                    Console.WriteLine($"[Whitelist] ERRO: arquivo nao encontrado: {_cfg.WhitelistFilePath}");
                    return false;
                }
                return File.ReadAllLines(_cfg.WhitelistFilePath)
                           .Any(l => l.Trim().Equals(steamId, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Whitelist] Erro ao ler arquivo: {ex.Message}");
                return false;
            }
        }
    }
}
