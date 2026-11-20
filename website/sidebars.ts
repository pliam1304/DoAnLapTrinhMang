import type { SidebarsConfig } from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docsSidebar: [
    {
      type: 'doc',
      id: 'index',
      label: 'Tổng Quan Dự Án',
    },
    {
      type: 'category',
      label: 'Phần I: Đặc Tả Ứng Dụng',
      collapsed: false,
      items: [
        'dac-ta-ung-dung/tong-quan-yeu-cau',
        'dac-ta-ung-dung/kien-truc-he-thong',
        'dac-ta-ung-dung/giao-thuc-va-du-lieu',
        'dac-ta-ung-dung/ma-tran-phan-quyen',
        'dac-ta-ung-dung/design-system-ui-ux',
        'dac-ta-ung-dung/quy-chuan-lap-trinh',
        {
          type: 'category',
          label: 'Đặc Tả 8 Chức Năng',
          collapsed: false,
          items: [
            'dac-ta-ung-dung/chuc-nang/F01-tu-dong-nhan-dien-udp-lan',
            'dac-ta-ung-dung/chuc-nang/F02-truyen-luong-jpeg-tcp-va-zoom-hd',
            'dac-ta-ung-dung/chuc-nang/F03-khoa-chuot-ban-phim-fail-safe',
            'dac-ta-ung-dung/chuc-nang/F04-thong-bao-khan-va-xac-nhan-ack',
            'dac-ta-ung-dung/chuc-nang/F05-tat-may-tu-xa-va-safe-mode',
            'dac-ta-ung-dung/chuc-nang/F06-cap-tai-khoan-phan-quyen-va-qr-code',
            'dac-ta-ung-dung/chuc-nang/F07-cum-gia-lap-va-telemetry',
            'dac-ta-ung-dung/chuc-nang/F08-dinh-huong-thi-cu-2-vai-tro',
          ],
        },
      ],
    },
    {
      type: 'category',
      label: 'Phần II: Kết Quả Đạt Được',
      collapsed: false,
      items: [
        'ket-qua-dat-duoc/tong-ket-chuc-nang',
        'ket-qua-dat-duoc/kiem-thu-unit-test',
        'ket-qua-dat-duoc/toi-uu-hieu-nang',
        'ket-qua-dat-duoc/huong-dan-cai-dat-khoi-chay',
      ],
    },
    {
      type: 'category',
      label: 'Phần III: Minh Chứng & Nhật Ký',
      collapsed: false,
      items: [
        'nhat-ky-va-minh-chung/lich-su-commit-github',
        'nhat-ky-va-minh-chung/nhat-ky-quyet-dinh-ky-thuat',
        'nhat-ky-va-minh-chung/nhat-ky-su-co-va-sua-loi',
      ],
    },
  ],
};

export default sidebars;
