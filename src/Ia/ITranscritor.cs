namespace BotIdeias.Ia;

public interface ITranscritor
{
    bool Disponivel { get; }

    // Texto falado no áudio, ou null se a transcrição falhou.
    Task<string?> TranscreverAsync(byte[] audio, string nomeArquivo, CancellationToken ct);
}

public class TranscritorIndisponivel : ITranscritor
{
    public bool Disponivel => false;
    public Task<string?> TranscreverAsync(byte[] audio, string nomeArquivo, CancellationToken ct) => Task.FromResult<string?>(null);
}
