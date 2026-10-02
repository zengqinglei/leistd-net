import { saveBlob } from './download-file';

import type { Mock } from 'vitest';

/**
 * 下载是有副作用的：它会在跑测试的那台机器上真的存下文件。
 *
 * 曾经有人给导出写单测时调了真实下载路径，单测跑在真实的浏览器里，
 * 每跑一次就往开发者的下载目录落一个文件，一度攒到上百个——用例是绿的，机器被污染了。
 * 所以这组用例对 `click()` 打桩，断言的是"发起了什么下载"，不是"文件存下来了"。
 */
describe('saveBlob', () => {
  let click: Mock;
  let createObjectURL: Mock;
  let revokeObjectURL: Mock;
  let anchors: HTMLAnchorElement[];

  beforeEach(() => {
    anchors = [];
    click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      anchors.push(this);
    });
    createObjectURL = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:stub');
    revokeObjectURL = vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);
  });

  it('triggers a single download with the given file name', () => {
    saveBlob(new Blob(['a,b\n1,2'], { type: 'text/csv' }), 'operation-records.csv');

    expect(click).toHaveBeenCalledTimes(1);
    expect(anchors[0].download).toBe('operation-records.csv');
    expect(anchors[0].getAttribute('href')).toBe('blob:stub');
  });

  it('revokes the blob URL once used', () => {
    const blob = new Blob(['x']);

    saveBlob(blob, 'x.txt');

    expect(createObjectURL).toHaveBeenCalledTimes(1);

    expect(createObjectURL).toHaveBeenCalledWith(blob);
    // 不释放的话每下载一次就泄漏一份，直到页面关闭
    expect(revokeObjectURL).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:stub');
  });

  it('does not leave the anchor in the document', () => {
    saveBlob(new Blob(['x']), 'x.txt');

    expect(document.querySelectorAll('a[download]').length).toBe(0);
  });
});
