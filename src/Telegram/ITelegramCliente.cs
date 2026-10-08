namespace BotIdeias.Telegram;

public interface ITelegramCliente
{
    // Devolve o id da mensagem enviada, ou null se o Telegram recusou.
    Task<long?> EnviarAsync(long chatId, string texto, long? respondeA, CancellationToken ct);

    // false se a mensagem não existe mais ou não pôde ser editada.
    Task<bool> EditarAsync(long chatId, long mensagemId, string texto, CancellationToken ct);

    // Melhor esforço: em grupo exige que o bot seja administrador.
    Task FixarAsync(long chatId, long mensagemId, CancellationToken ct);
}
