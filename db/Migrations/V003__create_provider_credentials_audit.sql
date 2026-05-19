CREATE TABLE IF NOT EXISTS provider_credentials_audit (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider_id  TEXT        NOT NULL,
    action       TEXT        NOT NULL
                     CHECK (action IN ('rotate', 'revoke', 'issue')),
    jti          TEXT,
    performed_by TEXT        NOT NULL,
    at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT fk_audit_provider
        FOREIGN KEY (provider_id) REFERENCES provider_registry (provider_id)
        ON DELETE CASCADE
);

CREATE INDEX idx_cred_audit_provider ON provider_credentials_audit (provider_id, at DESC);
