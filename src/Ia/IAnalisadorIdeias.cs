namespace BotIdeias.Ia;

public record IdeiaResumida(int Numero, string Codigo, string Titulo, string Tema);

public record ResultadoAnalise(
    string Titulo,
    string Resumo,
    string Modulo,
    string Tema,
    string Prioridade,
    int? SemelhanteNumero,
    IReadOnlyList<string> Perguntas,
    // Reorganização: tema de cada ideia (inclusive as antigas), por número. Pode vir vazio.
    IReadOnlyDictionary<int, string> Temas,
    string Origem);

public interface IAnalisadorIdeias
{
    Task<ResultadoAnalise> AnalisarAsync(string texto, IReadOnlyList<IdeiaResumida> existentes, int numeroNovo, CancellationToken ct);
}
