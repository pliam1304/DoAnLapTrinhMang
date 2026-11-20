import { themes as prismThemes } from 'prism-react-renderer';
import type { Config } from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Hệ Thống Giám Sát Phòng Máy & Remote Control (Nega)',
  tagline: 'Tài Liệu Kỹ Thuật & Báo Cáo Đồ Án Lập Trình Mạng (.NET 8 / Socket UDP & TCP)',
  favicon: 'img/favicon.ico',

  url: 'https://pliam1304.github.io',
  baseUrl: '/DoAnLapTrinhMang/',

  organizationName: 'pliam1304',
  projectName: 'DoAnLapTrinhMang',
  trailingSlash: false,

  onBrokenLinks: 'warn',

  markdown: {
    mermaid: true,
  },
  themes: ['@docusaurus/theme-mermaid'],

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          path: '../docs',
          routeBasePath: '/',
          editUrl: 'https://github.com/pliam1304/DoAnLapTrinhMang/tree/main/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    colorMode: {
      defaultMode: 'dark',
      disableSwitch: false,
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'Nega Docs',
      logo: {
        alt: 'Nega Logo',
        src: 'img/logo.png',
        srcDark: 'img/logo.png',
        width: 30,
        height: 30,
      },
      items: [
        {
          type: 'doc',
          docId: 'index',
          position: 'left',
          label: 'Tổng Quan',
        },
        {
          type: 'doc',
          docId: 'dac-ta-ung-dung/tong-quan-yeu-cau',
          position: 'left',
          label: 'Đặc Tả Ứng Dụng',
        },
        {
          type: 'doc',
          docId: 'ket-qua-dat-duoc/tong-ket-chuc-nang',
          position: 'left',
          label: 'Kết Quả Đạt Được',
        },
        {
          type: 'doc',
          docId: 'nhat-ky-va-minh-chung/lich-su-commit-github',
          position: 'left',
          label: 'Nhật Ký & Minh Chứng',
        },
        {
          href: 'https://github.com/pliam1304/DoAnLapTrinhMang',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      copyright: `Đồ án Học phần Lập Trình Mạng — Hệ thống Nega (.NET 8, UDP Broadcast & TCP Streaming)`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
