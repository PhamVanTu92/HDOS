export interface MenuSummary {
  id: string;
  name: string;
  slug: string;
  icon: string;
  description: string | null;
  parentId: string | null;
  sortOrder: number;
}

export interface ScreenSummary {
  id: string;
  name: string;
  icon: string;
  sortOrder: number;
}

export interface MenuDetail extends MenuSummary {
  screens: ScreenSummary[];
}

export interface WidgetDef {
  id: string;
  widgetType: 'kpi' | 'line' | 'bar' | 'pie' | 'table' | 'text';
  title: string;
  colSpan: number;
  sortOrder: number;
  color: string;
  dataSource: string | null;
  config: string; // raw JSON string
}

export interface WidgetConfig {
  operation?: string;
  params?: Record<string, unknown>;
  xField?: string;
  yField?: string;
  valField?: string;
  trendField?: string;
  catField?: string;
  cols?: string[];
}

export interface ScreenDetail {
  screenId: string;
  name: string;
  icon: string;
  menuId: string;
  menuName: string;
  menuSlug: string;
  widgets: WidgetDef[];
}

// Admin types
export interface AdminMenuNode {
  id: string;
  name: string;
  slug: string;
  icon: string;
  description: string | null;
  parentId: string | null;
  sortOrder: number;
  isVisible: boolean;
  createdAt: string;
  updatedAt: string;
  screenCount: number;
}

export interface AdminScreen {
  id: string;
  name: string;
  icon: string;
  status: 'draft' | 'published';
  sortOrder: number;
  widgetCount: number;
}

export interface AdminPermission {
  id: string;
  principalType: 'role' | 'user';
  principalValue: string;
  canView: boolean;
  canExport: boolean;
}
