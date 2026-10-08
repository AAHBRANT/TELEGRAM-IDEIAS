namespace BotIdeias;

// Tudo vem de variáveis de ambiente (Container App), nunca do código. Nomes com "__" viram ":" no .NET:
//   Telegram__BotToken, Telegram__WebhookSecret, Telegram__ChatIds
//   AzureOpenAI__Endpoint, AzureOpenAI__ApiKey, AzureOpenAI__Deployment (opcional: AzureOpenAI__ApiVersion)
//   Storage__ConnectionString
public class TelegramOpcoes
{
    public string? BotToken { get; set; }
    public string? WebhookSecret { get; set; }
    // IDs de chat autorizados, separados por vírgula (privado = id do usuário; grupo = número negativo).
    public string? ChatIds { get; set; }

    public bool ChatPermitido(long chatId)
    {
        if (string.IsNullOrWhiteSpace(ChatIds)) return false;
        return ChatIds.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(x => long.TryParse(x, out var id) && id == chatId);
    }
}

public class AzureOpenAiOpcoes
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string? Deployment { get; set; }
    public string ApiVersion { get; set; } = "2025-04-01-preview";

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Deployment);
}

public class StorageOpcoes
{
    public string? ConnectionString { get; set; }
    public string Tabela { get; set; } = "ideias";
}
