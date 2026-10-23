# AGENTS.md — TaskWorker

## Contexto

Projeto de portfólio: Worker Service independente para processamento assíncrono de tarefas, com fila persistida no PostgreSQL e evolução opcional para RabbitMQ. Leia o `README.md` antes de qualquer alteração. O status atual é **versão PostgreSQL implementada**; RabbitMQ permanece como evolução opcional.

## Regras de escopo

- Entregar primeiro a menor versão funcional; só adicionar tecnologia quando houver requisito que a justifique.
- Manter o escopo funcional do README; funcionalidades novas exigem registro prévio e justificativa.
- RabbitMQ é evolução opcional e só entra depois que a versão PostgreSQL estiver estável.
- **Não adicionar Kafka** a este projeto.
- Não criar abstrações ou camadas sem necessidade real.

## Critérios técnicos obrigatórios

- Estados explícitos de tarefa: Pendente, EmProcessamento, Concluída e FalhaDefinitiva (ou equivalente documentado).
- Aquisição segura de tarefas: múltiplos workers nunca podem pegar a mesma tarefa ao mesmo tempo (ex.: `FOR UPDATE SKIP LOCKED` ou atualização condicional).
- Retentativas limitadas e configuráveis, com backoff crescente.
- Distinguir falhas transitórias (retentar) de permanentes (falha definitiva).
- Processamento idempotente: reprocessar uma tarefa não pode gerar efeito duplicado.
- Usar `CancellationToken` em todo o pipeline e encerrar graciosamente sem corromper estado.
- Persistir estado, data de criação, tentativas e erro mais recente.
- Logs estruturados com Serilog contendo identificador da tarefa, estado, duração e motivo da falha.
- Nunca registrar segredos, tokens ou dados pessoais/sensíveis nos logs.
- Segredos apenas via variáveis de ambiente ou User Secrets; nunca versionar chaves, senhas ou strings de conexão reais.

## Convenções do repositório

- Registrar no README a versão do .NET utilizada (uma versão suportada/LTS).
- C# com nullable reference types habilitado e avisos tratados como erro quando viável.
- Commits pequenos e descritivos; uma mudança lógica por commit.
- Testes em xUnit; testes de integração com PostgreSQL real (Testcontainers recomendado).
- PostgreSQL local via Docker Compose; worker executável em container.
- CI (GitHub Actions) deve compilar e rodar todos os testes.
- Comandos canônicos esperados: `dotnet restore`, `dotnet build`, `dotnet test` e `docker compose up -d`.

## Critérios de teste

- Cobrir todos os cenários da seção "Testes essenciais" do README.
- Teste de concorrência deve executar dois workers reais (ou instâncias equivalentes) disputando a mesma fila.
- Testar backoff, limite de tentativas e transição para FalhaDefinitiva.
- Testar encerramento gracioso durante o processamento.
- Um único comando deve executar a suíte completa.

## Definition of Done

- Build e testes passam localmente e na CI.
- Worker roda como processo independente via Docker, processa lotes e tolera falhas isoladas.
- Testes comprovam que dois workers não processam a mesma tarefa simultaneamente.
- Logs permitem reconstruir o ciclo de vida de uma tarefa.
- README atualizado com execução, decisões técnicas e limitações conhecidas.
- Nenhum segredo ou dado sensível versionado ou logado.
- RabbitMQ (se implementado) com acknowledgements, retentativas e dead-letter queue documentados.

## Proibido

- Permitir que dois workers adquiram a mesma tarefa.
- Reprocessar tarefas sem idempotência.
- Engolir exceções sem registrar estado e motivo da falha.
- Logar segredos, tokens ou dados sensíveis.
- Adicionar Kafka ou outras tecnologias fora do escopo sem requisito documentado.
- Commitar segredos, senhas ou arquivos `.env` reais.
