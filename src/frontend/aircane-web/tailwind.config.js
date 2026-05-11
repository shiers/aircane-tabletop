/** @type {import('tailwindcss').Config} */
export default {
  content: [
    './index.html',
    './src/**/*.{vue,js,ts,jsx,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        // Aircane brand palette — extend as needed
        'aircane': {
          50:  '#f0f4ff',
          100: '#dde6ff',
          200: '#c3d0ff',
          300: '#9db0ff',
          400: '#7485ff',
          500: '#4f5bff',
          600: '#3a3ef5',
          700: '#2e2fd8',
          800: '#2828ae',
          900: '#272889',
          950: '#181852',
        },
      },
    },
  },
  plugins: [],
}
