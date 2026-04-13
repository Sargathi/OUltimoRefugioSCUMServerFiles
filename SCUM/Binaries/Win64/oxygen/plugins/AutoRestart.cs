using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

// Versão 1.3.0: Removidos reinícios especiais de PVP/PVE (sexta 21h e domingo 23h).
// O servidor agora opera com modo fixo e zonas de PvP/PvE configuradas no mapa.
// Reinícios regulares a cada 6 horas: 00:00, 06:00, 12:00, 18:00 — todos os dias da semana.
[Info("AutoRestart", "OUltimoRefugio", "1.3.0")]
public class AutoRestart : OxygenPlugin
{
    private DateTime _nextRestart;
    private List<int> _announcementIntervals = new List<int> { 60, 30, 20, 10, 5, 1 };
    private List<int> _announcedMinutes = new List<int>();
    private bool _isRunning = false;

    public override void OnLoad()
    {
        CalculateNextRestart();
        _isRunning = true;
        
        Task.Run(async () => await RestartLoop());
        
        Console.WriteLine("[AutoRestart] Plugin Loaded. Next restart at: " + _nextRestart.ToString("HH:mm"));
    }

    public override void OnUnload()
    {
        _isRunning = false;
        Console.WriteLine("[AutoRestart] Plugin Unloaded.");
    }

    private void CalculateNextRestart()
    {
        DateTime now = DateTime.Now;
        _announcedMinutes.Clear();

        List<DateTime> candidates = new List<DateTime>();

        // Reinícios regulares de 6 em 6 horas: 00:00, 06:00, 12:00, 18:00
        // Válidos para TODOS os dias da semana, incluindo segunda-feira 00:00
        int[] hours = { 0, 6, 12, 18 };
        foreach (int hour in hours)
        {
            DateTime todayCandidate = new DateTime(now.Year, now.Month, now.Day, hour, 0, 0);
            DateTime tomorrowCandidate = todayCandidate.AddDays(1);
            candidates.Add(todayCandidate);
            candidates.Add(tomorrowCandidate);
        }

        candidates.Sort();

        foreach (var candidate in candidates)
        {
            if (candidate > now)
            {
                _nextRestart = candidate;
                return;
            }
        }
    }

    private async Task RestartLoop()
    {
        while (_isRunning)
        {
            try
            {
                CheckRestartStatus();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoRestart] Error in loop: {ex.Message}");
            }
            
            await Task.Delay(30000);
        }
    }

    private void CheckRestartStatus()
    {
        DateTime now = DateTime.Now;
        TimeSpan timeRemaining = _nextRestart - now;
        int minutesRemaining = (int)Math.Ceiling(timeRemaining.TotalMinutes);

        if (minutesRemaining <= 0)
        {
            _isRunning = false;
            TriggerRestart();
            return;
        }

        foreach (int interval in _announcementIntervals)
        {
            if (minutesRemaining <= interval && !_announcedMinutes.Contains(interval))
            {
                string msg = $"O SERVIDOR SERA REINICIADO EM {minutesRemaining} MINUTO(S)!";
                Server.ProcessCommandAsync($"Announce {msg}");
                _announcedMinutes.Add(interval);
            }
        }
    }

    private void TriggerRestart()
    {
        Server.ProcessCommandAsync("Announce REINICIANDO SERVIDOR AGORA! VOLTAMOS EM INSTANTES.");
        
        Task.Run(async () => {
            await Task.Delay(5000);
            await Server.ProcessCommandAsync("ShutdownServer pretty please");
        });
    }
}
