import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    outDir: '../wwwroot',
    assetsDir: 'react-assets',
    emptyOutDir: false,
  },
  server: {
    proxy: { '/api': 'http://localhost:5072' },
    fs: { allow: ['..'] },
  },
});
