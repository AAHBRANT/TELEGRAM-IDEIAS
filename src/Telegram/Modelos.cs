using System.Text.Json.Serialization;

namespace BotIdeias.Telegram;

// Subconjunto do Update do Telegram que o bot usa.
public class Update
{
    [JsonPropertyName("update_id")] public long UpdateId { get; set; }
    [JsonPropertyName("message")] public Mensagem? Message { get; set; }
}

public class Mensagem
{
    [JsonPropertyName("message_id")] public long MessageId { get; set; }
    [JsonPropertyName("from")] public Usuario? From { get; set; }
    [JsonPropertyName("chat")] public Chat? Chat { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("caption")] public string? Caption { get; set; }
    [JsonPropertyName("voice")] public Midia? Voice { get; set; }
    [JsonPropertyName("audio")] public Midia? Audio { get; set; }
}

// Mensagem de voz ou arquivo de áudio.
public class Midia
{
    [JsonPropertyName("file_id")] public string? FileId { get; set; }
    [JsonPropertyName("duration")] public int Duration { get; set; }
    [JsonPropertyName("file_size")] public long? FileSize { get; set; }
}

public class Usuario
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("is_bot")] public bool IsBot { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }

    public string NomeExibicao
    {
        get
        {
            var nome = string.Join(' ', new[] { FirstName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return nome.Length > 0 ? nome : Username ?? "Telegram";
        }
    }
}

public class Chat
{
    [JsonPropertyName("id")] public long Id { get; set; }
}
