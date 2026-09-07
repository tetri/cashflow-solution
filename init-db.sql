-- =========================================================================
-- Esquema de Banco de Dados Relacional - CashFlow (PostgreSQL 16)
-- Governanca de Seguranca: Principio do Menor Privilegio (Least Privilege / OWASP)
-- =========================================================================

-- 1. Tabelas do Dominio Financeiro
CREATE TABLE IF NOT EXISTS transactions (
    id UUID PRIMARY KEY,
    merchant_id VARCHAR(50) NOT NULL,
    amount DECIMAL(18, 2) NOT NULL,
    type VARCHAR(10) NOT NULL, -- 'Credit' ou 'Debit'
    description VARCHAR(500) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    processed_at TIMESTAMP WITH TIME ZONE,
    idempotency_key VARCHAR(128)
);

CREATE INDEX IF NOT EXISTS idx_transactions_merchant_date ON transactions (merchant_id, created_at);
CREATE UNIQUE INDEX IF NOT EXISTS idx_transactions_idempotency_key ON transactions (idempotency_key) WHERE idempotency_key IS NOT NULL;

CREATE TABLE IF NOT EXISTS daily_consolidated (
    merchant_id VARCHAR(50) NOT NULL,
    date DATE NOT NULL,
    total_credits DECIMAL(18, 2) NOT NULL DEFAULT 0.00,
    total_debits DECIMAL(18, 2) NOT NULL DEFAULT 0.00,
    closing_balance DECIMAL(18, 2) NOT NULL DEFAULT 0.00,
    transaction_count INT NOT NULL DEFAULT 0,
    last_updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    version INT NOT NULL DEFAULT 1,
    PRIMARY KEY (merchant_id, date)
);

CREATE TABLE IF NOT EXISTS processed_events (
    event_id UUID PRIMARY KEY,
    event_type VARCHAR(100) NOT NULL,
    processed_at TIMESTAMP WITH TIME ZONE NOT NULL
);

-- =========================================================================
-- 2. Segregação de Privilégios (Write-Side vs Read-Side)
-- =========================================================================

-- Criacao do usuario de Escrita (Write-Side: Transactions API e Consolidated Worker)
-- Permissoes restritas: conexao, leitura, insercao e atualizacao nas tabelas operacionais
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'cashflow_writer') THEN
        CREATE USER cashflow_writer WITH PASSWORD 'writer_secret_pass_local';
    END IF;
END
$$;

GRANT CONNECT ON DATABASE cashflow_db TO cashflow_writer;
GRANT USAGE ON SCHEMA public TO cashflow_writer;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO cashflow_writer;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO cashflow_writer;

-- Criacao do usuario de Leitura Estrita (Read-Side: Consolidated API)
-- Permissoes maximamente restritas: conexao e SELECT exclusivamente na tabela daily_consolidated
-- Nenhuma permissao de INSERT, UPDATE, DELETE ou acesso a tabelas sensiveis de transacao
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'cashflow_reader') THEN
        CREATE USER cashflow_reader WITH PASSWORD 'reader_secret_pass_local';
    END IF;
END
$$;

GRANT CONNECT ON DATABASE cashflow_db TO cashflow_reader;
GRANT USAGE ON SCHEMA public TO cashflow_reader;
GRANT SELECT ON TABLE daily_consolidated TO cashflow_reader;
REVOKE ALL ON TABLE transactions FROM cashflow_reader;
REVOKE ALL ON TABLE processed_events FROM cashflow_reader;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO cashflow_reader;
