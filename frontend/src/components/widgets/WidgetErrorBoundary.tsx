import { Component, type ErrorInfo, type ReactNode } from 'react';

interface Props {
  children:  ReactNode;
  widgetKey: string;
}

interface State {
  hasError: boolean;
  message:  string | null;
}

/**
 * Error boundary that wraps each LiveWidget.
 * Catches render-time exceptions and shows a minimal error card instead
 * of crashing the whole module page.
 */
export class WidgetErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false, message: null };
  }

  static getDerivedStateFromError(error: unknown): State {
    const message =
      error instanceof Error ? error.message : 'Lỗi không xác định';
    return { hasError: true, message };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(
      `[WidgetErrorBoundary] widget="${this.props.widgetKey}"`,
      error,
      info.componentStack,
    );
  }

  handleRetry = () => {
    this.setState({ hasError: false, message: null });
  };

  render() {
    if (this.state.hasError) {
      return (
        <div className="hdos-card flex flex-col items-center justify-center h-full gap-3 p-4">
          <span className="text-2xl select-none">⚠️</span>
          <div className="text-center space-y-1">
            <p className="text-xs font-medium text-[--danger]">Widget gặp lỗi</p>
            {this.state.message && (
              <p className="text-[10px] text-[--tx3] font-mono break-all max-w-xs">
                {this.state.message}
              </p>
            )}
          </div>
          <button
            onClick={this.handleRetry}
            className="btn-ghost text-[10px] px-3 py-1"
          >
            Thử lại
          </button>
        </div>
      );
    }

    return this.props.children;
  }
}
