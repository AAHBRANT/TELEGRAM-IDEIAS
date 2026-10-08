using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BotIdeias.Telegram;

public class TelegramCliente : ITelegramCliente
{
    private readonly HttpClient _http;
    private readonly TelegramOpcoes _opcoes;
    private readonly ILogger<TelegramCliente> _log;

    public TelegramCliente(HttpClient http, IOptions<TelegramOpcoes> opcoes, ILogger<TelegramCliente> log)
    {
        _http = http;
        _opcoes = opcoes.Value;
        _log = log;
    }

    private string Url(string metodo) => $"https://api.telegram.org/bot{_opcoes.BotToken}/{metodo}";

    private static string Cortar(string texto) => texto.Length > 4000 ? texto[..3990] + "…" : texto;

    public async Task<long?> EnviarAsync(long chatId, string texto, long? respondeA, CancellationToken ct)
    {
        var corpo = new Dictionary<string, object> { ["chat_id"] = chatId, ["text"] = Cortar(texto) };
        if (respondeA is { } r) corpo["reply_to_message_id"] = r;

        using var resp = await _http.PostAsJsonAsync(Url("sendMessage"), corpo, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("sendMessage falhou: {Status}", (int)resp.StatusCode);
            return null;
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("result", out var res)
            && res.TryGetProperty("message_id", out var id) && id.TryGetInt64(out var m) ? m : null;
    }

    public async Task<bool> EditarAsync(long chatId, long mensagemId, string texto, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(Url("editMessageText"),
            new Dictionary<string, object> { ["chat_id"] = chatId, ["message_id"] = mensagemId, ["text"] = Cortar(texto) }, ct);
        if (resp.IsSuccessStatusCode) return true;

        // "message is not modified" (texto igual) conta como sucesso; qualquer outro erro, não.
        var corpo = await resp.Content.ReadAsStringAsync(ct);
        if (corpo.Contains("message is not modified", StringComparison.OrdinalIgnoreCase)) return true;
        _log.LogWarning("editMessageText falhou: {Status}", (int)resp.StatusCode);
        return false;
    }

    public async Task<byte[]?> BaixarArquivoAsync(string fileId, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(Url("getFile"), new Dictionary<string, object> { ["file_id"] = fileId }, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("getFile falhou: {Status}", (int)resp.StatusCode);
            return null;
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("result", out var res)
            || !res.TryGetProperty("file_path", out var caminho) || caminho.GetString() is not { Length: > 0 } path)
            return null;

        using var arquivo = await _http.GetAsync($"https://api.telegram.org/file/bot{_opcoes.BotToken}/{path}", ct);
        if (!arquivo.IsSuccessStatusCode)
        {
            _log.LogWarning("Download do arquivo falhou: {Status}", (int)arquivo.StatusCode);
            return null;
        }
        return await arquivo.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task FixarAsync(long chatId, long mensagemId, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync(Url("pinChatMessage"),
                new Dictionary<string, object> { ["chat_id"] = chatId, ["message_id"] = mensagemId, ["disable_notification"] = true }, ct);
            if (!resp.IsSuccessStatusCode) _log.LogInformation("Não foi possível fixar o painel ({Status}).", (int)resp.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogInformation(ex, "Não foi possível fixar o painel.");
        }
    }
}
