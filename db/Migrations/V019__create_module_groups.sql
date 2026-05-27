-- V019: module_groups — top-level sidebar groups (e.g. "Điều hành", "Lâm sàng").
-- Groups hold modules. Visibility and ordering are configurable.

CREATE TABLE module_groups (
    id           UUID         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    slug         VARCHAR(100) NOT NULL UNIQUE,
    label        VARCHAR(200) NOT NULL,
    icon         VARCHAR(50),
    sort_order   INT          NOT NULL DEFAULT 0,
    is_visible   BOOLEAN      NOT NULL DEFAULT true,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE module_groups IS
    'Top-level sidebar navigation groups. Each group contains one or more modules. '
    'Rendered in order of sort_order. Hidden groups (is_visible=false) are not sent to clients.';

CREATE INDEX ix_module_groups_order ON module_groups(sort_order) WHERE is_visible = true;
