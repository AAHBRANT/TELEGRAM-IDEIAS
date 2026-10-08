using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;

namespace BotIdeias.Ideias;

public class RepositorioTabela : IRepositorioIdeias
{
    private readonly TableClient _tabela;
    private bool _criada;
    private readonly SemaphoreSlim _trava = new(1, 1);

    public RepositorioTabela(IOptions<StorageOpcoes> opcoes)
    {
        var o = opcoes.Value;
        if (string.IsNullOrWhiteSpace(o.ConnectionString))
            throw new InvalidOperationException("Configure Storage__ConnectionString.");
        _tabela = new TableClient(o.ConnectionString, o.Tabela);
    }

    private async Task GarantirTabelaAsync(CancellationToken ct)
    {
        if (_criada) return;
        await _trava.WaitAsync(ct);
        try
        {
            if (_criada) return;
            await _tabela.CreateIfNotExistsAsync(ct);
            _criada = true;
        }
        finally { _trava.Release(); }
    }

    private static string Particao(long chatId) => chatId.ToString();

    private async Task<MetaEntidade> LerMetaAsync(long chatId, CancellationToken ct)
    {
        var r = await _tabela.GetEntityIfExistsAsync<MetaEntidade>(Particao(chatId), MetaEntidade.Chave, cancellationToken: ct);
        return r.HasValue ? r.Value! : new MetaEntidade { PartitionKey = Particao(chatId) };
    }

    // Grava a meta com concorrência otimista (ETag); quem perder a corrida relê e tenta de novo.
    private async Task<MetaEntidade> ModificarMetaAsync(long chatId, Action<MetaEntidade> alterar, CancellationToken ct)
    {
        for (var tentativa = 0; tentativa < 6; tentativa++)
        {
            var meta = await LerMetaAsync(chatId, ct);
            var nova = meta.ETag == default;
            alterar(meta);
            try
            {
                if (nova) await _tabela.AddEntityAsync(meta, ct);
                else await _tabela.UpdateEntityAsync(meta, meta.ETag, TableUpdateMode.Replace, ct);
                return meta;
            }
            catch (RequestFailedException e) when (e.Status is 409 or 412)
            {
                await Task.Delay(50 * (tentativa + 1), ct);
            }
        }
        throw new InvalidOperationException("Não foi possível atualizar o controle do chat (concorrência).");
    }

    public async Task<IdeiaEntidade> AdicionarAsync(long chatId, Func<int, IdeiaEntidade> criar, CancellationToken ct)
    {
        await GarantirTabelaAsync(ct);
        var meta = await ModificarMetaAsync(chatId, m => m.Contador++, ct);
        var ideia = criar(meta.Contador);
        ideia.PartitionKey = Particao(chatId);
        ideia.RowKey = IdeiaEntidade.ChaveDe(meta.Contador);
        ideia.Numero = meta.Contador;
        await _tabela.UpsertEntityAsync(ideia, TableUpdateMode.Replace, ct);
        return ideia;
    }

    public async Task<IReadOnlyList<IdeiaEntidade>> ListarAsync(long chatId, int maximo, CancellationToken ct)
    {
        await GarantirTabelaAsync(ct);
        var todas = new List<IdeiaEntidade>();
        var p = Particao(chatId);
        await foreach (var e in _tabela.QueryAsync<IdeiaEntidade>(
                           x => x.PartitionKey == p && x.RowKey != MetaEntidade.Chave, cancellationToken: ct))
            todas.Add(e);
        var ordenadas = todas.OrderBy(x => x.Numero).ToList();
        return ordenadas.Count <= maximo ? ordenadas : ordenadas.Skip(ordenadas.Count - maximo).ToList();
    }

    public async Task<IdeiaEntidade?> ObterAsync(long chatId, int numero, CancellationToken ct)
    {
        await GarantirTabelaAsync(ct);
        var r = await _tabela.GetEntityIfExistsAsync<IdeiaEntidade>(Particao(chatId), IdeiaEntidade.ChaveDe(numero), cancellationToken: ct);
        return r.HasValue ? r.Value : null;
    }

    public async Task AtualizarTemasAsync(long chatId, IReadOnlyDictionary<int, string> temas, CancellationToken ct)
    {
        await GarantirTabelaAsync(ct);
        foreach (var (numero, tema) in temas)
        {
            var ideia = await ObterAsync(chatId, numero, ct);
            if (ideia is null || ideia.Tema == tema) continue;
            ideia.Tema = tema;
            await _tabela.UpdateEntityAsync(ideia, ideia.ETag, TableUpdateMode.Merge, ct);
        }
    }

    public async Task<long> ObterPainelMensagemIdAsync(long chatId, CancellationToken ct)
    {
        await GarantirTabelaAsync(ct);
        return (await LerMetaAsync(chatId, ct)).PainelMensagemId;
    }

    public async Task SalvarPainelMensagemIdAsync(long chatId, long mensagemId, CancellationToken ct)
    {
        await GarantirTabelaAsync(ct);
        await ModificarMetaAsync(chatId, m => m.PainelMensagemId = mensagemId, ct);
    }

    public async Task<bool> JaTratadoAsync(long chatId, long updateId, CancellationToken ct)
    {
        await GarantirTabelaAsync(ct);
        var jaFoi = false;
        await ModificarMetaAsync(chatId, m =>
        {
            jaFoi = updateId <= m.UltimoUpdateId;
            if (!jaFoi) m.UltimoUpdateId = updateId;
        }, ct);
        return jaFoi;
    }
}
