using BotIdeias.Ia;
using BotIdeias.Ideias;
using BotIdeias.Painel;
using BotIdeias.Servicos;
using BotIdeias.Telegram;

namespace BotIdeias.Tests;

public class BotTests
{
    private const long Chat = 8226485025;

    private static (ProcessadorMensagem Proc, RepositorioMemoria Repo, TelegramFalso Tg, AnalisadorFalso Ia) Montar()
    {
        var repo = new RepositorioMemoria();
        var tg = new TelegramFalso();
        var ia = new AnalisadorFalso();
        return (new ProcessadorMensagem(repo, ia, tg, TimeProvider.System), repo, tg, ia);
    }

    private static Update Msg(long updateId, string texto, long chat = Chat)
        => new() { UpdateId = updateId, Message = new Mensagem { MessageId = updateId, Chat = new Chat { Id = chat }, From = new Usuario { Id = 7, FirstName = "Rafaela" }, Text = texto } };

    [Fact]
    public async Task Ideia_E_Registrada_Respondida_E_Painel_Criado_E_Fixado()
    {
        var (proc, repo, tg, _) = Montar();
        await proc.ProcessarAsync(Msg(1, "O sistema deveria avisar quando um EPI estiver vencido."), default);

        var salvas = await repo.ListarAsync(Chat, 10, default);
        Assert.Single(salvas);
        Assert.Equal("IDEIA-0001", salvas[0].Codigo);
        Assert.Equal("O sistema deveria avisar quando um EPI estiver vencido.", salvas[0].Texto);
        Assert.Contains(tg.Enviadas, e => e.Texto.StartsWith("Ideia registrada!") && e.Texto.Contains("IDEIA-0001"));
        Assert.Contains(tg.Enviadas, e => e.Texto.Contains("BANCO DE IDEIAS — 1 ideia"));
        Assert.Single(tg.Fixadas);
    }

    [Fact]
    public async Task Segunda_Ideia_Edita_O_Painel_Existente_E_Reorganiza_Temas()
    {
        var (proc, repo, tg, ia) = Montar();
        await proc.ProcessarAsync(Msg(1, "Avisar quando o EPI estiver vencido no sistema."), default);
        ia.Resposta = (t, ex, n) => new ResultadoAnalise("Alerta de ASO", "", "Saúde", "Alertas de vencimento", "Alta", null, [],
            new Dictionary<int, string> { [1] = "Alertas de vencimento", [n] = "Alertas de vencimento" }, "IA");
        await proc.ProcessarAsync(Msg(2, "Avisar quando o ASO do trabalhador estiver vencendo."), default);

        var salvas = await repo.ListarAsync(Chat, 10, default);
        Assert.All(salvas, i => Assert.Equal("Alertas de vencimento", i.Tema)); // a ideia 1 foi reorganizada
        Assert.Single(tg.Fixadas); // painel criado uma vez só
        Assert.Contains(tg.Edicoes, e => e.Texto.Contains("ALERTAS DE VENCIMENTO (2)"));
    }

    [Fact]
    public async Task Painel_Apagado_Gera_Novo_Painel()
    {
        var (proc, _, tg, _) = Montar();
        await proc.ProcessarAsync(Msg(1, "Primeira ideia de teste do bot de ideias."), default);
        tg.EdicaoFalha = true;
        await proc.ProcessarAsync(Msg(2, "Segunda ideia de teste do bot de ideias."), default);
        Assert.Equal(2, tg.Fixadas.Count);
    }

    [Fact]
    public async Task Update_Repetido_Nao_Duplica()
    {
        var (proc, repo, _, _) = Montar();
        var u = Msg(5, "Ideia que o Telegram entregou duas vezes.");
        await proc.ProcessarAsync(u, default);
        await proc.ProcessarAsync(u, default);
        Assert.Single(await repo.ListarAsync(Chat, 10, default));
    }

    [Fact]
    public async Task Texto_Curto_Pede_Detalhe_E_Comandos_Respondem()
    {
        var (proc, repo, tg, _) = Montar();
        await proc.ProcessarAsync(Msg(1, "oi"), default);
        await proc.ProcessarAsync(Msg(2, "/ajuda"), default);
        await proc.ProcessarAsync(Msg(3, "/ideia IDEIA-0099"), default);
        Assert.Empty(await repo.ListarAsync(Chat, 10, default));
        Assert.Contains(tg.Enviadas, e => e.Texto.Contains("Conte um pouco mais"));
        Assert.Contains(tg.Enviadas, e => e.Texto.Contains("/painel"));
        Assert.Contains(tg.Enviadas, e => e.Texto.Contains("Não encontrei a ideia"));
    }

    [Fact]
    public async Task Detalhe_Mostra_Texto_Original()
    {
        var (proc, _, tg, _) = Montar();
        await proc.ProcessarAsync(Msg(1, "Texto original da ideia número um."), default);
        await proc.ProcessarAsync(Msg(2, "/ideia IDEIA-0001"), default);
        Assert.Contains(tg.Enviadas, e => e.Texto.Contains("Texto original:") && e.Texto.Contains("Texto original da ideia número um."));
    }

    [Fact]
    public async Task Semelhanca_E_Informada_Mas_Ideia_E_Registrada()
    {
        var (proc, repo, tg, ia) = Montar();
        await proc.ProcessarAsync(Msg(1, "Alerta de vencimento de EPI para os trabalhadores."), default);
        ia.Resposta = (t, ex, n) => new ResultadoAnalise("EPI vencido", "", "EPI", "EPI", "Média", 1, ["Qual EPI?"],
            new Dictionary<int, string> { [n] = "EPI" }, "IA");
        await proc.ProcessarAsync(Msg(2, "Avisar quando algum EPI estiver vencido."), default);

        Assert.Equal(2, (await repo.ListarAsync(Chat, 10, default)).Count);
        Assert.Contains(tg.Enviadas, e => e.Texto.Contains("Parece com IDEIA-0001") && e.Texto.Contains("❓ Qual EPI?"));
    }

    [Fact]
    public void Chat_So_E_Aceito_Se_Estiver_Na_Lista()
    {
        var o = new TelegramOpcoes { ChatIds = "8226485025, -1001234567890" };
        Assert.True(o.ChatPermitido(8226485025));
        Assert.True(o.ChatPermitido(-1001234567890));
        Assert.False(o.ChatPermitido(1));
        Assert.False(new TelegramOpcoes().ChatPermitido(8226485025)); // sem lista = ninguém (falha fechada)
    }

    [Fact]
    public void Painel_Agrupa_Por_Tema_E_Ordena_Por_Prioridade()
    {
        var ideias = new[]
        {
            new IdeiaEntidade { Numero = 1, Titulo = "A", Tema = "Alertas", Prioridade = "Baixa" },
            new IdeiaEntidade { Numero = 2, Titulo = "B", Tema = "Alertas", Prioridade = "Alta" },
            new IdeiaEntidade { Numero = 3, Titulo = "C", Tema = "Compras", Prioridade = "Média" },
        };
        var texto = PainelRenderer.Renderizar(ideias, new DateTime(2026, 10, 8, 15, 0, 0, DateTimeKind.Utc));
        Assert.Contains("3 ideias", texto);
        Assert.Contains("12:00 (Brasília)", texto);
        Assert.True(texto.IndexOf("ALERTAS (2)") < texto.IndexOf("COMPRAS (1)"));
        Assert.True(texto.IndexOf("IDEIA-0002") < texto.IndexOf("IDEIA-0001")); // Alta antes de Baixa
    }

    [Fact]
    public void Painel_Grande_E_Cortado_Abaixo_Do_Limite_Do_Telegram()
    {
        var ideias = Enumerable.Range(1, 300)
            .Select(i => new IdeiaEntidade { Numero = i, Titulo = new string('x', 60), Tema = $"Tema {i % 25}", Prioridade = "Média" })
            .ToList();
        var texto = PainelRenderer.Renderizar(ideias, DateTime.UtcNow);
        Assert.True(texto.Length < 4096);
        Assert.Contains("e mais", texto);
    }

    [Fact]
    public void Resposta_Da_Ia_E_Validada()
    {
        var existentes = new[] { new IdeiaResumida(1, "IDEIA-0001", "Algo", "Geral") };
        const string json = """
            ```json
            {"titulo":"EPI vencido","resumo":"Avisar","modulo":"EPI","tema":"Alertas","prioridade":"alta",
             "semelhante":99,"perguntas":["a","b","c"],
             "temas":[{"numero":1,"tema":"Alertas"},{"numero":42,"tema":"Fantasma"},{"numero":2,"tema":"Alertas"}]}
            ```
            """;
        var r = RespostaIa.Interpretar(json, "texto", existentes, 2)!;
        Assert.Equal("Alta", r.Prioridade);
        Assert.Null(r.SemelhanteNumero);                 // 99 não existe
        Assert.Equal(2, r.Perguntas.Count);               // limitado a 2
        Assert.False(r.Temas.ContainsKey(42));            // ideia inexistente ignorada
        Assert.Equal("Alertas", r.Temas[1]);
        Assert.Equal("Alertas", r.Temas[2]);
        Assert.Null(RespostaIa.Interpretar("isto não é json", "texto", existentes, 2));
    }
}
