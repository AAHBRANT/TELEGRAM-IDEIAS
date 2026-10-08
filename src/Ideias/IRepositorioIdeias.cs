namespace BotIdeias.Ideias;

public interface IRepositorioIdeias
{
    // Reserva o próximo número do chat (atômico) e grava a ideia criada por `criar`.
    Task<IdeiaEntidade> AdicionarAsync(long chatId, Func<int, IdeiaEntidade> criar, CancellationToken ct);

    // Mais recentes por último; `maximo` limita às últimas N.
    Task<IReadOnlyList<IdeiaEntidade>> ListarAsync(long chatId, int maximo, CancellationToken ct);

    Task<IdeiaEntidade?> ObterAsync(long chatId, int numero, CancellationToken ct);

    // Atualiza o tema de várias ideias (reorganização feita pela IA).
    Task AtualizarTemasAsync(long chatId, IReadOnlyDictionary<int, string> temas, CancellationToken ct);

    Task<long> ObterPainelMensagemIdAsync(long chatId, CancellationToken ct);
    Task SalvarPainelMensagemIdAsync(long chatId, long mensagemId, CancellationToken ct);

    // true se este update já foi tratado (o Telegram pode reenviar o mesmo); registra o novo id.
    Task<bool> JaTratadoAsync(long chatId, long updateId, CancellationToken ct);
}
