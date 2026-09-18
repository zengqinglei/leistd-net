import { describeUserAgent } from './user-agent';

describe('describeUserAgent', () => {
  it('认出桌面 Chrome 与 macOS', () => {
    expect(
      describeUserAgent(
        'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36',
      ),
    ).toEqual({ browser: 'Chrome 128', os: 'macOS', kind: 'desktop' });
  });

  it('Edge 带着 Chrome 标记，但要认成 Edge', () => {
    expect(
      describeUserAgent(
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.2739.42',
      ),
    ).toEqual({ browser: 'Edge 128', os: 'Windows', kind: 'desktop' });
  });

  it('iPhone 上的 Safari 是手机', () => {
    expect(
      describeUserAgent(
        'Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1',
      ),
    ).toEqual({ browser: 'Safari 17', os: 'iOS', kind: 'mobile' });
  });

  it('不带 Mobile 的 Android 是平板', () => {
    expect(
      describeUserAgent(
        'Mozilla/5.0 (Linux; Android 14; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36',
      ).kind,
    ).toBe('tablet');
  });

  it('认不出时各项为空，按桌面处理', () => {
    expect(describeUserAgent('curl/8.7.1')).toEqual({ browser: null, os: null, kind: 'desktop' });
    expect(describeUserAgent(null)).toEqual({ browser: null, os: null, kind: 'desktop' });
  });
});
