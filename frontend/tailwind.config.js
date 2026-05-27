/** @type {import('tailwindcss').Config} */
export default {
  content: [
    './index.html',
    './src/**/*.{js,ts,jsx,tsx}',
  ],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        // Dark navy surface palette — matches CSS --bg/--surface/--card variables
        navy: {
          950: '#0b1929',
          900: '#0d1b2e',
          800: '#132337',
          700: '#1a2f45',
          600: '#1e3352',
          500: '#264560',
          400: '#2f5878',
          300: '#3d6e94',
        },
        // Brand blue — primary interactive color
        brand: {
          700: '#1557a8',
          600: '#1a73d4',
          500: '#2684e8',
          400: '#4da3ff',
          300: '#7ec0ff',
          200: '#b3d9ff',
        },
        // Status / semantic colors
        status: {
          success: '#2ecc71',
          warning: '#f0a030',
          danger:  '#ff5252',
          info:    '#4da3ff',
          purple:  '#a29bfe',
          teal:    '#00cec9',
        },
        // Alert level colors (L1=critical, L2=urgent, L3=advisory)
        alert: {
          L1: '#ff5252',
          L2: '#f0a030',
          L3: '#4da3ff',
        },
      },
      fontFamily: {
        sans: ['Inter', 'ui-sans-serif', 'system-ui', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'sans-serif'],
        mono: ['JetBrains Mono', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'monospace'],
      },
      borderRadius: {
        'xl':  '12px',
        '2xl': '16px',
        '3xl': '24px',
      },
      boxShadow: {
        'card':  '0 2px 12px rgba(0,0,0,0.35)',
        'glow':  '0 0 20px rgba(26,115,212,0.3)',
        'inner-sm': 'inset 0 1px 3px rgba(0,0,0,0.3)',
      },
      animation: {
        'fade-in':    'fadeIn 0.2s ease-out',
        'slide-up':   'slideUp 0.25s ease-out',
        'pulse-slow': 'pulse 3s cubic-bezier(0.4,0,0.6,1) infinite',
      },
      keyframes: {
        fadeIn:  { from: { opacity: '0' },                  to: { opacity: '1' } },
        slideUp: { from: { transform: 'translateY(8px)', opacity: '0' }, to: { transform: 'translateY(0)', opacity: '1' } },
      },
    },
  },
  plugins: [],
};
