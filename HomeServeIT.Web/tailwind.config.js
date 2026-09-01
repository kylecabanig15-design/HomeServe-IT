/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './Pages/**/*.cshtml',
    './Views/**/*.cshtml',
    './Areas/**/*.cshtml',
    './wwwroot/js/**/*.js'
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'sans-serif'],
      },
      colors: {
        primary: '#0878f9',
        'primary-hover': '#0066d6',
        dark: '#17191c',
        muted: '#5d6470',
        light: '#f8f9fa'
      }
    },
  },
  plugins: [],
}
