-- V015: Extend provider_credentials_audit action constraint to include
--       'register' (AdminProvidersController.RegisterAsync) and
--       'set_secret' (AdminProvidersController.SetSecretAsync).

ALTER TABLE provider_credentials_audit
    DROP CONSTRAINT IF EXISTS provider_credentials_audit_action_check;

ALTER TABLE provider_credentials_audit
    ADD CONSTRAINT provider_credentials_audit_action_check
    CHECK (action IN ('rotate', 'revoke', 'issue', 'probe', 'register', 'set_secret'));
