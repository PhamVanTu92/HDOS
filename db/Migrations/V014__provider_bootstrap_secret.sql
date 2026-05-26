-- V014: Store encrypted plaintext secret + bootstrap token for providers.
-- bootstrap_token  : plaintext token used by provider to fetch its clientSecret on startup
-- client_secret_enc: AES-encrypted (DataProtection) plaintext of the current clientSecret

ALTER TABLE provider_registry
    ADD COLUMN IF NOT EXISTS client_secret_enc  TEXT,
    ADD COLUMN IF NOT EXISTS bootstrap_token    TEXT;
