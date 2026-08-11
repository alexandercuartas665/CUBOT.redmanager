/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // Paleta CUBOT (misma que la web)
        cubot: {
          primary: '#A03DC9',   // morado brand
          magenta: '#C7398B',   // magenta acento
          dark: '#1F1235',      // fondo dark
          soft: '#F4EEF9',      // fondo suave (cards, hovers)
        },
      },
      fontFamily: {
        sans: ['system-ui', '-apple-system', 'Segoe UI', 'Roboto', 'sans-serif'],
      },
    },
  },
  plugins: [],
};
