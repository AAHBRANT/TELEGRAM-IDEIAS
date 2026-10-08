using System.Text;
using BotIdeias.Ia;
using BotIdeias.Ideias;
using BotIdeias.Painel;
using BotIdeias.Telegram;

namespace BotIdeias.Servicos;

// Toda a regra do bot: recebe uma mensagem do Telegram, registra a ideia, analisa, reorganiza e atualiza o painel.
public class ProcessadorMensagem
{
    internal const int TamanhoMinimo = 15;
    internal const int ContextoParaIa = 120;
    internal const int LimitePainel = 400;
    internal const int DuracaoMaximaAudioSeg = 180;
    internal const long TamanhoMaximoAudioBytes = 20 * 1024 * 1024;

    internal const string Ajuda =
        "Banco de Ideias — escreva ou grave um áudio com sua ideia, do jeito que vier. Eu registro, analiso e organizo.\n\n" +
        "Exemplo: \"Seria bom o sistema avisar quando o material recebido for diferente do pedido.\"\n\n" +
        "Comandos:\n/painel — mostra a lista organizada (e fixa no chat)\n/ideia IDEIA-0001 — detalhes de uma ideia\n/ajuda — esta mensagem";

    private readonly IRepositorioIdeias _repo;
    private readonly IAnalisadorIdeias _analisador;
    private readonly ITelegramCliente _telegram;
    private readonly TimeProvider _relogio;
    private readonly ITranscritor _transcritor;

    public ProcessadorMensagem(IRepositorioIdeias repo, IAnalisadorIdeias analisador, ITelegramCliente telegram, TimeProvider relogio, ITranscritor? transcritor = null)
    {
        _transcritor = transcritor ?? new TranscritorIndisponivel();
        _repo = repo;
        _analisador = analisador;
        _telegram = telegram;
        _relogio = relogio;
    }

    public async Task ProcessarAsync(Update update, CancellationToken ct)
    {
        var m = update.Message;
        if (m?.Chat is null || m.From is null || m.From.IsBot) return;
        var texto = (m.Text ?? m.Caption ?? string.Empty).Trim();
        var audio = m.Voice ?? m.Audio;
        if (texto.Length == 0 && audio?.FileId is null) return;

        var chatId = m.Chat.Id;
        if (await _repo.JaTratadoAsync(chatId, update.UpdateId, ct)) return;

        if (texto.Length == 0)
        {
            var transcrito = await TranscreverAsync(chatId, m.MessageId, audio!, ct);
            if (transcrito is null) return;
            await _telegram.EnviarAsync(chatId, $"🎙️ Entendi assim:\n\"{transcrito}\"", m.MessageId, ct);
            texto = transcrito;
        }

        if (texto.StartsWith('/'))
        {
            await TratarComandoAsync(chatId, m.MessageId, texto, ct);
            return;
        }

        if (texto.Length < TamanhoMinimo)
        {
            await _telegram.EnviarAsync(chatId, "Conte um pouco mais sobre a ideia: o que você gostaria que o sistema fizesse? Para ver as instruções, envie /ajuda.", m.MessageId, ct);
            return;
        }

        await RegistrarIdeiaAsync(chatId, m.MessageId, m.From.NomeExibicao, texto, ct);
    }

    // null = já respondi ao usuário explicando o problema.
    private async Task<string?> TranscreverAsync(long chatId, long mensagemId, Midia audio, CancellationToken ct)
    {
        if (!_transcritor.Disponivel)
        {
            await _telegram.EnviarAsync(chatId, "Ainda não consigo ouvir áudios. Escreva a ideia em texto, por favor.", mensagemId, ct);
            return null;
        }
        if (audio.Duration > DuracaoMaximaAudioSeg || audio.FileSize > TamanhoMaximoAudioBytes)
        {
            await _telegram.EnviarAsync(chatId, $"O áudio é longo demais. Envie até {DuracaoMaximaAudioSeg / 60} minutos, ou escreva a ideia.", mensagemId, ct);
            return null;
        }

        var bytes = await _telegram.BaixarArquivoAsync(audio.FileId!, ct);
        var texto = bytes is null ? null : await _transcritor.TranscreverAsync(bytes, "audio.ogg", ct);
        if (string.IsNullOrWhiteSpace(texto))
        {
            await _telegram.EnviarAsync(chatId, "Não consegui entender o áudio. Tente gravar de novo ou escreva a ideia.", mensagemId, ct);
            return null;
        }
        return texto.Trim();
    }

    private async Task RegistrarIdeiaAsync(long chatId, long mensagemId, string autor, string texto, CancellationToken ct)
    {
        var existentes = await _repo.ListarAsync(chatId, ContextoParaIa, ct);
        var resumidas = existentes.Select(e => new IdeiaResumida(e.Numero, e.Codigo, e.Titulo, e.Tema)).ToList();
        var proximo = (existentes.Count == 0 ? 0 : existentes.Max(e => e.Numero)) + 1;

        var analise = await _analisador.AnalisarAsync(texto, resumidas, proximo, ct);

        var salva = await _repo.AdicionarAsync(chatId, numero => new IdeiaEntidade
        {
            Numero = numero,
            Texto = texto,
            Titulo = analise.Titulo,
            Resumo = analise.Resumo,
            Tema = analise.Tema,
            Modulo = analise.Modulo,
            Prioridade = analise.Prioridade,
            Autor = autor,
            SemelhanteA = analise.SemelhanteNumero is { } s ? IdeiaEntidade.CodigoDe(s) : string.Empty,
            CriadaEmUtc = _relogio.GetUtcNow().UtcDateTime
        }, ct);

        // A reorganização vale para as ideias que existiam; o número da nova pode ter mudado numa corrida.
        var temas = analise.Temas.Where(t => t.Key != proximo).ToDictionary(t => t.Key, t => t.Value);
        if (temas.Count > 0) await _repo.AtualizarTemasAsync(chatId, temas, ct);

        await _telegram.EnviarAsync(chatId, MontarResposta(salva, analise, existentes), mensagemId, ct);
        await AtualizarPainelAsync(chatId, ct);
    }

    internal static string MontarResposta(IdeiaEntidade ideia, ResultadoAnalise analise, IReadOnlyList<IdeiaEntidade> existentes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ideia registrada!");
        sb.AppendLine();
        sb.AppendLine($"ID: {ideia.Codigo}");
        sb.AppendLine($"Título: {ideia.Titulo}");
        sb.AppendLine($"Tema: {ideia.Tema}");
        if (!string.IsNullOrWhiteSpace(ideia.Modulo)) sb.AppendLine($"Módulo: {ideia.Modulo}");
        sb.AppendLine($"Prioridade sugerida: {ideia.Prioridade}");
        if (!string.IsNullOrWhiteSpace(ideia.Resumo)) sb.AppendLine($"\nResumo: {ideia.Resumo}");

        if (analise.SemelhanteNumero is { } n)
        {
            var parecida = existentes.FirstOrDefault(e => e.Numero == n);
            if (parecida is not null)
                sb.AppendLine($"\nParece com {parecida.Codigo} — {parecida.Titulo}. Registrei mesmo assim e deixei a semelhança anotada.");
        }

        foreach (var p in analise.Perguntas) sb.AppendLine($"\n❓ {p}");
        if (analise.Origem == "Local") sb.AppendLine("\n(Análise automática indisponível agora; registrei sem classificar.)");
        return sb.ToString().TrimEnd();
    }

    private async Task TratarComandoAsync(long chatId, long mensagemId, string texto, CancellationToken ct)
    {
        var partes = texto.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var comando = partes[0].Split('@')[0].ToLowerInvariant();

        switch (comando)
        {
            case "/painel":
                // Painel novo (e fixado) sempre que pedido: se o antigo foi apagado, este substitui.
                await AtualizarPainelAsync(chatId, ct, forcarNovo: true);
                break;

            case "/ideia" when partes.Length == 2:
                await EnviarDetalheAsync(chatId, mensagemId, partes[1], ct);
                break;

            default:
                await _telegram.EnviarAsync(chatId, Ajuda, mensagemId, ct);
                break;
        }
    }

    private async Task EnviarDetalheAsync(long chatId, long mensagemId, string argumento, CancellationToken ct)
    {
        var digitos = new string(argumento.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digitos, out var numero) || await _repo.ObterAsync(chatId, numero, ct) is not { } ideia)
        {
            await _telegram.EnviarAsync(chatId, $"Não encontrei a ideia {argumento}.", mensagemId, ct);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{ideia.Codigo} — {ideia.Titulo}");
        sb.AppendLine($"Tema: {ideia.Tema}" + (string.IsNullOrWhiteSpace(ideia.Modulo) ? "" : $" · Módulo: {ideia.Modulo}"));
        sb.AppendLine($"Prioridade sugerida: {ideia.Prioridade} · Registrada por {ideia.Autor} em {ideia.CriadaEmUtc.AddHours(-3):dd/MM/yyyy HH:mm}");
        if (!string.IsNullOrWhiteSpace(ideia.Resumo)) sb.AppendLine($"\nResumo: {ideia.Resumo}");
        if (!string.IsNullOrWhiteSpace(ideia.SemelhanteA)) sb.AppendLine($"Parecida com: {ideia.SemelhanteA}");
        sb.AppendLine($"\nTexto original:\n{ideia.Texto}");
        await _telegram.EnviarAsync(chatId, sb.ToString().TrimEnd(), mensagemId, ct);
    }

    private async Task AtualizarPainelAsync(long chatId, CancellationToken ct, bool forcarNovo = false)
    {
        var ideias = await _repo.ListarAsync(chatId, LimitePainel, ct);
        var texto = PainelRenderer.Renderizar(ideias, _relogio.GetUtcNow().UtcDateTime);

        var painelId = forcarNovo ? 0 : await _repo.ObterPainelMensagemIdAsync(chatId, ct);
        if (painelId != 0 && await _telegram.EditarAsync(chatId, painelId, texto, ct)) return;

        var novoId = await _telegram.EnviarAsync(chatId, texto, null, ct);
        if (novoId is not { } id) return;
        await _repo.SalvarPainelMensagemIdAsync(chatId, id, ct);
        await _telegram.FixarAsync(chatId, id, ct);
    }
}
