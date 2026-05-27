-- V020: modules — individual sidebar items within a group.
-- Each module maps to a route /m/:slug and has its own tab/widget layout.

CREATE TABLE modules (
    id             UUID          NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    group_id       UUID          NOT NULL REFERENCES module_groups(id) ON DELETE CASCADE,
    slug           VARCHAR(100)  NOT NULL UNIQUE,
    label          VARCHAR(200)  NOT NULL,
    icon           VARCHAR(50),
    description    TEXT,
    -- Access control: NULL = all authenticated users; array = must have one of these realm roles
    required_roles VARCHAR(50)[] NULL,
    sort_order     INT           NOT NULL DEFAULT 0,
    is_visible     BOOLEAN       NOT NULL DEFAULT true,
    is_active      BOOLEAN       NOT NULL DEFAULT true,
    -- Refresh interval in seconds for auto-reload; NULL = manual refresh only
    refresh_interval_seconds INT NULL,
    created_at     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at     TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE modules IS
    'Config-driven sidebar modules. One module = one page at /m/:slug. '
    'Layout (tabs + widgets) is defined in module_tabs and widgets tables. '
    'required_roles: array of Keycloak realm roles. NULL = open to all authenticated users.';

CREATE INDEX ix_modules_group   ON modules(group_id, sort_order) WHERE is_visible = true;
CREATE INDEX ix_modules_slug    ON modules(slug);
CREATE INDEX ix_modules_active  ON modules(is_active) WHERE is_active = true;
