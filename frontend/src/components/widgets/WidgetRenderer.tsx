import type { WidgetChartType } from '../../types/module';
import { KpiGridWidget }          from './KpiGridWidget';
import { KpiWidget }              from './KpiWidget';
import { ProgressRowsWidget }     from './ProgressRowsWidget';
import { AlertListWidget }        from './AlertListWidget';
import { FlowStepsWidget }        from './FlowStepsWidget';
import { PatientFlowWidget }      from './PatientFlowWidget';
import { RiskTiersWidget }        from './RiskTiersWidget';
import { LineChartWidget }        from './LineChartWidget';
import { PieChartWidget }         from './PieChartWidget';
import { SimpleTableWidget }      from './SimpleTableWidget';
import { RawJsonWidget }          from './RawJsonWidget';
import { BedGridWidget }          from './BedGridWidget';
import { RoomStatusGridWidget }   from './RoomStatusGridWidget';
import { TimelineVerticalWidget } from './TimelineVerticalWidget';
import { News2BarsWidget }        from './News2BarsWidget';
import { MapPinsWidget }          from './MapPinsWidget';
import { GaugeWidget }            from './GaugeWidget';
import { ChatPanelWidget }        from './ChatPanelWidget';
import { FilterDropdownWidget }   from './FilterDropdownWidget';
import { FilterDateRangeWidget }  from './FilterDateRangeWidget';
import { FilterSliderWidget }     from './FilterSliderWidget';
import { FilterSearchWidget }     from './FilterSearchWidget';

/** Set of chart types that are filter controls, not data visualizations. */
export const FILTER_CHART_TYPES = new Set<string>([
  'filter_dropdown',
  'filter_date_range',
  'filter_slider',
  'filter_search',
]);

export interface FilterProps {
  filterKey:      string;
  currentFilters: Record<string, unknown>;
  onFilterChange: (key: string, value: unknown) => void;
}

interface WidgetRendererProps {
  chartType:    WidgetChartType;
  data:         unknown;
  visualConfig?: string;
  /** Defined only for filter-type widgets */
  filter?: FilterProps;
}

export function WidgetRenderer({ chartType, data, visualConfig, filter }: WidgetRendererProps) {
  switch (chartType) {
    // ── Filter controls ───────────────────────────────────────────────────
    case 'filter_dropdown':
      return filter
        ? <FilterDropdownWidget  data={data} visualConfig={visualConfig} {...filter} />
        : <RawJsonWidget data={data} chartType={chartType} />;

    case 'filter_date_range':
      return filter
        ? <FilterDateRangeWidget data={data} visualConfig={visualConfig} {...filter} />
        : <RawJsonWidget data={data} chartType={chartType} />;

    case 'filter_slider':
      return filter
        ? <FilterSliderWidget    data={data} visualConfig={visualConfig} {...filter} />
        : <RawJsonWidget data={data} chartType={chartType} />;

    case 'filter_search':
      return filter
        ? <FilterSearchWidget    data={data} visualConfig={visualConfig} {...filter} />
        : <RawJsonWidget data={data} chartType={chartType} />;

    // ── Healthcare ────────────────────────────────────────────────────────
    case 'kpi_grid':              return <KpiGridWidget data={data} />;
    case 'progress_rows':         return <ProgressRowsWidget data={data} />;
    case 'alert_list':            return <AlertListWidget data={data} />;
    case 'flow_steps':            return <FlowStepsWidget data={data} />;
    case 'patient_flow_stages':   return <PatientFlowWidget data={data} />;
    case 'risk_tiers':            return <RiskTiersWidget data={data} />;
    case 'bed_grid':              return <BedGridWidget data={data} />;
    case 'room_status_grid':      return <RoomStatusGridWidget data={data} />;
    case 'timeline_vertical':     return <TimelineVerticalWidget data={data} />;
    case 'news2_bars':            return <News2BarsWidget data={data} />;
    case 'map_pins':              return <MapPinsWidget data={data} />;

    // ── AI ────────────────────────────────────────────────────────────────
    case 'chat_panel':            return <ChatPanelWidget data={data} />;

    // ── Single KPI / Gauge ───────────────────────────────────────────────
    case 'kpi':                   return <KpiWidget data={data} />;
    case 'gauge':                 return <GaugeWidget data={data} />;

    // ── Time series / bar / area ─────────────────────────────────────────
    case 'line_chart':
    case 'bar_chart':
    case 'area_chart':
      return <LineChartWidget data={data} chartType={chartType} />;

    // ── Pie / donut ───────────────────────────────────────────────────────
    case 'pie_chart':
    case 'donut_chart':
      return <PieChartWidget data={data} chartType={chartType} />;

    // ── Tables ────────────────────────────────────────────────────────────
    case 'simple_table':
    case 'advanced_table':
      return <SimpleTableWidget data={data} />;

    // ── Fallback ─────────────────────────────────────────────────────────
    default:
      return <RawJsonWidget data={data} chartType={chartType} />;
  }
}
