/**
 * 把内容存成本地文件（触发浏览器下载）。
 *
 * **`revokeObjectURL` 不能省**：`createObjectURL` 建的引用会一直持有整个 blob，
 * 不释放的话每下载一次就泄漏一份，直到页面关闭。
 */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}
