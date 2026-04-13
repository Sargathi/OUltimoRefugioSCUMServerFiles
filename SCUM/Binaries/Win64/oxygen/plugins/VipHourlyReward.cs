using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

/// <summary>
/// Recompensa VIPs com moeda Normal a cada hora de tempo online acumulado individualmente.
///
/// Regras:
///   - Cada VIP tem um contador próprio de segundos online.
///   - Ao atingir 3600 segundos (1 hora), recebe a recompensa e o contador zera.
///   - Tolerância de disconnect: se o jogador reconectar em até 5 minutos, a contagem continua.
///   - Se ficar offline por mais de 5 minutos, o contador é zerado ao reconectar.
///
///   VIP Prata (permissão "vip.prata"): +$50 por hora
///   VIP Ouro  (permissão "vip.ouro"):  +$100 por hora
/// </summary>
[Info("VIP Hourly Reward", "OUltimoRefugio", "2.0.0")]
[Description("Tracks individual VIP online time and grants Normal currency every hour.")]
public class VipHourlyRewardPlugin : OxygenPlugin
{
    private const int REWARD_PRATA   = 50;
    private const int REWARD_OURO    = 100;
    private const int SECONDS_TO_PAY = 3600;          // 1 hora em segundos
    private const int DISCONNECT_TOLERANCE_SEC = 300; // 5 minutos de tolerância
    private const int TICK_INTERVAL_MS = 60_000;      // Verificação a cada 1 minuto

    private string _vipPrataFilePath = @"C:\scumserver\vip_prata.txt";
    private string _vipOuroFilePath = @"C:\scumserver\vip_ouro.txt";
    private HashSet<string> _vipPrataIds = new();
    private HashSet<string> _vipOuroIds = new();

    // Dicionário: SteamID → segundos acumulados online
    private readonly Dictionary<string, int> _onlineSeconds = new();

    // Dicionário: SteamID → DateTime em que o jogador desconectou (para tolerância)
    private readonly Dictionary<string, DateTime> _disconnectTime = new();

    private bool _isRunning = false;

    public override void OnLoad()
    {
        _vipPrataFilePath = Environment.GetEnvironmentVariable("VIP_PRATA_FILE_PATH") ?? _vipPrataFilePath;
        _vipOuroFilePath = Environment.GetEnvironmentVariable("VIP_OURO_FILE_PATH") ?? _vipOuroFilePath;
        ReloadVipFiles();
        _isRunning = true;
        Task.Run(async () => await TickLoop());
        Console.WriteLine(
            $"[VipHourlyReward] v2.0.0 carregado. Rastreamento individual ativo (tolerância de 5 min para disconnect). " +
            $"Arquivos VIP: prata='{_vipPrataFilePath}', ouro='{_vipOuroFilePath}'."
        );
    }

    public override void OnUnload()
    {
        _isRunning = false;
        Console.WriteLine("[VipHourlyReward] Plugin descarregado.");
    }

    // ─── Evento: jogador conectou ────────────────────────────────────────────
    public override void OnPlayerConnected(PlayerBase player)
    {
        if (player == null || string.IsNullOrEmpty(player.SteamId)) return;
        string steamId = player.SteamId;

        if (_disconnectTime.TryGetValue(steamId, out DateTime disconnectedAt))
        {
            double offlineSeconds = (DateTime.UtcNow - disconnectedAt).TotalSeconds;
            _disconnectTime.Remove(steamId);

            if (offlineSeconds <= DISCONNECT_TOLERANCE_SEC)
            {
                // Dentro da tolerância: contagem continua de onde parou
                Console.WriteLine($"[VipHourlyReward] {steamId} reconectou em {offlineSeconds:F0}s — contagem mantida ({_onlineSeconds.GetValueOrDefault(steamId, 0)}s).");
            }
            else
            {
                // Fora da tolerância: zera o contador
                _onlineSeconds[steamId] = 0;
                Console.WriteLine($"[VipHourlyReward] {steamId} reconectou após {offlineSeconds:F0}s — contagem zerada.");
            }
        }
        else
        {
            // Primeira conexão da sessão
            if (!_onlineSeconds.ContainsKey(steamId))
                _onlineSeconds[steamId] = 0;
        }
    }

    // ─── Evento: jogador desconectou ─────────────────────────────────────────
    public override void OnPlayerDisconnected(PlayerBase player)
    {
        if (player == null || string.IsNullOrEmpty(player.SteamId)) return;
        string steamId = player.SteamId;
        _disconnectTime[steamId] = DateTime.UtcNow;
        Console.WriteLine($"[VipHourlyReward] {steamId} desconectou. Contador pausado em {_onlineSeconds.GetValueOrDefault(steamId, 0)}s.");
    }

    // ─── Loop de tick (a cada 1 minuto) ──────────────────────────────────────
    private async Task TickLoop()
    {
        while (_isRunning)
        {
            await Task.Delay(TICK_INTERVAL_MS);

            try
            {
                await ProcessTick();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VipHourlyReward] Erro no tick: {ex.Message}");
            }
        }
    }

    private async Task ProcessTick()
    {
        // Recarrega arquivos para refletir alterações sem restart.
        ReloadVipFiles();

        var players = Server.AllPlayers;
        if (players == null || players.Count == 0) return;

        foreach (var player in players)
        {
            // Só processa VIPs (fonte: arquivos vip_*.txt)
            string steamId = player.SteamId;
            bool isOuro  = _vipOuroIds.Contains(steamId);
            bool isPrata = !isOuro && _vipPrataIds.Contains(steamId);
            if (!isOuro && !isPrata) continue;

            // Incrementa 60 segundos (1 minuto de tick)
            if (!_onlineSeconds.ContainsKey(steamId))
                _onlineSeconds[steamId] = 0;

            _onlineSeconds[steamId] += 60;

            // Verifica se atingiu 1 hora
            if (_onlineSeconds[steamId] >= SECONDS_TO_PAY)
            {
                _onlineSeconds[steamId] = 0; // Zera para a próxima hora

                int reward = isOuro ? REWARD_OURO : REWARD_PRATA;
                string plano = isOuro ? "Ouro" : "Prata";

                try
                {
                    await player.ProcessCommandAsync($"ChangeCurrencyBalance Normal +{reward}");
                    player.Reply($"[VIP {plano}] +${reward} depositados na sua conta bancária. Bom jogo!", Color.Green);
                    Console.WriteLine($"[VipHourlyReward] {steamId} ({plano}) recebeu +${reward} após 1h online.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VipHourlyReward] Erro ao pagar {steamId}: {ex.Message}");
                }
            }
        }
    }

    private void ReloadVipFiles()
    {
        _vipPrataIds = LoadIds(_vipPrataFilePath);
        _vipOuroIds = LoadIds(_vipOuroFilePath);
    }

    private HashSet<string> LoadIds(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new HashSet<string>();

            return File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VipHourlyReward] Erro ao ler arquivo VIP '{path}': {ex.Message}");
            return new HashSet<string>();
        }
    }
}
