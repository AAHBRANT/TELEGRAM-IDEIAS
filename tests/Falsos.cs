using BotIdeias.Ia;
using BotIdeias.Ideias;
using BotIdeias.Telegram;

namespace BotIdeias.Tests;

internal class RepositorioMemoria : IRepositorioIdeias
{
    private readonly Dictionary<long, List<IdeiaEntidade>> _ideias = new();
    private readonly Dictionary<long, long> _painel = new();
    private readonly Dictionary<long, long> _updates = new();

    public Task<IdeiaEntidade> AdicionarAsync(long chatId, Func<int, IdeiaEntidade> criar, CancellationToken ct)
    {
        var lista = _ideias.TryGetValue(chatId, out var l) ? l : _ideias[chatId] = new();
        var ideia = criar(lista.Count == 0 ? 1 : lista.Max(x => x.Numero) + 1);
        ideia.Numero = lista.Count == 0 ? 1 : lista.Max(x => x.Numero) + 1;
        lista.Add(ideia);
        return Task.FromResult(ideia);
    }

    public Task<IReadOnlyList<IdeiaEntidade>> ListarAsync(long chatId, int maximo, CancellationToken ct)
    {
        var lista = _ideias.TryGetValue(chatId, out var l) ? l.OrderBy(x => x.Numero).ToList() : new();
        return Task.FromResult<IReadOnlyList<IdeiaEntidade>>(lista.Count <= maximo ? lista : lista.Skip(lista.Count - maximo).ToList());
    }

    public Task<IdeiaEntidade?> ObterAsync(long chatId, int numero, CancellationToken ct)
        => Task.FromResult(_ideias.TryGetValue(chatId, out var l) ? l.FirstOrDefault(x => x.Numero == numero) : null);

    public Task AtualizarTemasAsync(long chatId, IReadOnlyDictionary<int, string> temas, CancellationToken ct)
    {
        foreach (var (n, t) in temas)
            if (_ideias.TryGetValue(chatId, out var l) && l.FirstOrDefault(x => x.Numero == n) is { } i) i.Tema = t;
        return Task.CompletedTask;
    }

    public Task<long> ObterPainelMensagemIdAsync(long chatId, CancellationToken ct) => Task.FromResult(_painel.GetValueOrDefault(chatId));
    public Task SalvarPainelMensagemIdAsync(long chatId, long mensagemId, CancellationToken ct) { _painel[chatId] = mensagemId; return Task.CompletedTask; }

    public Task<bool> JaTratadoAsync(long chatId, long updateId, CancellationToken ct)
    {
        var jaFoi = updateId <= _updates.GetValueOrDefault(chatId);
        if (!jaFoi) _updates[chatId] = updateId;
        return Task.FromResult(jaFoi);
    }
}

internal class TelegramFalso : ITelegramCliente
{
    public List<(long Chat, long Id, string Texto)> Enviadas { get; } = new();
    public List<(long Id, string Texto)> Edicoes { get; } = new();
    public List<long> Fixadas { get; } = new();
    public bool EdicaoFalha { get; set; }
    private long _proximoId = 1000;

    public Task<long?> EnviarAsync(long chatId, string texto, long? respondeA, CancellationToken ct)
    {
        var id = ++_proximoId;
        Enviadas.Add((chatId, id, texto));
        return Task.FromResult<long?>(id);
    }

    public Task<bool> EditarAsync(long chatId, long mensagemId, string texto, CancellationToken ct)
    {
        if (EdicaoFalha) return Task.FromResult(false);
        Edicoes.Add((mensagemId, texto));
        return Task.FromResult(true);
    }

    public Task FixarAsync(long chatId, long mensagemId, CancellationToken ct) { Fixadas.Add(mensagemId); return Task.CompletedTask; }
}

internal class AnalisadorFalso : IAnalisadorIdeias
{
    public Func<string, IReadOnlyList<IdeiaResumida>, int, ResultadoAnalise>? Resposta { get; set; }

    public Task<ResultadoAnalise> AnalisarAsync(string texto, IReadOnlyList<IdeiaResumida> existentes, int numeroNovo, CancellationToken ct)
        => Task.FromResult(Resposta?.Invoke(texto, existentes, numeroNovo)
            ?? new ResultadoAnalise(AnalisadorLocal.TituloDe(texto), "", "", "Geral", "Média", null, [], new Dictionary<int, string> { [numeroNovo] = "Geral" }, "IA"));
}
