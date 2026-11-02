import { loadEnv } from 'vite';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
    const env = loadEnv(mode, '.', '');
    const apiTarget = env.VITE_API_PROXY_TARGET ?? 'http://localhost:5080';

    return {
        plugins: [react()],
        test: {
            environment: 'jsdom',
            setupFiles: ['./src/test/setup.ts'],
        },
        server: {
            proxy: {
                '/api': {
                    target: apiTarget,
                    changeOrigin: true,
                },
                '/health': {
                    target: apiTarget,
                    changeOrigin: true,
                },
            },
        },
    };
});
