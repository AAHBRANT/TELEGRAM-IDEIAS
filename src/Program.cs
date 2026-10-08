using System.Security.Cryptography;
using System.Text;
using BotIdeias;
using BotIdeias.Ia;
using BotIdeias.Ideias;
using BotIdeias.Servicos;
using BotIdeias.Telegram;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TelegramOpcoes>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<AzureOpenAiOpcoes>(builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.Configure<StorageOpcoes>(builder.Configuration.GetSection("Storage"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRepositorioIdeias, RepositorioTabela>();
builder.Services.AddHttpClient<ITelegramCliente, TelegramCliente>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<AnalisadorAzureOpenAi>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<IAnalisadorIdeias>(sp =>
    sp.GetRequiredService<IOptions<AzureOpenAiOpcoes>>().Value.Configurado
        ? sp.GetRequiredService<AnalisadorAzureOpenAi>()
        : new AnalisadorLocal());
builder.Services.AddHttpClient<TranscritorAzureOpenAi>(c => c.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddSingleton<ITranscritor>(sp =>
    sp.GetRequiredService<IOptions<AzureOpenAiOpcoes>>().Value.TranscricaoConfigurada
        ? sp.GetRequiredService<TranscritorAzureOpenAi>()
        : new TranscritorIndisponivel());
builder.Services.AddScoped<ProcessadorMensagem>();

var app = builder.Build();

app.MapGet("/", () => "Bot de Ideias no ar.");

// Webhook do Telegram. A proteção é dupla e falha fechada: segredo do cabeçalho + chat autorizado.
// Responde sempre 200 depois de validar: o Telegram reenvia o update em qualquer outro status.
app.MapPost("/telegram", async (Update update, HttpRequest request, IOptions<TelegramOpcoes> opcoes,
    ProcessadorMensagem processador, ITelegramCliente telegram, ILogger<Program> log, CancellationToken ct) =>
{
    var esperado = opcoes.Value.WebhookSecret;
    var recebido = request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
    if (string.IsNullOrWhiteSpace(esperado) || string.IsNullOrEmpty(recebido)
        || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(esperado), Encoding.UTF8.GetBytes(recebido)))
        return Results.Unauthorized();

    var chatId = update.Message?.Chat?.Id;
    if (chatId is null || !opcoes.Value.ChatPermitido(chatId.Value)) return Results.Ok();

    try
    {
        await processador.ProcessarAsync(update, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        log.LogError(ex, "Falha ao processar a mensagem do Telegram.");
        try
        {
            await telegram.EnviarAsync(chatId.Value, "Não consegui registrar sua ideia agora. Tente de novo em alguns minutos.",
                update.Message?.MessageId, CancellationToken.None);
        }
        catch { /* melhor esforço */ }
    }
    return Results.Ok();
});

app.Run();

public partial class Program;
