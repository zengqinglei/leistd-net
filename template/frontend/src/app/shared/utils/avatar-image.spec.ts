import {
  AVATAR_SIZE,
  AvatarImageRejected,
  isAvatarImageUrl,
  MAX_SOURCE_AVATAR_BYTES,
  prepareAvatarImage,
} from './avatar-image';

/** 画一张指定尺寸的 PNG 当作用户选的原图。 */
async function pngFile(width: number, height: number): Promise<File> {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d')!;
  context.fillStyle = '#3366cc';
  context.fillRect(0, 0, width, height);
  const blob = await new Promise<Blob>((resolve) => canvas.toBlob((b) => resolve(b!), 'image/png'));
  return new File([blob], 'photo.png', { type: 'image/png' });
}

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = reject;
    image.src = src;
  });
}

describe('prepareAvatarImage', () => {
  it('非正方形的原图居中裁成正方形并缩放到固定边长', async () => {
    const dataUrl = await prepareAvatarImage(await pngFile(800, 400));

    expect(dataUrl).toMatch(/^data:image\/(webp|jpeg);base64,/);
    const image = await loadImage(dataUrl);
    expect([image.naturalWidth, image.naturalHeight]).toEqual([AVATAR_SIZE, AVATAR_SIZE]);
  });

  it('不接受的类型直接拒绝，不去解码', async () => {
    const file = new File(['<svg/>'], 'logo.svg', { type: 'image/svg+xml' });

    await expectAsync(prepareAvatarImage(file)).toBeRejectedWith(new AvatarImageRejected('type'));
  });

  it('原图过大时拒绝', async () => {
    const file = new File([new Uint8Array(MAX_SOURCE_AVATAR_BYTES + 1)], 'huge.png', {
      type: 'image/png',
    });

    await expectAsync(prepareAvatarImage(file)).toBeRejectedWith(new AvatarImageRejected('size'));
  });

  it('声明是图片、内容解不开时按解码失败拒绝', async () => {
    const file = new File(['not an image'], 'fake.png', { type: 'image/png' });

    await expectAsync(prepareAvatarImage(file)).toBeRejectedWith(new AvatarImageRejected('decode'));
  });
});

describe('isAvatarImageUrl', () => {
  it('认得站内头像地址、外部地址与 data URL', () => {
    expect(isAvatarImageUrl('/api/v1/users/1/avatar?v=abc')).toBeTrue();
    expect(isAvatarImageUrl('https://example.com/a.png')).toBeTrue();
    expect(isAvatarImageUrl('data:image/webp;base64,AAAA')).toBeTrue();
    expect(isAvatarImageUrl('')).toBeFalse();
    expect(isAvatarImageUrl(undefined)).toBeFalse();
  });
});
