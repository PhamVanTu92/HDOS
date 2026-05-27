import { useState, useRef, useEffect, KeyboardEvent } from 'react';

interface ChatMessage {
  id?:        string;
  role:       'user' | 'assistant' | 'system';
  content:    string;
  timestamp?: string;
  pending?:   boolean;
}

interface ChatData {
  messages?:     ChatMessage[];
  placeholder?:  string;
  systemContext?: string;
}

function formatTimestamp(ts?: string): string | null {
  if (!ts) return null;
  try {
    const d = new Date(ts);
    if (isNaN(d.getTime())) return ts;
    return d.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
  } catch {
    return ts;
  }
}

function PendingDots() {
  return (
    <span className="inline-flex items-center gap-0.5" aria-label="Đang xử lý">
      <span
        className="w-1.5 h-1.5 rounded-full animate-bounce"
        style={{ background: 'var(--tx3)', animationDelay: '0ms' }}
      />
      <span
        className="w-1.5 h-1.5 rounded-full animate-bounce"
        style={{ background: 'var(--tx3)', animationDelay: '150ms' }}
      />
      <span
        className="w-1.5 h-1.5 rounded-full animate-bounce"
        style={{ background: 'var(--tx3)', animationDelay: '300ms' }}
      />
    </span>
  );
}

let _msgIdCounter = 0;
function nextId(): string {
  return `msg-${Date.now()}-${++_msgIdCounter}`;
}

export function ChatPanelWidget({ data }: { data: unknown }) {
  const d = data as ChatData | null;
  const placeholder = d?.placeholder ?? 'Nhập câu hỏi lâm sàng...';
  const initialMessages: ChatMessage[] = d?.messages ?? [];

  const [messages, setMessages] = useState<ChatMessage[]>(initialMessages);
  const [input, setInput] = useState('');
  const bottomRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);

  // Auto-scroll when messages change
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  function handleSubmit() {
    const text = input.trim();
    if (!text) return;

    const pendingId = nextId();

    setMessages(prev => [
      ...prev,
      {
        id:        nextId(),
        role:      'user',
        content:   text,
        timestamp: new Date().toISOString(),
      },
      {
        id:      pendingId,
        role:    'assistant',
        content: '',
        pending: true,
      },
    ]);
    setInput('');

    setTimeout(() => {
      setMessages(prev =>
        prev.map(m =>
          m.id === pendingId
            ? {
                ...m,
                content:   'Tính năng AI đang được tích hợp. Vui lòng liên hệ nhà cung cấp để kích hoạt.',
                pending:   false,
                timestamp: new Date().toISOString(),
              }
            : m
        )
      );
    }, 1500);
  }

  function handleKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  }

  const isEmpty = messages.length === 0;

  return (
    <div className="flex flex-col h-full overflow-hidden">

      {/* System context banner */}
      {d?.systemContext && (
        <div
          className="flex items-start gap-1.5 px-3 py-1.5 shrink-0 text-[10px] rounded"
          style={{
            background: 'var(--info-bg)',
            color:      'var(--info)',
            borderBottom: '1px solid var(--border)',
          }}
        >
          <span className="shrink-0">ℹ️</span>
          <span>{d.systemContext}</span>
        </div>
      )}

      {/* Message area */}
      <div className="flex-1 overflow-y-auto px-3 py-3 flex flex-col gap-2">
        {isEmpty ? (
          <div className="flex flex-col items-center justify-center h-full gap-2 select-none">
            <span className="text-3xl">🤖</span>
            <p className="text-sm font-medium text-[--tx2]">Trợ lý AI lâm sàng</p>
            <p className="text-xs text-[--tx3]">Nhập câu hỏi để bắt đầu</p>
          </div>
        ) : (
          messages.map((msg, idx) => {
            const key = msg.id ?? `${idx}`;

            if (msg.role === 'system') {
              return (
                <div key={key} className="text-center">
                  <span className="text-[10px] italic text-[--tx3]">{msg.content}</span>
                </div>
              );
            }

            const isUser = msg.role === 'user';
            const ts = formatTimestamp(msg.timestamp);

            return (
              <div
                key={key}
                className={`flex flex-col gap-0.5 ${isUser ? 'items-end' : 'items-start'}`}
              >
                <div
                  className="px-3 py-2 rounded-xl text-sm leading-relaxed"
                  style={{
                    maxWidth:   '80%',
                    background: isUser ? 'var(--brand-dim)' : 'var(--overlay)',
                    border:     isUser
                      ? '1px solid var(--brand)'
                      : '1px solid var(--border)',
                    color:      'var(--tx)',
                  }}
                >
                  {msg.pending ? <PendingDots /> : msg.content}
                </div>
                {ts && (
                  <span
                    className="text-[10px]"
                    style={{ color: 'var(--tx3)' }}
                  >
                    {ts}
                  </span>
                )}
              </div>
            );
          })
        )}
        <div ref={bottomRef} />
      </div>

      {/* Input area */}
      <div
        className="flex items-end gap-2 px-3 py-2 shrink-0"
        style={{ borderTop: '1px solid var(--border)' }}
      >
        <textarea
          ref={inputRef}
          rows={1}
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          className="flex-1 resize-none rounded-lg px-3 py-2 text-sm outline-none transition-all"
          style={{
            background:    'var(--overlay)',
            border:        '1px solid var(--border)',
            color:         'var(--tx)',
            minHeight:     '36px',
            maxHeight:     '72px',
            lineHeight:    '1.4',
            fieldSizing:   'content',
          }}
        />
        <button
          onClick={handleSubmit}
          disabled={!input.trim()}
          className="shrink-0 flex items-center justify-center w-9 h-9 rounded-lg text-sm font-medium transition-opacity"
          style={{
            background: 'var(--brand)',
            color:      'var(--bg)',
            opacity:    input.trim() ? 1 : 0.4,
            cursor:     input.trim() ? 'pointer' : 'default',
          }}
          aria-label="Gửi"
        >
          →
        </button>
      </div>
    </div>
  );
}
