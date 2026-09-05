-- Cashflow Database Schema

CREATE TABLE IF NOT EXISTS transactions (
    id UUID PRIMARY KEY,
    merchant_id VARCHAR(50) NOT NULL,
    amount DECIMAL(18, 2) NOT NULL,
    type VARCHAR(10) NOT NULL, -- 'Credit' or 'Debit'
    description VARCHAR(255) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    processed_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX IF NOT EXISTS idx_transactions_merchant_date ON transactions (merchant_id, created_at);

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
