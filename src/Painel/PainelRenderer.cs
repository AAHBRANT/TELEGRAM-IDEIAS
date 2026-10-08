using System.Text;
using BotIdeias.Ideias;

namespace BotIdeias.Painel;

// Monta o texto do painel fixado no Telegram. O texto é gerado por código a partir dos dados salvos — a IA só
// decide o tema de cada ideia, nunca escreve o painel (evita inventar ou omitir ideias).
public static class PainelRenderer
{
    private const int LimiteCaracteres = 3800;

    public static string Renderizar(IReadOnlyList<IdeiaEntidade> ideias, DateTime agoraUtc)
    {
        var sb = new StringBuilder();
        var brasilia = agoraUtc.AddHours(-3); // Brasília não tem horário de verão desde 2019.
        sb.AppendLine($"📋 BANCO DE IDEIAS — {ideias.Count} {(ideias.Count == 1 ? "ideia" : "ideias")}");
        sb.AppendLine($"Atualizado em {brasilia:dd/MM/yyyy HH:mm} (Brasília)");

        if (ideias.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("Nenhuma ideia ainda. Escreva a primeira aqui no chat.");
            return sb.ToString().TrimEnd();
        }

        var grupos = ideias
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Tema) ? "Sem classificação" : i.Tema)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var omitidas = 0;
        foreach (var grupo in grupos)
        {
            var linhas = grupo
                .OrderBy(i => PesoPrioridade(i.Prioridade))
                .ThenBy(i => i.Numero)
                .Select(i => $"  {i.Codigo} · {Cortar(i.Titulo, 70)}{Marca(i.Prioridade)}")
                .ToList();

            var bloco = new StringBuilder();
            bloco.AppendLine();
            bloco.AppendLine($"■ {grupo.Key.ToUpperInvariant()} ({grupo.Count()})");
            foreach (var l in linhas) bloco.AppendLine(l);

            if (sb.Length + bloco.Length > LimiteCaracteres)
            {
                omitidas += grupo.Count();
                continue;
            }
            sb.Append(bloco);
        }

        if (omitidas > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"… e mais {omitidas} ideias. Use /ideia IDEIA-0001 para ver os detalhes de uma.");
        }
        return sb.ToString().TrimEnd();
    }

    private static int PesoPrioridade(string p) => p switch { "Alta" => 0, "Baixa" => 2, _ => 1 };

    private static string Marca(string p) => p switch { "Alta" => " 🔴", "Baixa" => " 🟢", _ => "" };

    private static string Cortar(string valor, int max) => valor.Length <= max ? valor : valor[..(max - 1)].TrimEnd() + "…";
}
