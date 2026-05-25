-- Add auto-refresh configuration to report_screens
-- refresh_mode: 'none' (off) | 'timer' (poll every N seconds) | 'sse' (server-push via WidgetStale)
-- refresh_interval_s: seconds between polls when refresh_mode = 'timer' (ignored otherwise)

ALTER TABLE report_screens
  ADD COLUMN IF NOT EXISTS refresh_mode       VARCHAR(10) NOT NULL DEFAULT 'none'
    CONSTRAINT chk_refresh_mode CHECK (refresh_mode IN ('none', 'timer', 'sse')),
  ADD COLUMN IF NOT EXISTS refresh_interval_s INT         NOT NULL DEFAULT 0;

COMMENT ON COLUMN report_screens.refresh_mode       IS 'none=off, timer=poll, sse=server-push';
COMMENT ON COLUMN report_screens.refresh_interval_s IS 'seconds between polls; used when refresh_mode=timer';
