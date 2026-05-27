-- V021: module_tabs and widgets — layout engine for config-driven modules.
-- Each module has tabs; each tab has widgets arranged on a react-grid-layout canvas.

CREATE TABLE module_tabs (
    id          UUID         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    module_id   UUID         NOT NULL REFERENCES modules(id) ON DELETE CASCADE,
    slug        VARCHAR(100) NOT NULL,
    label       VARCHAR(200) NOT NULL,
    sort_order  INT          NOT NULL DEFAULT 0,
    is_default  BOOLEAN      NOT NULL DEFAULT false,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE(module_id, slug)
);

COMMENT ON TABLE module_tabs IS
    'Tabs within a module page. One tab = one react-grid-layout canvas. '
    'is_default=true tab is shown on initial load. Only one default per module.';

CREATE INDEX ix_module_tabs_module ON module_tabs(module_id, sort_order);


CREATE TABLE widgets (
    id                UUID          NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    tab_id            UUID          NOT NULL REFERENCES module_tabs(id) ON DELETE CASCADE,

    -- Identity
    widget_key        VARCHAR(100)  NOT NULL,
    title             VARCHAR(200),
    subtitle          VARCHAR(500),

    -- Chart type (references widget_type_catalog)
    chart_type        VARCHAR(50)   NOT NULL,

    -- react-grid-layout position (12-column grid)
    grid_x            INT           NOT NULL DEFAULT 0,
    grid_y            INT           NOT NULL DEFAULT 0,
    grid_w            INT           NOT NULL DEFAULT 6,
    grid_h            INT           NOT NULL DEFAULT 4,

    -- Data source — which operation powers this widget
    -- NULL for layout-only widgets (text_widget, tab_container)
    operation_pattern VARCHAR(200)  NULL,
    provider_id       VARCHAR(200)  NULL,

    -- Template params with {{token}} substitution (see RENDER_CONTRACTS.md §6.3)
    -- e.g. {"date": "{{today}}", "ward": "{{filters.ward}}"}
    params_template   JSONB         NOT NULL DEFAULT '{}',

    -- Visual configuration passed through to frontend (colors, formats, axis labels)
    visual_config     JSONB         NOT NULL DEFAULT '{}',

    -- Which dashboard filter keys this widget subscribes to
    -- Empty = subscribes to all filters on the page
    filter_bindings   TEXT[]        NOT NULL DEFAULT '{}',

    -- Drill-down interactions (see RENDER_CONTRACTS.md §6)
    interactions      JSONB         NOT NULL DEFAULT '{}',

    -- For filter_* widgets: the filter key this widget controls
    filter_key        VARCHAR(100)  NULL,

    is_visible        BOOLEAN       NOT NULL DEFAULT true,
    sort_order        INT           NOT NULL DEFAULT 0,
    created_at        TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ   NOT NULL DEFAULT NOW(),

    UNIQUE(tab_id, widget_key)
);

COMMENT ON TABLE widgets IS
    'Individual widgets on a module tab canvas. '
    'grid_x/y/w/h define react-grid-layout position (12-column grid). '
    'operation_pattern links to operation_registry for live data. '
    'params_template supports {{today}}, {{currentMonth}}, {{userId}}, {{filters.key}} tokens.';

CREATE INDEX ix_widgets_tab       ON widgets(tab_id, sort_order);
CREATE INDEX ix_widgets_operation ON widgets(operation_pattern) WHERE operation_pattern IS NOT NULL;
CREATE INDEX ix_widgets_visible   ON widgets(tab_id, is_visible) WHERE is_visible = true;
