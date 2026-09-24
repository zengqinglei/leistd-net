import { saveBlob } from './download-file';

/**
 * 下载是有副作用的：它会在跑测试的那台机器上真的存下文件。
 *
 * 曾经有人给导出写单测时调了真实下载路径，Karma 跑在真实 ChromeHeadless 里，
 * 每跑一次就往开发者的下载目录落一个文件，一度攒到上百个——用例是绿的，机器被污染了。
 * 所以这组用例对 `click()` 打桩，断言的是"发起了什么下载"，不是"文件存下来了"。
 */
describe('saveBlob', () => {
  let click: jasmine.Spy;
  let createObjectURL: jasmine.Spy;
  let revokeObjectURL: jasmine.Spy;
  let anchors: HTMLAnchorElement[];

  beforeEach(() => {
    anchors = [];
    click = spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (
      this: HTMLAnchorElement,
    ) {
      anchors.push(this);
    });
    createObjectURL = spyOn(URL, 'createObjectURL').and.returnValue('blob:stub');
    revokeObjectURL = spyOn(URL, 'revokeObjectURL');
  });

  it('按给定文件名发起一次下载', () => {
    saveBlob(new Blob(['a,b\n1,2'], { type: 'text/csv' }), 'operation-records.csv');

    expect(click).toHaveBeenCalledTimes(1);
    expect(anchors[0].download).toBe('operation-records.csv');
    expect(anchors[0].getAttribute('href')).toBe('blob:stub');
  });

  it('用完即释放 blob 引用', () => {
    const blob = new Blob(['x']);

    saveBlob(blob, 'x.txt');

    expect(createObjectURL).toHaveBeenCalledOnceWith(blob);
    // 不释放的话每下载一次就泄漏一份，直到页面关闭
    expect(revokeObjectURL).toHaveBeenCalledOnceWith('blob:stub');
  });

  it('不把锚点留在文档里', () => {
    saveBlob(new Blob(['x']), 'x.txt');

    expect(document.querySelectorAll('a[download]').length).toBe(0);
  });
});
