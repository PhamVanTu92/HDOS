export function RawJsonWidget({ data, chartType }: { data: unknown; chartType?: string }) {
  return (
    <div className="h-full overflow-auto">
      {chartType && chartType !== 'raw_json' && (
        <p className="text-xs text-[--warning] mb-2">
          ⚠ Widget type "{chartType}" — renderer not yet implemented. Showing raw data.
        </p>
      )}
      <pre className="text-[10px] text-[--tx2] font-mono whitespace-pre-wrap break-all leading-relaxed">
        {JSON.stringify(data, null, 2)}
      </pre>
    </div>
  );
}
