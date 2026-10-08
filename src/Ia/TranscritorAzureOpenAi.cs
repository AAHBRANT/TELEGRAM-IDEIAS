using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BotIdeias.Ia;

public class TranscritorAzureOpenAi : ITranscritor
{
    private readonly HttpClient _http;
    private readonly AzureOpenAiOpcoes _opcoes;
    private readonly ILogger<TranscritorAzureOpenAi> _log;

    public TranscritorAzureOpenAi(HttpClient http, IOptions<AzureOpenAiOpcoes> opcoes, ILogger<TranscritorAzureOpenAi> log)
    {
        _http = http;
        _opcoes = opcoes.Value;
        _log = log;
    }

    public bool Disponivel => _opcoes.TranscricaoConfigurada;

    public async Task<string?> TranscreverAsync(byte[] audio, string nomeArquivo, CancellationToken ct)
    {
        try
        {
            var url = $"{_opcoes.Endpoint!.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(_opcoes.DeploymentTranscricao!)}/audio/transcriptions?api-version={_opcoes.ApiVersion}";

            using var corpo = new MultipartFormDataContent();
            var arquivo = new ByteArrayContent(audio);
            arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            corpo.Add(arquivo, "file", nomeArquivo);
            corpo.Add(new StringContent("pt"), "language");
            corpo.Add(new StringContent("json"), "response_format");

            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = corpo };
            req.Headers.Add("api-key", _opcoes.ApiKey);

            using var resp = await _http.SendAsync(req, ct);
            var texto = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Transcrição falhou: {Status} {Corpo}", (int)resp.StatusCode, texto.Length > 400 ? texto[..400] : texto);
                return null;
            }

            using var doc = JsonDocument.Parse(texto);
            return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString()?.Trim() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Transcrição falhou.");
            return null;
        }
    }
}
