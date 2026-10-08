using Azure;
using Azure.Data.Tables;

namespace BotIdeias.Ideias;

// Uma ideia guardada no Table Storage. PartitionKey = id do chat; RowKey = número com zeros à esquerda ("0007"),
// o que mantém a ordem de registro. O texto original nunca é alterado depois de gravado.
public class IdeiaEntidade : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public int Numero { get; set; }
    public string Texto { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Resumo { get; set; } = string.Empty;
    public string Tema { get; set; } = "Sem classificação";
    public string Modulo { get; set; } = string.Empty;
    public string Prioridade { get; set; } = "Média";
    public string Autor { get; set; } = string.Empty;
    public string SemelhanteA { get; set; } = string.Empty;
    public DateTime CriadaEmUtc { get; set; }

    public string Codigo => CodigoDe(Numero);

    public static string CodigoDe(int numero) => $"IDEIA-{numero:D4}";
    public static string ChaveDe(int numero) => numero.ToString("D6");
}

// Linha especial de controle de cada chat (RowKey "_meta"): contador, mensagem do painel e último update tratado.
public class MetaEntidade : ITableEntity
{
    public const string Chave = "_meta";

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = Chave;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public int Contador { get; set; }
    public long PainelMensagemId { get; set; }
    public long UltimoUpdateId { get; set; }
}
