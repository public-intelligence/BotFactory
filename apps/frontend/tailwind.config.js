/** @type {import('tailwindcss').Config} */
module.exports = {
  presets: [require('./node_modules/@public-intelligence/ui-core/tailwind.preset')],
  content: [
    './src/**/*.{html,ts}',
    './node_modules/@public-intelligence/ui-core/**/*.{html,ts,mjs}',
  ],
  theme: {
    extend: {},
  },
  plugins: [],
};
