# Bot de Ideias (Telegram)

Bot **só de Telegram**, em repositório próprio, independente do app de SST (outro projeto, outro deploy, outra memória). Você escreve uma ideia no
chat; o bot registra, **analisa com IA (Azure OpenAI)**, responde com o resumo e **reorganiza o painel fixado** no chat,
agrupando as ideias por tema.

```
Você → Telegram → webhook (/telegram) → Azure OpenAI (analisa e reorganiza) → Table Storage (memória) → painel fixado
```

## O que o bot faz
- Cada mensagem com mais de 15 caracteres vira uma ideia (`IDEIA-0001`, `0002`...). O texto original nunca é alterado.
- A IA devolve título, resumo, módulo, tema, prioridade sugerida, perguntas (se faltar algo) e se parece com outra ideia.
- A cada ideia, a IA **reagrupa todas** em temas e o painel (mensagem fixada) é reescrito.
- Comandos: `/painel` (reenvia e fixa o painel), `/ideia IDEIA-0001` (detalhes), `/ajuda`.
- Se o Azure OpenAI falhar ou não estiver configurado, a ideia **é registrada mesmo assim**, sem classificação.

## Segurança
- O webhook só aceita chamadas com o cabeçalho `X-Telegram-Bot-Api-Secret-Token` correto **e** de chats em `Telegram__ChatIds`.
  Sem os dois configurados, rejeita tudo.
- Nenhum segredo no código: tudo por variáveis de ambiente do Container App.
- O bot não acessa o banco nem a API do SST.

## Variáveis de ambiente
| Variável | O que é |
|---|---|
| `Telegram__BotToken` | token do bot (BotFather) — segredo |
| `Telegram__WebhookSecret` | texto aleatório longo, o mesmo do `setWebhook` — segredo |
| `Telegram__ChatIds` | ids dos chats autorizados, separados por vírgula (privado = id do usuário; grupo = número negativo) |
| `AzureOpenAI__Endpoint` | ex.: `https://oai-gpol-hml-27207f.openai.azure.com` |
| `AzureOpenAI__ApiKey` | chave do recurso — segredo |
| `AzureOpenAI__Deployment` | nome do deployment do modelo (aqui: `gpt5mini`, modelo gpt-5.4-mini) |
| `AzureOpenAI__ApiVersion` | opcional; padrão `2025-04-01-preview` |
| `Storage__ConnectionString` | conexão da conta de armazenamento do bot — segredo |

## Criar os recursos no Azure (PowerShell, uma vez)
Ajuste os nomes se quiser. `$rg` é o grupo onde o bot vai ficar.
```powershell
$rg   = "rg-gnezis-hub-staging"
$loc  = "brazilsouth"
$app  = "bot-ideias-hml"
$sa   = "stbotideiashml"            # 3-24 letras minúsculas/números, único no Azure

# 1) memória do bot (Table Storage) — conta pequena e só dele
az storage account create -n $sa -g $rg -l $loc --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 --allow-blob-public-access false
$conn = az storage account show-connection-string -n $sa -g $rg --query connectionString -o tsv
```
A IA usa o recurso OpenAI que já existe (`oai-gpol-hml-27207f`, East US 2). Veja os modelos que ele já tem:
```powershell
az cognitiveservices account deployment list -n oai-gpol-hml-27207f -g rg-gpoliticas --query "[].{deployment:name, modelo:properties.model.name, versao:properties.model.version}" -o table
```
Use o nome de um deployment de chat pequeno (tipo `gpt-4o-mini`). Se não houver nenhum, crie um novo no portal
(Azure AI Foundry → Implantações) — não precisa de nenhuma outra liberação.

## Subir o bot
O deploy automático (`.github/workflows/deploy.yml`) só roda se a variável de repositório `CONTAINERAPP_BOT_IDEIAS` existir.
Primeira vez, crie o Container App com qualquer imagem e depois deixe o workflow atualizar; ou use `az acr build` + `az containerapp create`
apontando para o `Dockerfile` da raiz.

## Deploy automático: credencial do GitHub no Azure (uma vez)
O deploy usa login OIDC (sem senha guardada). A credencial federada do SST vale só para o repositório do SST; para este é preciso
criar uma nova. Portal do Azure → Microsoft Entra ID → App registrations → o app cujo ID é o `AZURE_CLIENT_ID` →
Certificates & secrets → **Federated credentials** → Add credential → *GitHub Actions deploying Azure resources*:
organização `AAHBRANT`, repositório `TELEGRAM-IDEIAS`, entidade **Branch**, branch `main`.
Depois, neste repositório, crie os secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` e as variáveis
`AZURE_RESOURCE_GROUP`, `AZURE_ACR_NAME` e `CONTAINERAPP_BOT_IDEIAS`.

## Registrar o webhook (depois que o app estiver no ar)
```powershell
Invoke-RestMethod -Method Post -Uri "https://api.telegram.org/bot$token/setWebhook" `
  -Body @{ url = "https://<URL-DO-APP>/telegram"; secret_token = $segredo; allowed_updates = '["message"]' }
```
Em grupo: desative a privacidade do bot no BotFather (`/mybots` → Bot Settings → Group Privacy → Turn off) e, para o painel ser
fixado, deixe o bot como administrador.

## Limites conhecidos
- A IA recebe as **últimas 120 ideias** como contexto; o painel mostra até 400.
- Sem edição/exclusão de ideias pelo bot (por ora).
- Não lê fotos nem arquivos; só texto (e legenda).
- Painel e respostas são texto simples (sem formatação).

## Áudios (opcional)

Crie um deployment de transcrição no mesmo recurso Azure OpenAI (ex.: `gpt-4o-transcribe`) e defina
`AzureOpenAI__DeploymentTranscricao` com o nome dele. Sem essa variável, o bot avisa que não ouve áudios.
Limite: 3 minutos / 20 MB por áudio.
