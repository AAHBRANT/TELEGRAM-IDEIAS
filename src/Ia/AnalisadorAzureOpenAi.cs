using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BotIdeias.Ia;

// Analisa a ideia nova e reorganiza o painel com um modelo do Azure OpenAI (chat completions, resposta em JSON).
// Se algo falhar (rede, cota, JSON inválido), cai no AnalisadorLocal: a ideia nunca deixa de ser registrada.
public class AnalisadorAzureOpenAi : IAnalisadorIdeias
{
    internal const string InstrucaoSistema = """
        Você organiza ideias de melhoria do software de gestão de uma empresa brasileira de engenharia e construção
        (módulos como SST, EPI, treinamentos, inspeções, acidentes, compras, obras, pessoas, financeiro, qualidade).
        Receberá uma IDEIA NOVA e a lista das ideias já registradas (código, título, tema).
        Responda SOMENTE com um objeto JSON, sem texto fora dele, neste formato:
        {
          "titulo": "título curto e claro, até 90 caracteres",
          "resumo": "uma frase com o problema e o que se quer",
          "modulo": "módulo do sistema ou vazio se não der para saber",
          "tema": "tema curto (até 4 palavras) em que a ideia se encaixa",
          "prioridade": "Alta | Média | Baixa",
          "semelhante": número da ideia já registrada que é REALMENTE a mesma ideia, ou null,
          "perguntas": ["até 2 perguntas objetivas, só se faltar algo importante"],
          "temas": [{"numero": 1, "tema": "tema"}]
        }
        Regras:
        - Em "temas", liste TODAS as ideias (as antigas e a nova) com o melhor tema; reorganize agrupando ideias parecidas.
        - Reaproveite os temas existentes; crie tema novo só quando necessário; no máximo 10 temas no total.
        - Não invente fatos nem altere o sentido da ideia. Se houver dúvida, deixe "modulo" vazio e faça uma pergunta.
        - "semelhante" só quando for a mesma proposta, não apenas o mesmo assunto.
        - Escreva em português do Brasil.
        """;

    private readonly HttpClient _http;
    private readonly AzureOpenAiOpcoes _opcoes;
    private readonly IAnalisadorIdeias _reserva;
    private readonly ILogger<AnalisadorAzureOpenAi> _log;

    public AnalisadorAzureOpenAi(HttpClient http, IOptions<AzureOpenAiOpcoes> opcoes, ILogger<AnalisadorAzureOpenAi> log)
    {
        _http = http;
        _opcoes = opcoes.Value;
        _reserva = new AnalisadorLocal();
        _log = log;
    }

    public async Task<ResultadoAnalise> AnalisarAsync(string texto, IReadOnlyList<IdeiaResumida> existentes, int numeroNovo, CancellationToken ct)
    {
        try
        {
            var usuario = MontarMensagemUsuario(texto, existentes, numeroNovo);
            var url = $"{_opcoes.Endpoint!.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(_opcoes.Deployment!)}/chat/completions?api-version={_opcoes.ApiVersion}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    messages = new object[]
                    {
                        new { role = "system", content = InstrucaoSistema },
                        new { role = "user", content = usuario }
                    },
                    // Modelos da família GPT-5 recusam `temperature` e `max_tokens`: usam o padrão e `max_completion_tokens`
                    // (que também cobre os tokens de raciocínio interno, por isso o teto generoso).
                    max_completion_tokens = 6000,
                    response_format = new { type = "json_object" }
                })
            };
            req.Headers.Add("api-key", _opcoes.ApiKey);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detalhe = await resp.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Azure OpenAI respondeu {Status}; usando análise local. Detalhe: {Detalhe}",
                    (int)resp.StatusCode, detalhe.Length > 400 ? detalhe[..400] : detalhe);
                return await _reserva.AnalisarAsync(texto, existentes, numeroNovo, ct);
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var conteudo = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            var resultado = RespostaIa.Interpretar(conteudo, texto, existentes, numeroNovo);
            return resultado ?? await _reserva.AnalisarAsync(texto, existentes, numeroNovo, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Falha na análise com Azure OpenAI; usando análise local.");
            return await _reserva.AnalisarAsync(texto, existentes, numeroNovo, ct);
        }
    }

    internal static string MontarMensagemUsuario(string texto, IReadOnlyList<IdeiaResumida> existentes, int numeroNovo)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"IDEIA NOVA (será a número {numeroNovo}):");
        sb.AppendLine(texto);
        sb.AppendLine();
        sb.AppendLine("IDEIAS JÁ REGISTRADAS:");
        if (existentes.Count == 0) sb.AppendLine("(nenhuma)");
        foreach (var e in existentes) sb.AppendLine($"{e.Numero} | {e.Titulo} | tema: {e.Tema}");
        return sb.ToString();
    }
}

// Lê e valida o JSON devolvido pelo modelo. Nada que vem da IA é confiado sem checagem.
internal static class RespostaIa
{
    public static ResultadoAnalise? Interpretar(string conteudo, string textoOriginal, IReadOnlyList<IdeiaResumida> existentes, int numeroNovo)
    {
        var json = ExtrairJson(conteudo);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return null;

            var titulo = Limitar(Texto(raiz, "titulo"), 120);
            if (titulo.Length == 0) titulo = AnalisadorLocal.TituloDe(textoOriginal);

            var validos = existentes.Select(e => e.Numero).ToHashSet();
            int? semelhante = null;
            if (raiz.TryGetProperty("semelhante", out var s) && s.ValueKind == JsonValueKind.Number
                && s.TryGetInt32(out var n) && validos.Contains(n)) semelhante = n;

            var temas = new Dictionary<int, string>();
            if (raiz.TryGetProperty("temas", out var lista) && lista.ValueKind == JsonValueKind.Array)
                foreach (var item in lista.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!item.TryGetProperty("numero", out var num) || !num.TryGetInt32(out var numero)) continue;
                    if (numero != numeroNovo && !validos.Contains(numero)) continue;
                    var tema = Limitar(Texto(item, "tema"), 40);
                    if (tema.Length > 0) temas[numero] = tema;
                }

            var tema0 = Limitar(Texto(raiz, "tema"), 40);
            if (tema0.Length == 0) tema0 = temas.TryGetValue(numeroNovo, out var t) ? t : "Sem classificação";
            temas[numeroNovo] = tema0;

            var perguntas = new List<string>();
            if (raiz.TryGetProperty("perguntas", out var ps) && ps.ValueKind == JsonValueKind.Array)
                foreach (var p in ps.EnumerateArray().Take(2))
                    if (p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                        perguntas.Add(Limitar(p.GetString()!, 200));

            return new ResultadoAnalise(
                titulo,
                Limitar(Texto(raiz, "resumo"), 300),
                Limitar(Texto(raiz, "modulo"), 60),
                tema0,
                NormalizarPrioridade(Texto(raiz, "prioridade")),
                semelhante,
                perguntas,
                temas,
                "IA");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // O modelo às vezes envolve o JSON em ```json ... ```.
    internal static string? ExtrairJson(string conteudo)
    {
        var ini = conteudo.IndexOf('{');
        var fim = conteudo.LastIndexOf('}');
        return ini >= 0 && fim > ini ? conteudo[ini..(fim + 1)] : null;
    }

    internal static string NormalizarPrioridade(string valor)
    {
        var v = valor.Trim().ToLowerInvariant();
        if (v.StartsWith("alt")) return "Alta";
        if (v.StartsWith("bai")) return "Baixa";
        return "Média";
    }

    private static string Texto(JsonElement e, string nome)
        => e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static string Limitar(string valor, int max) => valor.Length <= max ? valor : valor[..max].TrimEnd() + "…";
}
