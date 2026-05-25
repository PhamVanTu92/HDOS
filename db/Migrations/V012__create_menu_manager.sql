-- V012: Menu Manager — 4 tables: menu_nodes, report_screens, screen_widgets, menu_permissions

CREATE TABLE menu_nodes (
    id          UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    name        VARCHAR(200) NOT NULL,
    slug        VARCHAR(200) NOT NULL UNIQUE,
    icon        VARCHAR(50)  NOT NULL DEFAULT '📊',
    description TEXT,
    parent_id   UUID REFERENCES menu_nodes(id) ON DELETE CASCADE,
    sort_order  INT         NOT NULL DEFAULT 0,
    is_visible  BOOLEAN     NOT NULL DEFAULT true,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE report_screens (
    id          UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    menu_id     UUID        NOT NULL REFERENCES menu_nodes(id) ON DELETE CASCADE,
    name        VARCHAR(200) NOT NULL,
    icon        VARCHAR(50)  NOT NULL DEFAULT '📊',
    status      VARCHAR(20)  NOT NULL DEFAULT 'draft' CONSTRAINT chk_screen_status CHECK (status IN ('draft', 'published')),
    sort_order  INT         NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE screen_widgets (
    id          UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    screen_id   UUID        NOT NULL REFERENCES report_screens(id) ON DELETE CASCADE,
    widget_type VARCHAR(20)  NOT NULL CONSTRAINT chk_widget_type CHECK (widget_type IN ('kpi','line','bar','pie','table','text')),
    title       VARCHAR(200) NOT NULL DEFAULT 'Widget',
    col_span    INT         NOT NULL DEFAULT 6,
    sort_order  INT         NOT NULL DEFAULT 0,
    color       VARCHAR(20)  NOT NULL DEFAULT '#4f46e5',
    data_source VARCHAR(200),
    config      JSONB       NOT NULL DEFAULT '{}',
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE menu_permissions (
    id              UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    menu_id         UUID        NOT NULL REFERENCES menu_nodes(id) ON DELETE CASCADE,
    principal_type  VARCHAR(10)  NOT NULL CHECK (principal_type IN ('role','user')),
    principal_value VARCHAR(200) NOT NULL,
    can_view        BOOLEAN     NOT NULL DEFAULT true,
    can_export      BOOLEAN     NOT NULL DEFAULT false,
    UNIQUE (menu_id, principal_type, principal_value)
);

CREATE INDEX ix_menu_nodes_parent      ON menu_nodes(parent_id);
CREATE INDEX ix_report_screens_menu    ON report_screens(menu_id, sort_order);
CREATE INDEX ix_screen_widgets_screen  ON screen_widgets(screen_id, sort_order);
CREATE INDEX ix_menu_permissions_menu  ON menu_permissions(menu_id);
