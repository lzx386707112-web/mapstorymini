// 嵌入版构建配置：把后台前端打成「可塞进 Unity Resources 的静态包」。
//
// 与 config/vite.config.prod.ts 的差别只有打包产物相关的部分：
//   · 去掉 imagemin（要下二进制，构建机上经常失败，且后台界面用不到）
//   · 去掉 gzip 预压缩与 visualizer（Unity 侧的本地服务不做 gzip 协商）
//   · 输出到 dist-embed/，由 tools/pack_admin_ui.py 打成 adminui.bytes
// 业务代码、路由、样式一律不动。
import { mergeConfig } from 'vite';
import baseConfig from './vite.config.base';
import configArcoResolverPlugin from './plugin/arcoResolver';

export default mergeConfig(
  {
    mode: 'production',
    build: {
      outDir: 'dist-embed',
      emptyOutDir: true,
      sourcemap: false,
      chunkSizeWarningLimit: 4000,
      rollupOptions: {
        output: {
          manualChunks: {
            arco: ['@arco-design/web-vue'],
            chart: ['echarts', 'vue-echarts'],
            vue: ['vue', 'vue-router', 'pinia', '@vueuse/core', 'vue-i18n'],
          },
        },
      },
    },
    plugins: [configArcoResolverPlugin()],
  },
  baseConfig
);
