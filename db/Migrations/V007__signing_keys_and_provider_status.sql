-- V007: JWT signing key infrastructure + provider_registry extensions + audit action expansion
-- Phase 8 — Provider Bridge + JWT Authentication
-- Applied: 2026-05-19

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. signing_keys table
--    Stores RSA-2048 key pairs used to sign provider JWTs.
--    Private key is encrypted with ASP.NET Data Protection before storage.
--    Public key (SPKI DER bytes) is exported to the JWKS endpoint.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS signing_keys (
    id                    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- kid header value in issued JWTs. UUID v7 string for chronological ordering.
    key_id                TEXT        NOT NULL,

    algorithm             TEXT        NOT NULL DEFAULT 'RS256',

    -- RSA private key in PKCS#8 DER format, encrypted with ASP.NET Data Protection.
    -- NEVER returned by any API endpoint. Only read by the process that issues tokens.
    private_key_encrypted BYTEA       NOT NULL,

    -- SubjectPublicKeyInfo DER bytes. Exported to JWKS endpoint as n/e components.
    -- Safe to cache in-memory; safe to serve publicly.
    public_key_spki       BYTEA       NOT NULL,

    status                TEXT        NOT NULL DEFAULT 'active'
                              CHECK (status IN ('active', 'retired', 'revoked')),

    created_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- NULL = key lives until manually retired.
    -- Set on rotation: old key retires_at = NOW() + '60 minutes'.
    retires_at            TIMESTAMPTZ,

    retired_at            TIMESTAMPTZ,

    CONSTRAINT uq_signing_key_id UNIQUE (key_id)
);

CREATE INDEX idx_signing_keys_status ON signing_keys (status, created_at DESC);

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. provider_registry — expand status constraint to include 'credentials_revoked'
-- ─────────────────────────────────────────────────────────────────────────────

-- PostgreSQL generates the constraint name as provider_registry_status_check
-- for an unnamed CHECK constraint on the status column.
ALTER TABLE provider_registry
    DROP CONSTRAINT IF EXISTS provider_registry_status_check;

ALTER TABLE provider_registry
    ADD CONSTRAINT provider_registry_status_check
    CHECK (status IN ('active', 'suspended', 'maintenance', 'credentials_revoked'));

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. provider_registry — rotation grace period columns
--    During the 60-second grace window after credential rotation, both the new
--    hash (client_secret_hash) and the old hash (pending_client_secret_hash) are
--    accepted by ValidateCredentialsAsync. pending_secret_expires_at guards the
--    grace window; it is checked BEFORE BCrypt.Verify is called on the pending hash.
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE provider_registry
    ADD COLUMN IF NOT EXISTS pending_client_secret_hash TEXT,
    ADD COLUMN IF NOT EXISTS pending_secret_expires_at  TIMESTAMPTZ;

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. provider_registry — max_concurrent_requests column (OQ-B resolution)
--    Default 8. Copied to Welcome.max_concurrent_requests; RabbitMQ prefetch set
--    to this value per session in ProviderRequestConsumer.
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE provider_registry
    ADD COLUMN IF NOT EXISTS max_concurrent_requests INT NOT NULL DEFAULT 8;

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. provider_credentials_audit — expand action constraint to include 'probe'
--    'probe' is logged when POST /api/v1/admin/providers/{id}/probe is called.
--    Probe JTI is recorded so probe activity is visible in the audit trail
--    separately from real token issuances (action = 'issue').
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE provider_credentials_audit
    DROP CONSTRAINT IF EXISTS provider_credentials_audit_action_check;

ALTER TABLE provider_credentials_audit
    ADD CONSTRAINT provider_credentials_audit_action_check
    CHECK (action IN ('rotate', 'revoke', 'issue', 'probe'));
