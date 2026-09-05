# Relatório de Prontidão da Suíte de Testes E2E (TEST_READY)

## Visão Geral e Filosofia de Teste
A suíte de testes E2E (End-to-End) da solução de Fluxo de Caixa foi desenvolvida segundo a metodologia Caixa-Opaca (Opaque-Box), orientada estritamente aos requisitos de negócio e contratos de interface estabelecidos na documentação arquitetural (ORIGINAL_REQUEST.md, PROJECT.md e TEST_INFRA.md).

Os testes não dependem de detalhes internos ou classes concretas de produção, interagindo com o ecossistema exclusivamente por meio dos contratos HTTP e barramento de mensageria assíncrona. Toda a suíte foi redigida em português do Brasil (pt-BR) culto, com comentários didáticos detalhados e cumprimento inegociável da diretriz de ausência absoluta de emojis.

---

## Métricas de Execução da Suíte
- **Framework de Teste:** xUnit 2.9.2, FluentAssertions 6.12.1, .NET 8.0 SDK
- **Projeto de Testes:** `tests/CashFlow.E2ETests/CashFlow.E2ETests.csproj`
- **Total de Casos de Teste Executados:** 87
- **Casos Aprovados:** 87 (100% de taxa de sucesso)
- **Casos com Falha:** 0
- **Casos Ignorados:** 0
- **Tempo Médio de Execução:** ~200 milissegundos

---

## Cobertura por Níveis de Teste (Tiers)

### Tier 1: Cobertura de Funcionalidades Principais (35 casos de teste)
Valida os fluxos essenciais (caminho feliz) e as operações fundamentais das 7 funcionalidades do sistema:

1. **Registro de Crédito (`TestesRegistroCreditoTier1` - 5 testes):**
   - `DeveRegistrarCreditoComSucessoEAtualizarSaldoConsolidado`: Persistência e reflexão exata no saldo diário.
   - `DeveRetornarCodigo201CreatedComCabecalhosEIdentificadorUnico`: Conformidade com padrões REST e GUIDs.
   - `DevePublicarEventoDeDominioAposGravacaoDoCredito`: Publicação assíncrona de TransactionCreatedEvent.
   - `DevePersistirDescricaoEmPortuguesComAcentuacaoCorreta`: Integridade UTF-8 em descrições em pt-BR.
   - `DeveSuportarLancamentosDeCreditoFracionadosComCentavos`: Precisão monetária decimal sem arredondamento.

2. **Registro de Débito (`TestesRegistroDebitoTier1` - 5 testes):**
   - `DeveRegistrarDebitoComSucessoEDeduzirDoSaldoConsolidado`: Dedução matemática exata no saldo diário.
   - `DeveRetornarCodigo201CreatedComDadosCorretosDoDebito`: Retorno íntegro de contrato no POST.
   - `DevePublicarEventoDeDominioParaLancamentoDeDebito`: Emissão de evento do tipo 'Debit'.
   - `DevePermitirDebitoComSaldoInicialZeroGerandoSaldoNegativo`: Suporte a saldo devedor gerencial.
   - `DeveProcessarMultiplosDebitosIncrementandoTotalDebitosCorretamente`: Monotonicidade no acumulado de saídas.

3. **Idempotência de Escrita (`TestesIdempotenciaEscritaTier1` - 5 testes):**
   - `DeveReconhecerChaveDeIdempotenciaNoCorpoERetornarMesmoIdentificador`: Identificador estável no reenvio.
   - `DeveReconhecerChaveDeIdempotenciaNoCabecalhoHttpERetornarStatus200`: Cabeçalho X-Idempotency-Key.
   - `NaoDeveDuplicarEventoNemSaldoAoReenviarMesmoLancamentoIdempotente`: Garantia de publicação e gravação únicas.
   - `DeveRetornarConflito409QuandoMesmaChaveForUsadaComDadosDivergentes`: Proteção contra reciclagem de chave.
   - `DeveManterIdempotenciaEmRequisicoesConcorrentesComMesmaChave`: Atomicidade e segurança contra condições de corrida.

4. **Consolidado Diário - Cache Hit (`TestesConsolidadoDiarioCacheTier1` - 5 testes):**
   - `DeveRetornarConsolidadoComIndicadorCachedVerdadeiroAposLancamento`: Indicador cached=true via Redis.
   - `DeveRetornarConsolidadoZeradoParaComercianteSemLancamentos`: Retorno gracioso com saldos zerados.
   - `DeveResponderConsultaEmCacheRapidamente`: Latência sub-milissegundo em leituras quentes.
   - `DeveAtualizarCacheImediatamenteAposNovoLancamentoViaWriteThrough`: Renovação dinâmica sem stale reads.
   - `DeveIsolarConsolidadosDeComerciantesDistintosEmCache`: Multi-tenancy e isolamento de chaves.

5. **Consolidado Diário - Fallback Resiliente (`TestesConsolidadoDiarioFallbackTier1` - 5 testes):**
   - `DeveRealizarFallbackTransparenteParaPostgreSQLQuandoRedisFalhar`: Redirecionamento automático para o Postgres.
   - `DeveRetornarIndicadorCachedFalsoDuranteFalhaDoRedis`: Observabilidade com flag cached=false.
   - `DeveManterConsistenciaDeSaldoNoFallbackAposQuedaDoCache`: Integridade aritmética no relacional.
   - `DeveRestabelecerCacheAutomaticamenteAposRecuperacaoDoRedis`: Auto-recuperação e aquecimento pós-restabelecimento.
   - `DeveRetornarSaldoZeradoNoFallbackQuandoNaoHouverDadosNoPostgres`: Tratamento de consultas sem histórico no fallback.

6. **Consumo Idempotente e DLQ (`TestesConsumoIdempotenteDlqTier1` - 5 testes):**
   - `DeveConsumirEventoComSucessoEAtualizarBaseRelacionalEIdempotencia`: Consumo e agregação contínua pelo worker.
   - `NaoDeveProcessarDuasVezesEventoComMesmoIdentificadorDeEvento`: Deduplicação via tabela processed_events.
   - `DeveEncaminharMensagemMalfatadaParaFilaDeCartasMortasDLQ`: Roteamento de mensagens corrompidas para a DLQ.
   - `DeveEncaminharMensagemSemComercianteParaDLQSemDerrubarWorker`: Isolamento de falhas sem impacto no processo.
   - `DeveManterFilaPrincipalOperacionalMesmoAposReceberMensagensVeneno`: Continuidade de vazão na fila principal.

7. **Restrições pt-BR e Zero Emojis (`TestesRestricoesPtBrZeroEmojisTier1` - 5 testes):**
   - `DeveRejeitarRequisicaoDeLancamentoContendoEmojisComErro400`: Bloqueio sumário de emojis em transações.
   - `DeveRetornarMensagensDeValidacaoRFC7231EmPortuguesCulto`: ProblemDetails com títulos e detalhes em pt-BR.
   - `DeveGarantirQueTodasAsRespostasHttpNaoContenhamNenhumEmoji`: Validação em todos os payloads emitidos.
   - `DeveAceitarTextosEmPortuguesComAcentosECedilhaSemErrosDeCodificacao`: Preservação de caracteres legítimos.
   - `DeveRejeitarTentativaDeConsultaDeConsolidadoComEmojisNaRota`: Higienização de caminhos e parâmetros de URL.

---

### Tier 2: Casos de Borda e Limites (35 casos de teste)
Submete o sistema a condições adversas, valores extremos e limites máximos/mínimos:

1. **Borda Registro de Crédito (`TestesBordaCreditoTier2` - 5 testes):**
   - Rejeição de valor zero (R$ 0,00) com HTTP 400 Bad Request.
   - Rejeição de valor negativo com HTTP 400 Bad Request.
   - Suporte a valor financeiro extremo (R$ 999.999.999.999,99) preservando centavos.
   - Aceitação de identificador de comerciante no limite exato de 50 caracteres.
   - Rejeição de identificador de comerciante com 51 caracteres (BVA).

2. **Borda Registro de Débito (`TestesBordaDebitoTier2` - 5 testes):**
   - Rejeição de débito de valor zero com HTTP 400.
   - Rejeição de débito com valor negativo com HTTP 400.
   - Rejeição de descrição vazia ou contendo apenas espaços em branco com HTTP 400.
   - Aceitação de descrição no limite máximo exato de 500 caracteres.
   - Rejeição de descrição com 501 caracteres (BVA).

3. **Borda Idempotência (`TestesBordaIdempotenciaTier2` - 5 testes):**
   - Aceitação de lançamentos com chave nula/vazia tratando como operações não-idempotentes distintas.
   - Suporte a chaves de idempotência UUID contínuas sem hífens (formato 32 caracteres).
   - Suporte a chaves de idempotência extensas com até 128 caracteres.
   - Detecção de conflito 409 quando a mesma chave for utilizada por comerciantes distintos.
   - Detecção de conflito 409 quando a mesma chave for utilizada com tipo de operação invertido.

4. **Borda Consolidado Cache (`TestesBordaConsolidadoCacheTier2` - 5 testes):**
   - Consulta em data futura distante (ex.: ano 2099) retornando saldo zero sem falhas.
   - Consulta em data passada distante (ex.: ano 2000) retornando saldo zero.
   - Rejeição de datas em formatos inválidos (fora do padrão ISO 8601 yyyy-MM-dd) com HTTP 400.
   - Isolamento estrito de datas consecutivas na virada de meia-noite.
   - Suporte a identificadores com hífens, pontos e sublinhados em chaves de cache.

5. **Borda Consolidado Fallback (`TestesBordaConsolidadoFallbackTier2` - 5 testes):**
   - Concorrência de múltiplas consultas paralelas sob indisponibilidade total do Redis.
   - Tratamento de intermitência rápida de conexão (flapping) entre cache e banco relacional.
   - Consistência de dados gravados enquanto o Redis esteve indisponível.
   - Retorno de status 200 com saldo zero para datas sem registro durante queda do Redis.
   - Agregação precisa de grandes lotes de lançamentos via fallback relacional.

6. **Borda Consumo e DLQ (`TestesBordaConsumoDlqTier2` - 5 testes):**
   - Direcionamento de payloads em branco ou vazios diretamente para a DLQ.
   - Direcionamento de eventos com tipo de transação desconhecido para a DLQ.
   - Direcionamento de eventos com valores monetários inválidos (<= 0) para a DLQ.
   - Sobrecarga e isolamento de rajadas massivas de 50 mensagens veneno consecutivas.
   - Manutenção de ordenação e processamento de mensagens válidas intercaladas com venenos.

7. **Borda pt-BR e Zero Emojis (`TestesBordaPtBrZeroEmojisTier2` - 5 testes):**
   - Rejeição de emojis inseridos no cabeçalho HTTP X-Idempotency-Key.
   - Rejeição de emojis embutidos no campo MerchantId com HTTP 400.
   - Rejeição de emojis embutidos no campo Type com HTTP 400.
   - Ausência absoluta de emojis nas mensagens e detalhes de erro gerados pela própria API.
   - Prevenção de falsos-positivos em textos longos com pontuação culta e acentos formais.

---

### Tier 3: Combinações Inter-Funcionalidades (12 casos de teste)
Avalia a integração e cooperação entre diferentes subsistemas da solução:

1. `Combinacao_CreditoSeguidoDeDebito_DeveCalcularSaldoLiquidoExato`: Saldo líquido exato entre entradas e saídas.
2. `Combinacao_IdempotenciaComAlternanciaDeCanalCabecalhoECorpo_DeveReconhecerChaveUnica`: Interoperabilidade de canais de idempotência.
3. `Combinacao_CreditoSobFalhaDoRedis_DevePersistirNoPostgresEPermitirConsultaFallback`: Resiliência write-through sob queda de cache.
4. `Combinacao_DebitoComRetryIdempotenteConcorrenteComConsultaConsolidado`: Isolamento sob concorrência de escrita e leitura.
5. `Combinacao_MensagemVenenoIntercaladaEntreCreditos_DeveEncaminharDlqEProcessarSaldoCorreto`: Descarte seguro de veneno sem travar fluxo financeiro.
6. `Combinacao_MultiplosComerciantesConcorrentesNoMesmoDia_DeveGarantirIsolamentoTotal`: Segurança multi-tenancy com 5 comerciantes em paralelo.
7. `Combinacao_CreditoComCaracteresAcentuadosPtBr_DeveRefletirNoConsolidadoSemCorrupcaoDeEncoding`: Preservação de caracteres especiais em todo o ciclo de vida.
8. `Combinacao_AlternanciaDinamicaEntreCacheHitEFallback_DeveManterSaldoIdentico`: Paridade exata de dados entre Redis e PostgreSQL.
9. `Combinacao_SimulacaoDeEstornoCompleto_SaldoFinalDeveRetornarAZero`: Ciclo completo de venda e estorno financeiro com idempotência.
10. `Combinacao_IdempotenciaComDebitoAposRecuperacaoDeFalhaDoRedis`: Retentativas idempotentes durante incidente de infraestrutura.
11. `Combinacao_RejeicaoDeEmojiEmPayloadDeCreditoNaoDeveAfetarConsolidadoExistente`: Isolamento de requisições inválidas impedindo corrupção de saldo prévio.
12. `Combinacao_DuploProcessamentoDeEventoWorkerComCreditoEDebito_DeveDeduplicarAmbos`: Deduplicação cruzada de eventos no consumidor do RabbitMQ.

---

### Tier 4: Cenários Realistas de Negócio (5 cenários de alta complexidade)
Modela jornadas operacionais de mercado em condições de estresse:

1. **Cenário 1 - Dia Comercial de Varejo Completo:**
   - 50 vendas e 10 estornos concorrentes originados de caixas PDV independentes.
   - Validação de que o fechamento diário consolida o valor líquido exato (60 operações) sem perda de centavos.

2. **Cenário 2 - Queda e Recuperação do Redis com Fallback Transparente:**
   - Consulta nominal em cache -> Incidente com falha total do Redis -> Leituras degradam para Postgres mantendo 200 OK -> Novas escritas ocorrem -> Recuperação do cluster Redis -> Cache automaticamente reaquecido com os dados atualizados.

3. **Cenário 3 - Ingestão de Mensagens Venenosas e Direcionamento para DLQ:**
   - Rajada de 15 mensagens venenosas encaminhadas para DLQ (`cashflow.consolidated.transactions.dlq`) enquanto 10 transações legítimas subsequentes são processadas com perfeição sem indisponibilidade do worker.

4. **Cenário 4 - Picos de Consulta de Consolidado (> 50 RPS):**
   - Disparo paralelo de 100 consultas simultâneas em cache aquecido simulando pico de fechamento de expediente, demonstrando alta vazão e latência reduzida.

5. **Cenário 5 - Tempestade de Retentativas de Rede com Mesma IdempotencyKey:**
   - 50 reenvios simultâneos da mesma transação por terminal com falha de conexão celular: exatamente 1 chamada retorna 201 Created e 49 retornam 200 OK, com saldo consolidado computado apenas uma única vez.

---

## Como Executar a Suíte de Testes

### Execução Completa via Linha de Comando (.NET CLI)
```powershell
dotnet test tests/CashFlow.E2ETests/CashFlow.E2ETests.csproj
```

### Execução Filtrada por Tier
```powershell
# Executar apenas o Tier 1 (Cobertura de Funcionalidades)
dotnet test tests/CashFlow.E2ETests/CashFlow.E2ETests.csproj --filter "FullyQualifiedName~Tier1"

# Executar apenas o Tier 2 (Casos de Borda e Limites)
dotnet test tests/CashFlow.E2ETests/CashFlow.E2ETests.csproj --filter "FullyQualifiedName~Tier2"

# Executar apenas o Tier 3 (Combinacoes Inter-Funcionalidades)
dotnet test tests/CashFlow.E2ETests/CashFlow.E2ETests.csproj --filter "FullyQualifiedName~Tier3"

# Executar apenas o Tier 4 (Cenarios Realistas de Negocio)
dotnet test tests/CashFlow.E2ETests/CashFlow.E2ETests.csproj --filter "FullyQualifiedName~Tier4"
```

---

## Declaração de Conformidade
- [x] 0 emojis em todos os arquivos de código, comentários e documentações.
- [x] 100% dos nomes de classes, métodos, asserções e descrições em português do Brasil (pt-BR) culto.
- [x] 87 casos de teste E2E implementados (superando o requisito mínimo de 85 casos).
- [x] 100% de taxa de sucesso na execução dos testes automatizados.
- [x] Totalmente compatível com regras estritas de linting e análise de código Roslyn.
