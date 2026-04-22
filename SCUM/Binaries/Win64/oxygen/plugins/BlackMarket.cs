using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Oxygen.Csharp.API;
using Oxygen.Csharp.Core;

namespace BlackMarketSystem
{
    #region Data Models

    public class BlackMarketZone
    {
        public string Id { get; set; } = "C2_MercadoNegro";
        public string Label { get; set; } = "Mercado Negro C2";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RadiusMeters { get; set; } = 15.0;
    }

    public class BlackMarketItem
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public int PricePerUnit { get; set; } = 0;
        public int MaxPerSale { get; set; } = 30;
    }

    public class BlackMarketConfig
    {
        public bool Enabled { get; set; } = true;
        public int CooldownSeconds { get; set; } = 180;
        public string AdminPermission { get; set; } = "blackmarket.admin";
        public string LogFilePath { get; set; } = @"C:\scumserver\blackmarket_sales.log";

        public List<BlackMarketZone> Zones { get; set; } = new List<BlackMarketZone>
        {
            new BlackMarketZone
            {
                Id = "C2_MercadoNegro",
                Label = "Mercado Negro C2",
                X = -152176.171875,
                Y = 287610.88899,
                Z = 0.0,
                RadiusMeters = 15.0
            }
        };

        public List<BlackMarketItem> Items { get; set; } = new List<BlackMarketItem>
        {
            new BlackMarketItem { Code = "Joint01", Label = "Baseado", PricePerUnit = 300, MaxPerSale = 30 },
            new BlackMarketItem { Code = "Spliff",  Label = "Spliff",  PricePerUnit = 150, MaxPerSale = 30 }
        };
    }

    #endregion

    [Info("Black Market", "OUltimoRefugio", "2.4.0")]
    [Description("Mercado negro para itens bloqueados nos NPC Traders.")]
    public class BlackMarketPlugin : OxygenPlugin
    {
        private BlackMarketConfig _cfg;
        private Dictionary<string, DateTime> _cooldowns = new Dictionary<string, DateTime>();

        #region Initialization

        public override void OnLoad()
        {
            _cfg = LoadConfig<BlackMarketConfig>() ?? new BlackMarketConfig();
            SaveConfig(_cfg);

            Console.WriteLine("[BlackMarket] v2.4.0 carregado. Zonas: " + _cfg.Zones.Count + ", Itens: " + _cfg.Items.Count);
        }

        public override void OnUnload()
        {
            Console.WriteLine("[BlackMarket] Plugin descarregado.");
        }

        public override void OnPlayerDisconnected(PlayerBase player)
        {
            _cooldowns.Remove(player.SteamId);
        }

        #endregion

        #region Commands

        [Command("mn_ping")]
        private void PingCommand(PlayerBase player, string[] args)
        {
            player.Reply("[Mercado Negro] pong. Plugin ativo.", Color.Green);
            Console.WriteLine("[BlackMarket] /mn_ping por " + player.Name);
        }

        [Command("precos")]
        private void PrecosCommand(PlayerBase player, string[] args)
        {
            string msg = "\n=== MERCADO NEGRO ===\n";
            foreach (var it in _cfg.Items)
            {
                msg += "- " + it.Label + ": $" + it.PricePerUnit + "/un (max " + it.MaxPerSale + ")\n";
            }
            msg += "Cooldown: " + _cfg.CooldownSeconds + "s";
            player.Reply(msg, Color.Blue);
        }

        [Command("mn_zonas")]
        private void ZonasCommand(PlayerBase player, string[] args)
        {
            if (_cfg.Zones.Count == 0)
            {
                player.Reply("[Mercado Negro] Nenhuma zona configurada.", Color.Yellow);
                return;
            }

            double px = player.Location.X;
            double py = player.Location.Y;

            string msg = "\n=== ZONAS DE CONTRABANDO ===\n";
            foreach (var z in _cfg.Zones)
            {
                double meters = Distance2DMeters(px, py, z.X, z.Y);
                msg += "- " + z.Label + " [" + z.Id + "] raio " + z.RadiusMeters + "m (distancia: " + meters.ToString("F0") + "m)\n";
            }
            player.Reply(msg, Color.Blue);
        }

        [Command("mn_reload")]
        private void ReloadCommand(PlayerBase player, string[] args)
        {
            if (!player.HasPermission(_cfg.AdminPermission))
            {
                player.Reply("Permissao negada.", Color.Red);
                return;
            }

            _cfg = LoadConfig<BlackMarketConfig>() ?? new BlackMarketConfig();
            SaveConfig(_cfg);
            player.Reply("[Mercado Negro] Config recarregada.", Color.Green);
        }

        // /mn_debug                → lista todos os nomes distintos de itens do inventário com contagem
        // /mn_debug <substring>    → só nomes que contêm a substring (case-insensitive)
        [Command("mn_debug")]
        private void DebugCommand(PlayerBase player, string[] args)
        {
            if (!player.HasPermission(_cfg.AdminPermission))
            {
                player.Reply("Permissao negada.", Color.Red);
                return;
            }

            string filter = args.Length > 0 ? args[0].ToLower() : null;

            string msg = "\n=== DEBUG INVENTARIO ===\n";

            if (player.Inventory == null || player.Inventory.All == null)
            {
                msg += "Inventory/All = null\n";
                player.Reply(msg, Color.Yellow);
                Console.WriteLine(msg);
                return;
            }

            msg += "Inventory.Count=" + player.Inventory.Count + "  All.Count=" + player.Inventory.All.Count + "\n";

            // Contagem dos itens configurados
            foreach (var it in _cfg.Items)
            {
                int c = 0;
                foreach (var invItem in player.Inventory.All)
                {
                    if (invItem != null && invItem.Name != null && invItem.Name.Equals(it.Code, StringComparison.OrdinalIgnoreCase))
                        c++;
                }
                msg += "  [config] " + it.Code + " (" + it.Label + "): " + c + "\n";
            }

            // Agrupa todos os nomes distintos com contagem
            var counts = new Dictionary<string, int>();
            foreach (var invItem in player.Inventory.All)
            {
                if (invItem == null) continue;
                string nm = invItem.Name ?? "<null>";
                if (filter != null && nm.ToLower().IndexOf(filter) < 0) continue;
                if (!counts.ContainsKey(nm)) counts[nm] = 0;
                counts[nm]++;
            }

            var ordered = counts.OrderBy(kv => kv.Key).ToList();
            if (filter != null)
                msg += "--- itens com '" + filter + "' (" + ordered.Count + ") ---\n";
            else
                msg += "--- todos os nomes distintos (" + ordered.Count + ") ---\n";

            foreach (var kv in ordered)
            {
                msg += "  " + kv.Value.ToString().PadLeft(3) + "x  " + kv.Key + "\n";
            }

            // Responde no chat em pedaços se precisar (o chat SCUM tem limite de tamanho)
            ReplyChunked(player, msg, Color.Yellow);
            Console.WriteLine(msg);
        }

        private static void ReplyChunked(PlayerBase player, string msg, Color color)
        {
            // SCUM costuma aceitar mensagens longas, mas dividimos em blocos de ~1500 chars
            // só pra garantir que nada seja truncado no meio.
            const int chunk = 1500;
            if (msg.Length <= chunk)
            {
                player.Reply(msg, color);
                return;
            }

            int start = 0;
            while (start < msg.Length)
            {
                int end = Math.Min(start + chunk, msg.Length);
                int nl = msg.LastIndexOf('\n', end - 1, end - start);
                if (nl > start) end = nl + 1;
                player.Reply(msg.Substring(start, end - start), color);
                start = end;
            }
        }

        // /mn_inspect <codename>   → reflexão sobre o primeiro Item encontrado com esse Name.
        // Lista todas as propriedades e métodos — útil para descobrir se o jogo expõe
        // algo como Amount/StackSize/Quantity para contar stacks sem precisar destruir.
        [Command("mn_inspect")]
        private void InspectCommand(PlayerBase player, string[] args)
        {
            if (!player.HasPermission(_cfg.AdminPermission))
            {
                player.Reply("Permissao negada.", Color.Red);
                return;
            }
            if (args.Length < 1)
            {
                player.Reply("Uso: /mn_inspect <ItemName>. Ex: /mn_inspect Joint01", Color.Orange);
                return;
            }
            if (player.Inventory == null || player.Inventory.All == null)
            {
                player.Reply("Inventario inacessivel.", Color.Red);
                return;
            }

            string target = args[0];
            Item found = null;
            foreach (var invItem in player.Inventory.All)
            {
                if (invItem != null && invItem.Name != null && invItem.Name.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    found = invItem;
                    break;
                }
            }
            if (found == null)
            {
                player.Reply("[MN] Nao encontrei " + target + " no seu inventario.", Color.Red);
                return;
            }

            Type t = found.GetType();
            string msg = "\n=== INSPECT " + target + " (" + t.FullName + ") ===\n";

            msg += "-- Properties --\n";
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v = null;
                try { v = p.GetValue(found); } catch { }
                string vs = v == null ? "<null>" : v.ToString();
                if (vs.Length > 80) vs = vs.Substring(0, 80) + "…";
                msg += "  " + p.PropertyType.Name + " " + p.Name + " = " + vs + "\n";
            }

            msg += "-- Methods --\n";
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.IsSpecialName) continue; // ignora get_/set_
                if (m.DeclaringType == typeof(object)) continue;
                var ps = m.GetParameters();
                string sig = m.ReturnType.Name + " " + m.Name + "(" +
                    string.Join(", ", ps.Select(pp => pp.ParameterType.Name + " " + pp.Name)) + ")";
                msg += "  " + sig + "\n";
            }

            ReplyChunked(player, msg, Color.Yellow);
            Console.WriteLine(msg);
        }

        [Command("vender")]
        private async void VenderCommand(PlayerBase player, string[] args)
        {
            if (!_cfg.Enabled)
            {
                player.Reply("[Mercado Negro] Fora de operacao.", Color.Red);
                return;
            }

            if (args.Length < 1)
            {
                player.Reply("Uso: /vender <baseado|spliff> [qtd]", Color.Orange);
                return;
            }

            BlackMarketItem itemCfg = ResolveItem(args[0]);
            if (itemCfg == null)
            {
                player.Reply("[Mercado Negro] Item nao negociado. Use /precos.", Color.Red);
                return;
            }

            int pedidos = int.MaxValue;
            if (args.Length >= 2)
            {
                int.TryParse(args[1], out pedidos);
                if (pedidos <= 0) pedidos = int.MaxValue;
            }

            BlackMarketZone zona = FindSellZone(player);
            if (zona == null)
            {
                player.Reply("[Mercado Negro] Voce nao esta na zona de contrabando.", Color.Red);
                return;
            }

            if (_cooldowns.TryGetValue(player.SteamId, out DateTime until) && until > DateTime.UtcNow)
            {
                int sec = (int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds);
                player.Reply("[Mercado Negro] Aguarde " + sec + "s.", Color.Orange);
                return;
            }

            if (player.Inventory == null || player.Inventory.All == null)
            {
                player.Reply("[Mercado Negro] Nao consegui acessar seu inventario.", Color.Red);
                return;
            }

            // ATENÇÃO: cada objeto 'Item' pode representar um STACK (1..N unidades in-game).
            // Descobrimos essa diferença medindo a contagem total antes e depois de cada
            // Destroy(). Sempre usamos player.Inventory.All.Count(name) como fonte da
            // verdade — porque esse é o número que o jogo mostra no HUD do inventário.
            int totalAntes = CountInInventory(player, itemCfg.Code);

            if (totalAntes <= 0)
            {
                player.Reply("[Mercado Negro] Voce nao tem " + itemCfg.Label + " na mochila.", Color.Red);
                return;
            }

            int quer = pedidos;
            if (itemCfg.MaxPerSale > 0 && itemCfg.MaxPerSale < quer) quer = itemCfg.MaxPerSale;
            if (totalAntes < quer) quer = totalAntes;

            int removidos = 0;
            int restante  = quer;

            // Percorre os stacks destruindo um por um até atingir o alvo 'quer'.
            // Entre Destroy()s, re-escaneia o inventário para calcular quanto o
            // último Destroy() realmente removeu (pode ser 1 ou pode ser N de um stack).
            foreach (var invItem in new List<Item>(player.Inventory.All))
            {
                if (restante <= 0) break;
                if (invItem == null || invItem.Name == null) continue;
                if (!invItem.Name.Equals(itemCfg.Code, StringComparison.OrdinalIgnoreCase)) continue;

                int antesDoDestroy = CountInInventory(player, itemCfg.Code);
                try
                {
                    invItem.Destroy();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[BlackMarket] Destroy falhou: " + ex.Message);
                    break;
                }

                int depoisDoDestroy = CountInInventory(player, itemCfg.Code);
                int removidoNesseStack = antesDoDestroy - depoisDoDestroy;
                if (removidoNesseStack <= 0)
                {
                    Console.WriteLine("[BlackMarket] Destroy não alterou a contagem de " +
                        itemCfg.Code + " (antes=" + antesDoDestroy + " depois=" + depoisDoDestroy + ").");
                    break;
                }

                removidos += removidoNesseStack;
                restante  -= removidoNesseStack;

                Console.WriteLine("[BlackMarket] " + player.Name + ": stack " + itemCfg.Code +
                    " destruído (-" + removidoNesseStack + "). Total removido=" + removidos +
                    " restante=" + restante);
            }

            if (removidos <= 0)
            {
                player.Reply("[Mercado Negro] Nao consegui recolher a mercadoria.", Color.Red);
                return;
            }

            // IMPORTANTE: paga pelo que REALMENTE foi removido, não pelo pedido.
            // Se o último stack tinha mais unidades que o restante (ex.: pediu 30,
            // último stack era de 12 e restante era 5, removeu 12), paga pelos 12
            // — senão o jogador é prejudicado.
            int valor = removidos * itemCfg.PricePerUnit;

            await player.ProcessCommandAsync("ChangeCurrencyBalance Normal +" + valor);

            _cooldowns[player.SteamId] = DateTime.UtcNow.AddSeconds(_cfg.CooldownSeconds);

            player.Reply("[Mercado Negro] Vendidos " + removidos + "x " + itemCfg.Label + " por $" + valor + ".", Color.Green);

            LogSale(player, zona, itemCfg, removidos, valor);

            Console.WriteLine("[BlackMarket] " + player.Name + " vendeu " + removidos + "x " + itemCfg.Code + " por $" + valor);
        }

        #endregion

        #region Helpers

        private int CountInInventory(PlayerBase player, string code)
        {
            if (player.Inventory == null || player.Inventory.All == null || code == null) return 0;
            int c = 0;
            foreach (var invItem in player.Inventory.All)
            {
                if (invItem != null && invItem.Name != null && invItem.Name.Equals(code, StringComparison.OrdinalIgnoreCase))
                    c++;
            }
            return c;
        }

        private BlackMarketItem ResolveItem(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            string lower = input.ToLower();
            foreach (var it in _cfg.Items)
            {
                if (it.Code != null && it.Code.ToLower() == lower) return it;
                if (it.Label != null && it.Label.ToLower() == lower) return it;
            }
            return null;
        }

        private static double Distance2DMeters(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx;
            double dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy) / 100.0;
        }

        private BlackMarketZone FindSellZone(PlayerBase player)
        {
            double px = player.Location.X;
            double py = player.Location.Y;
            foreach (var z in _cfg.Zones)
            {
                double meters = Distance2DMeters(px, py, z.X, z.Y);
                if (meters <= z.RadiusMeters) return z;
            }
            return null;
        }

        private void LogSale(PlayerBase player, BlackMarketZone zona, BlackMarketItem it, int qty, int total)
        {
            try
            {
                string path = _cfg.LogFilePath;
                if (string.IsNullOrEmpty(path)) return;

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string line =
                    DateTime.UtcNow.ToString("o") + "\t" +
                    (player.SteamId ?? "") + "\t" +
                    Safe(player.Name) + "\t" +
                    Safe(zona.Id) + "\t" +
                    Safe(it.Code) + "\t" +
                    Safe(it.Label) + "\t" +
                    qty + "\t" +
                    total;

                File.AppendAllText(path, line + "\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BlackMarket] LogSale: " + ex.Message);
            }
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        #endregion
    }
}
