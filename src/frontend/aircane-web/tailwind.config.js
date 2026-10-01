/** @type {import('tailwindcss').Config} */
export default {
  content: [
    './index.html',
    './src/**/*.{vue,js,ts,jsx,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        // Aircane brand palette - purple/violet tones
        'aircane': {
          50:  '#f5f0ff',
          100: '#ede5ff',
          200: '#daccff',
          300: '#c2a6ff',
          400: '#a67aff',
          500: '#8b4fff',
          600: '#7c3aed',
          700: '#6d28d9',
          800: '#5b21b6',
          900: '#4c1d95',
          950: '#2e1065',
        },
        // Dark surface palette - purple-tinted darks
        'surface': {
          50:  '#f8f6fc',
          100: '#eee9f7',
          200: '#d8ceed',
          300: '#b8a5db',
          400: '#9478c4',
          500: '#7654ab',
          600: '#5f3f8f',
          700: '#4a3070',
          800: '#1e1533',
          850: '#181028',
          900: '#130d20',
          950: '#0d0916',
        },
      },
      backgroundImage: {
        'gradient-radial': 'radial-gradient(var(--tw-gradient-stops))',
      },
    },
  },
  plugins: [],
}
