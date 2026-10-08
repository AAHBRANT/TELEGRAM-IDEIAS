namespace BotIdeias.Ia;

// Usado quando o Azure OpenAI não está configurado ou falha: o bot continua registrando, sem análise.
public class AnalisadorLocal : IAnalisadorIdeias
{
    public Task<ResultadoAnalise> AnalisarAsync(string texto, IReadOnlyList<IdeiaResumida> existentes, int numeroNovo, CancellationToken ct)
        => Task.FromResult(new ResultadoAnalise(
            Titulo: TituloDe(texto),
            Resumo: string.Empty,
            Modulo: string.Empty,
            Tema: "Sem classificação",
            Prioridade: "Média",
            SemelhanteNumero: null,
            Perguntas: [],
            Temas: new Dictionary<int, string>(),
            Origem: "Local"));

    public static string TituloDe(string texto)
    {
        var primeira = texto.Split(['\n', '.', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).FirstOrDefault() ?? texto.Trim();
        if (primeira.Length == 0) return "Ideia sem título";
        if (primeira.Length > 90)
        {
            var corte = primeira.LastIndexOf(' ', 90);
            primeira = primeira[..(corte > 40 ? corte : 90)].TrimEnd(',', ';', ' ') + "…";
        }
        return char.ToUpperInvariant(primeira[0]) + primeira[1..];
    }
}
